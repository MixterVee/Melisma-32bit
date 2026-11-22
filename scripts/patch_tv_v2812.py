#!/usr/bin/env python3
"""Melisma Universal v2.8.12 optional Firebase cloud sync (v2.11.0 baseline).

Apply AFTER patch_tv_v2811.py. Adds a full opt-in Firebase Email/Password +
Firestore client with TV-friendly configuration. Local corrections remain
authoritative for playback. No Firebase credentials are embedded in the APK.
No changes to TV artwork, Amazon Music, or existing Settings backup.
"""
from pathlib import Path
import sys
root=Path(sys.argv[1]) if len(sys.argv)>1 else Path("melisma")
base=root/"app/src/main/java/com/melisma/app"
cloud=base/"cloud"
cloud.mkdir(exist_ok=True)

def once(s, old, new, label):
    n=s.count(old)
    if n != 1: raise RuntimeError(f"{label}: expected one match, got {n}")
    return s.replace(old, new, 1)

manager=cloud/"CloudSyncManager.kt"
if manager.exists(): raise RuntimeError("CloudSyncManager exists")
manager.write_text(r'''package com.melisma.app.cloud

import android.content.Context
import com.google.firebase.FirebaseApp
import com.google.firebase.FirebaseOptions
import com.google.firebase.auth.FirebaseAuth
import com.google.firebase.firestore.DocumentChange
import com.google.firebase.firestore.FieldValue
import com.google.firebase.firestore.FirebaseFirestore
import com.google.firebase.firestore.ListenerRegistration
import com.google.firebase.firestore.Source
import com.melisma.app.settings.Settings
import com.melisma.app.settings.SettingsStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.launch
import org.json.JSONArray
import org.json.JSONObject
import java.security.MessageDigest

/**
 * Optional personal cloud syncing of source selection and TV timing only.
 * - Never uploads lyrics, music, account tokens or other Settings.
 * - Google Firebase Authentication and owner-scoped Firestore security rules
 *   must be configured in the user's own Firebase project.
 * - Firebase SDK manages authentication persistence and offline queued writes.
 * - Deletions are tombstones, so Reset/Automatic sync across TVs.
 * - Local SettingsStore always drives playback; cloud is asynchronous.
 */
class CloudSyncManager(
    context: Context,
    private val store: SettingsStore,
    private val scope: CoroutineScope,
) {
    private val appContext = context.applicationContext
    private val prefs = appContext.getSharedPreferences("melisma_cloud", Context.MODE_PRIVATE)
    private val lock = Any()
    private var app: FirebaseApp? = null
    private var auth: FirebaseAuth? = null
    private var firestore: FirebaseFirestore? = null
    private var listener: ListenerRegistration? = null
    @Volatile private var connected = false

    /** Values most recently observed locally. Missing means deleted. */
    private var localShadow = snapshot(store.current)
    /** Pending edits stay recorded while offline or signed out. */
    private val pending = loadPending()
    private val _status = MutableStateFlow("Cloud sync off · corrections saved locally")
    val status: StateFlow<String> = _status.asStateFlow()

    val projectId get() = prefs.getString("project_id", "").orEmpty()
    val appId get() = prefs.getString("app_id", "").orEmpty()
    val apiKey get() = prefs.getString("api_key", "").orEmpty()

    init {
        scope.launch {
            store.settings.collect { settings ->
                val now = snapshot(settings)
                val changed: List<Pair<String, String?>>
                synchronized(lock) {
                    changed = (now.keys + localShadow.keys).distinct()
                        .filter { now[it] != localShadow[it] }.map { it to now[it] }
                    localShadow = now
                    if (changed.isNotEmpty()) {
                        for ((key, value) in changed) pending[key] = value
                        savePending()
                    }
                }
                if (connected) for ((key, value) in changed) publish(key, value)
            }
        }
        if (projectId.isNotBlank() && appId.isNotBlank() && apiKey.isNotBlank())
            initializeFirebase()
    }

    /** Firebase configuration identifiers are public, not passwords. */
    fun configure(project: String, applicationId: String, webKey: String) {
        val p = project.trim()
        val a = applicationId.trim()
        val k = webKey.trim()
        if (!Regex("[a-z0-9][a-z0-9-]{3,63}").matches(p) ||
            !Regex("1:[0-9]+:android:[A-Za-z0-9]+").matches(a) ||
            k.length < 20) {
            _status.value = "Invalid Firebase project ID, Android App ID or API key"
            return
        }
        val changed = projectId != p || appId != a || apiKey != k
        prefs.edit().putString("project_id", p).putString("app_id", a)
            .putString("api_key", k).apply()
        if (changed && app != null) {
            listener?.remove()
            connected = false
            _status.value = "Firebase configuration changed · restart Melisma"
        } else if (app == null) initializeFirebase()
        else _status.value = if (auth?.currentUser != null) "Connected" else "Configured · sign in"
    }

    private fun initializeFirebase() {
        try {
            val options = FirebaseOptions.Builder()
                .setApplicationId(appId)
                .setApiKey(apiKey)
                .setProjectId(projectId)
                .build()
            val firebaseApp = FirebaseApp.getApps(appContext).firstOrNull { it.name == "melisma-cloud" }
                ?: FirebaseApp.initializeApp(appContext, options, "melisma-cloud")
            app = firebaseApp
            auth = FirebaseAuth.getInstance(firebaseApp)
            firestore = FirebaseFirestore.getInstance(firebaseApp)
            _status.value = "Configured · sign in"
            if (auth?.currentUser != null) beginListening()
        } catch (t: Exception) {
            _status.value = "Firebase setup failed: " + (t.localizedMessage ?: "Invalid configuration")
        }
    }

    fun signIn(email: String, password: String) {
        val a = auth ?: run {
            _status.value = "Save Firebase project settings first"
            return
        }
        if (email.isBlank() || password.isBlank()) {
            _status.value = "Enter the Firebase account email and password"
            return
        }
        _status.value = "Signing in..."
        a.signInWithEmailAndPassword(email.trim(), password)
            .addOnSuccessListener { beginListening() }
            .addOnFailureListener {
                _status.value = "Sign-in failed: " + (it.localizedMessage ?: "Check account")
            }
    }

    fun signOut() {
        connected = false
        listener?.remove()
        listener = null
        auth?.signOut()
        _status.value = "Signed out · local corrections preserved"
    }

    private fun collection() = firestore!!.collection("users")
        .document(auth!!.currentUser!!.uid).collection("corrections")

    private fun beginListening() {
        listener?.remove()
        connected = false
        val user = auth?.currentUser ?: run {
            _status.value = "Not signed in"
            return
        }
        if (firestore == null) return
        _status.value = "Connecting to cloud for " + (user.email ?: "account")
        var firstSnapshot = true
        listener = collection().addSnapshotListener { snap, error ->
            if (error != null) {
                _status.value = "Cloud unavailable: " + (error.localizedMessage ?: "network")
                return@addSnapshotListener
            }
            if (snap == null) return@addSnapshotListener
            if (firstSnapshot) {
                firstSnapshot = false
                val remoteKeys = snap.documents.mapNotNull { it.getString("key") }.toSet()
                for (doc in snap.documents) {
                    val key = doc.getString("key") ?: continue
                    if (key.length > 800 || (!key.startsWith("s:") && !key.startsWith("o:"))) continue
                    val value = if (doc.getBoolean("deleted") == true) null else doc.getString("value")
                    handleRemote(key, value, doc.metadata.hasPendingWrites())
                }
                // Existing unsynced corrections on a newly linked device are
                // seeded only if the cloud has no record for that key.
                val local = snapshot(store.current)
                connected = true
                for ((key, value) in local) if (key !in remoteKeys) publish(key, value)
                val outstanding = synchronized(lock) { pending.toMap() }
                for ((key, value) in outstanding) publish(key, value)
            } else {
                for (change in snap.documentChanges) {
                    if (change.type == DocumentChange.Type.REMOVED) continue
                    val doc = change.document
                    val key = doc.getString("key") ?: continue
                    if (key.length > 800 || (!key.startsWith("s:") && !key.startsWith("o:"))) continue
                    val value = if (doc.getBoolean("deleted") == true) null else doc.getString("value")
                    handleRemote(key, value, doc.metadata.hasPendingWrites())
                }
            }
            _status.value = if (snap.metadata.isFromCache) {
                "Offline · changes queued safely"
            } else "Connected · corrections syncing automatically"
        }
    }

    private fun handleRemote(key: String, value: String?, localWrite: Boolean) {
        // Never overwrite pending device edits with an older cloud response.
        val hasPending = synchronized(lock) { pending.containsKey(key) }
        if (hasPending) {
            if (!localWrite) {
                val identical = synchronized(lock) { pending[key] == value }
                if (identical) synchronized(lock) {
                    pending.remove(key)
                    savePending()
                }
            }
            return
        }
        // The local Firestore cache replays writes; they are not remote changes.
        if (localWrite) return
        if (snapshot(store.current)[key] == value) return
        // Set shadow before the settings StateFlow updates, preventing echo writes.
        synchronized(lock) {
            val next = localShadow.toMutableMap()
            if (value == null) next.remove(key) else next[key] = value
            localShadow = next
        }
        if (key.startsWith("s:")) {
            store.setSourceChoice(key.substring(2), value)
        } else if (key.startsWith("o:")) {
            val full = key.substring(2)
            val split = full.lastIndexOf('\t')
            if (split <= 0) return
            val provider = full.substring(split + 1).takeUnless { it == "#automatic" }
            val ms = value?.toIntOrNull() ?: 0
            if (ms in SettingsStore.SYNC_OFFSET_RANGE) {
                store.setSongOffset(full.substring(0, split), provider, ms)
            }
        }
    }

    private fun publish(key: String, value: String?) {
        val c = try { collection() } catch (_: Exception) { return }
        val digest = MessageDigest.getInstance("SHA-256")
            .digest(key.toByteArray(Charsets.UTF_8))
            .joinToString("") { "%02x".format(it) }
        val data = hashMapOf<String, Any>(
            "key" to key,
            "value" to (value ?: ""),
            "deleted" to (value == null),
            "updatedAt" to FieldValue.serverTimestamp(),
        )
        c.document(digest).set(data).addOnFailureListener {
            _status.value = "Sync retry pending: " + (it.localizedMessage ?: "cloud error")
        }
    }

    fun syncNow() {
        if (auth?.currentUser == null || firestore == null) {
            _status.value = "Sign in before syncing"
            return
        }
        _status.value = "Checking cloud..."
        collection().get(Source.SERVER)
            .addOnSuccessListener { result ->
                // The realtime listener receives any server changes as well.
                _status.value = "Connected · " + result.size() + " saved cloud entries"
            }
            .addOnFailureListener {
                _status.value = "Cloud check failed: " + (it.localizedMessage ?: "network")
            }
    }

    private fun snapshot(state: Settings): Map<String, String> {
        val map = LinkedHashMap<String, String>()
        for ((key, source) in state.sourceChoices) map["s:" + key] = source
        for ((key, offset) in state.songOffsets) map["o:" + key] = offset.toString()
        return map
    }

    private fun loadPending(): MutableMap<String, String?> {
        val out = LinkedHashMap<String, String?>()
        try {
            val arr = JSONArray(prefs.getString("pending", "[]"))
            for (i in 0 until minOf(arr.length(), 10000)) {
                val e = arr.getJSONObject(i)
                val key = e.getString("key")
                out[key] = if (e.optBoolean("deleted")) null else e.getString("value")
            }
        } catch (_: Exception) { /* leave local corrections untouched */ }
        return out
    }

    private fun savePending() {
        val arr = JSONArray()
        for ((key, value) in pending) arr.put(JSONObject()
            .put("key", key).put("value", value ?: "")
            .put("deleted", value == null))
        prefs.edit().putString("pending", arr.toString()).apply()
    }
}
''', encoding="utf-8")

gradle=root/"app/build.gradle.kts"
g=gradle.read_text(encoding="utf-8")
g=once(g,"dependencies {\n    implementation(libs.androidx.core.ktx)",
"""dependencies {
    // Explicit opt-in Firebase cloud sync. Initialized at runtime with user-provided
    // Firebase project values; no google-services.json or embedded account keys.
    implementation(platform("com.google.firebase:firebase-bom:35.2.0"))
    implementation("com.google.firebase:firebase-auth")
    implementation("com.google.firebase:firebase-firestore")
    implementation(libs.androidx.core.ktx)""","Firebase Auth/Firestore deps")
gradle.write_text(g,encoding="utf-8")

app=base/"MelismaApp.kt"
s=app.read_text(encoding="utf-8")
s=once(s,"import com.melisma.app.settings.SettingsStore\n",
"""import com.melisma.app.settings.SettingsStore
import com.melisma.app.cloud.CloudSyncManager
""","container cloud import")
s=once(s,"    val settings = SettingsStore(context)\n    val media = MediaSessionRepository(context, settings)",
"""    val settings = SettingsStore(context)
    val cloudSync = CloudSyncManager(context, settings, scope)
    val media = MediaSessionRepository(context, settings)""","cloud owned by app container")
app.write_text(s,encoding="utf-8")

settings=base/"ui/SettingsSheet.kt"
s=settings.read_text(encoding="utf-8")
s=once(s,"import androidx.compose.material3.ModalBottomSheet\n",
"""import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedTextField
""","cloud settings imports")
s=once(s,"import androidx.compose.ui.text.input.ImeAction\n",
"""import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.PasswordVisualTransformation
""","cloud password masking import")
s=once(s,
"""            Section(
                title = "Storage and backup",""",
"""            Section(
                title = "Cloud sync",
                subtitle = "Optional Firebase sync of song sources and timing across TVs",
                open = openSection == "cloud",
                accent = accent,
                onToggle = { openSection = if (openSection == "cloud") null else "cloud" },
            ) {
                val sync = container.cloudSync
                val state by sync.status.collectAsState()
                var projectId by remember { mutableStateOf(sync.projectId) }
                var androidAppId by remember { mutableStateOf(sync.appId) }
                var webApiKey by remember { mutableStateOf(sync.apiKey) }
                var loginEmail by remember { mutableStateOf("") }
                var loginPassword by remember { mutableStateOf("") }
                Text(
                    "Optional. Melisma always saves corrections locally, even when cloud sync is off. " +
                        "Configure your own Firebase project and sign in to the SAME account on each TV.",
                    fontSize = 12.sp,
                    color = Color.White.copy(alpha = 0.68f),
                )
                Spacer(Modifier.height(8.dp))
                Text(state, color = accent, fontSize = 12.sp)
                Spacer(Modifier.height(10.dp))
                OutlinedTextField(
                    value = projectId,
                    onValueChange = { projectId = it },
                    label = { Text("Firebase Project ID") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
                OutlinedTextField(
                    value = androidAppId,
                    onValueChange = { androidAppId = it },
                    label = { Text("Firebase Android App ID") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
                OutlinedTextField(
                    value = webApiKey,
                    onValueChange = { webApiKey = it },
                    label = { Text("Firebase Web API key") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
                Button(onClick = { sync.configure(projectId, androidAppId, webApiKey) }) {
                    Text("Save Firebase configuration")
                }
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = loginEmail,
                    onValueChange = { loginEmail = it },
                    label = { Text("Account email") },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
                OutlinedTextField(
                    value = loginPassword,
                    onValueChange = { loginPassword = it },
                    label = { Text("Account password") },
                    visualTransformation = PasswordVisualTransformation(),
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    Button(onClick = {
                        sync.signIn(loginEmail, loginPassword)
                        loginPassword = ""
                    }) { Text("Sign in") }
                    Button(onClick = { sync.syncNow() }) { Text("Sync now") }
                    Button(onClick = { sync.signOut() }) { Text("Sign out") }
                }
                Help(
                    "Only preferred lyrics providers and timing corrections sync. Lyrics, music, " +
                        "API credentials and other settings never upload. Firebase email/password " +
                        "authentication and private Firestore access rules must be configured " +
                        "first. Use the same account on each device."
                )
            }

            Section(
                title = "Storage and backup",""",
"cloud settings section before backup")
settings.write_text(s,encoding="utf-8")
print("v2.8.12: optional owner-authenticated Firebase Firestore sync with local fallback and Settings UI.")
