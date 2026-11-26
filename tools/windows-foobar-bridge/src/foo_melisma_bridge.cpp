// Melisma Foobar2000 Bridge v0.4
// Read-only playback timeline for local Windows karaoke. No audio modifications,
// playback commands, network listeners, filesystem or registry access.
// Foobar2000 SDK playback_control requires the main thread. The Win32 timer
// runs there and publishes a small versioned shared-memory snapshot.
#include <foobar2000/SDK/foobar2000.h>
#include <windows.h>
#include <cstdint>
#include <cmath>
#include <cstddef>
#include <algorithm>
#include <cstring>

namespace {
constexpr wchar_t kMappingName[] = L"Local\\MelismaFoobarBridge_v1";
constexpr wchar_t kMetaName[] = L"Local\\MelismaFoobarMetadata_v1";
constexpr wchar_t kArtName[] = L"Local\\MelismaFoobarArtwork_v1";
constexpr wchar_t kWriterLockName[] = L"Local\\MelismaFoobarWriterLock_v1";
constexpr std::uint32_t kMaxArtBytes = 8 * 1024 * 1024;
constexpr std::uint32_t kMagic = 0x4D454C49; // "MELI"
constexpr std::uint32_t kVersion = 1;

struct alignas(8) BridgeSnapshot {
    std::uint32_t magic;        // +0
    std::uint32_t version;      // +4
    volatile LONG sequence;     // +8: odd while writer updates, even when stable
    LONG playbackState;         // +12: 0 stopped, 1 playing, 2 paused
    std::int64_t positionUs;    // +16: Foobar2000 reported elapsed track time
    std::int64_t durationUs;    // +24: negative if unknown
    std::uint64_t tickMs;       // +32: GetTickCount64 when position was sampled
    std::uint64_t generation;   // +40: incremented when a new track starts
};
static_assert(offsetof(BridgeSnapshot, positionUs) == 16);
static_assert(offsetof(BridgeSnapshot, durationUs) == 24);
static_assert(offsetof(BridgeSnapshot, tickMs) == 32);
static_assert(sizeof(BridgeSnapshot) == 48);

struct alignas(8) MetadataSnapshot {
    std::uint32_t magic, version;
    volatile LONG sequence;
    LONG reserved;
    std::uint64_t generation;
    char title[256], artist[256], album[256];
};
static_assert(sizeof(MetadataSnapshot) == 792);
static_assert(offsetof(MetadataSnapshot, artist) == 280);
HANDLE g_metaMapping = nullptr;
MetadataSnapshot* g_meta = nullptr;
// A third optional mapping publishes the exact front cover retrieved by
// foobar2000's shared now-playing artwork loader. No image files, paths or
// tag edits are written, and the v0.1/v0.2 mappings are unchanged.
struct alignas(8) ArtworkSnapshot {
    std::uint32_t magic, version;  // +0, +4
    volatile LONG sequence;       // +8: odd while writing
    LONG state;                   // +12: 0 loading, 1 image, 2 no art
    std::uint64_t generation;     // +16: match timeline + tag identity
    std::uint32_t byteCount;      // +24
    std::uint32_t reserved;       // +28
    std::uint8_t bytes[kMaxArtBytes]; // +32
};
static_assert(offsetof(ArtworkSnapshot, bytes) == 32);
HANDLE g_artMapping = nullptr;
ArtworkSnapshot* g_art = nullptr;
now_playing_album_art_notify* g_artNotify = nullptr;
bool g_embeddedCoverActive = false;

HANDLE g_mapping = nullptr;
BridgeSnapshot* g_shared = nullptr;
UINT_PTR g_timer = 0;
std::uint64_t g_generation = 0;
HANDLE g_writerLock = nullptr;
bool g_writerOwns = false;
bool g_publishing = false;
bool g_mappingWasExisting = false;
void tryStartPublisher() noexcept;

void publishArtwork(album_art_data::ptr data, bool finished = true) noexcept {
    if (g_art == nullptr) return;
    const std::size_t size = data.is_valid() ? data->size() : 0;
    const bool usable = finished && size > 0 && size <= kMaxArtBytes;
    InterlockedIncrement(&g_art->sequence);
    MemoryBarrier();
    g_art->generation = g_generation;
    g_art->byteCount = usable ? static_cast<std::uint32_t>(size) : 0;
    g_art->state = !finished ? 0 : usable ? 1 : 2;
    if (usable) std::memcpy(g_art->bytes, data->data(), size);
    MemoryBarrier();
    InterlockedIncrement(&g_art->sequence);
}


// For files with an embedded front cover, prefer that EXACT image over
// external folder images, online lookups or Windows media-session thumbnails.
// Called once per new Foobar track, not during playback/audio callbacks.
bool publishEmbeddedFrontCover(metadb_handle_ptr track) noexcept {
    if (g_art == nullptr || track.is_empty()) return false;
    try {
        abort_callback_dummy abort;
        auto extractor = album_art_extractor::g_open(
            file_ptr(), track->get_path(), abort);
        auto embedded = extractor->query(album_art_ids::cover_front, abort);
        if (embedded.is_valid() && embedded->size() > 128 &&
            embedded->size() <= kMaxArtBytes) {
            publishArtwork(embedded);
            return true;
        }
    } catch (...) {
        // No supported embedded cover: the centralized Foobar artwork
        // service can still supply an external/folder front cover.
    }
    return false;
}

void writeField(char (&buffer)[256], const pfc::string8& value) noexcept {
    std::memset(buffer, 0, sizeof(buffer));
    const char* data = value.c_str();
    if (data) {
        const std::size_t length = std::strlen(data);
        std::memcpy(buffer, data,
            length < sizeof(buffer) - 1 ? length : sizeof(buffer) - 1);
    }
}

void publishMetadata(metadb_handle_ptr track) noexcept {
    if (!g_meta) return;
    pfc::string8 title, artist, album;
    try {
        if (!track.is_empty()) {
            track->format_title_legacy(nullptr, title, "[%title%]", nullptr);
            track->format_title_legacy(nullptr, artist, "[%artist%]", nullptr);
            track->format_title_legacy(nullptr, album, "[%album%]", nullptr);
        }
    } catch (...) {}
    InterlockedIncrement(&g_meta->sequence);
    MemoryBarrier();
    g_meta->generation = g_generation;
    writeField(g_meta->title, title);
    writeField(g_meta->artist, artist);
    writeField(g_meta->album, album);
    MemoryBarrier();
    InterlockedIncrement(&g_meta->sequence);
}


void publishFromFoobar() noexcept {
    if (g_shared == nullptr) return;
    LONG state = 0;
    std::int64_t position = 0;
    std::int64_t duration = -1;
    try {
        // Only called by the foobar2000 main-thread Win32 timer.
        auto p = playback_control::get();
        if (p->is_playing()) {
            state = p->is_paused() ? 2 : 1;
            const double sec = p->playback_get_position();
            const double length = p->playback_get_length();
            if (std::isfinite(sec) && sec >= 0)
                position = static_cast<std::int64_t>(std::llround(sec * 1000000.0));
            if (std::isfinite(length) && length > 0)
                duration = static_cast<std::int64_t>(std::llround(length * 1000000.0));
        }
    } catch (...) {
        state = 0;
    }

    // Single writer, lock-free consistent snapshot for readers.
    InterlockedIncrement(&g_shared->sequence); // odd
    MemoryBarrier();
    g_shared->playbackState = state;
    g_shared->positionUs = position;
    g_shared->durationUs = duration;
    g_shared->tickMs = GetTickCount64();
    g_shared->generation = g_generation;
    MemoryBarrier();
    InterlockedIncrement(&g_shared->sequence); // even
}

VOID CALLBACK timerProc(HWND, UINT, UINT_PTR id, DWORD) {
    if (id != g_timer) return;
    if (!g_publishing) tryStartPublisher();
    else publishFromFoobar();
}

class track_events final : public play_callback_impl_base {
public:
    track_events() : play_callback_impl_base(
        play_callback::flag_on_playback_new_track |
        play_callback::flag_on_playback_edited) {}
    void on_playback_new_track(metadb_handle_ptr track) override {
        ++g_generation;
        publishArtwork(album_art_data::ptr(), false); // do not reuse prior cover
        publishMetadata(track);
        g_embeddedCoverActive = publishEmbeddedFrontCover(track);
        publishFromFoobar();
    }
    void on_playback_edited(metadb_handle_ptr track) override {
        publishMetadata(track);
        // Artwork can change when the user updates this file's tags.
        g_embeddedCoverActive = publishEmbeddedFrontCover(track);
    }
};
track_events* g_trackEvents = nullptr;

// A still-running old v0.3 bridge does not use the new writer mutex,
// so also require its heartbeat to become stale before adopting a mapping.
// If Foobar restarts rapidly, its new bridge retries until the old writer is
// definitely gone rather than failing permanently on ERROR_ALREADY_EXISTS.
void tryStartPublisher() noexcept {
    if (g_publishing || !g_shared || !g_writerOwns) return;
    const auto now = GetTickCount64();
    if (g_mappingWasExisting &&
        g_shared->magic == kMagic && g_shared->version == kVersion) {
        const auto tick = g_shared->tickMs;
        if (tick > 0 && (tick > now || now - tick <= 2100)) return;
    }

    const std::uint64_t pastGeneration =
        g_shared->magic == kMagic && g_shared->version == kVersion
            ? g_shared->generation : 0;
    // Generation is now unique across Foobar process restarts, not merely
    // a per-process 1,2,3 counter. Existing v0.3 readers accept uint64.
    const std::uint64_t processEpoch =
        (static_cast<std::uint64_t>(now) << 16) ^
         static_cast<std::uint64_t>(GetCurrentProcessId());
    g_generation = pastGeneration + 1 > processEpoch
        ? pastGeneration + 1 : processEpoch;
    ZeroMemory(g_shared, sizeof(*g_shared));
    g_shared->magic = kMagic;
    g_shared->version = kVersion;
    g_shared->durationUs = -1;

        // New optional metadata side-channel; original timeline layout
        // remains byte-for-byte backward-compatible with bridge v0.1.
        g_metaMapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr,
            PAGE_READWRITE, 0, sizeof(MetadataSnapshot), kMetaName);
        if (g_metaMapping) {
            // A previous Foobar process may have exited while the viewer
            // still holds this named mapping. The writer has now acquired
            // exclusive ownership, so adopt and REINITIALIZE that mapping.
            g_meta = static_cast<MetadataSnapshot*>(MapViewOfFile(
                g_metaMapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(MetadataSnapshot)));
            if (g_meta) {
                ZeroMemory(g_meta, sizeof(*g_meta));
                g_meta->magic = 0x4D455441; // META
                g_meta->version = 1;
            }
            if (!g_meta) {
                CloseHandle(g_metaMapping);
                g_metaMapping = nullptr;
            }
        }
        g_artMapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr,
            PAGE_READWRITE, 0, sizeof(ArtworkSnapshot), kArtName);
        if (g_artMapping) {
            g_art = static_cast<ArtworkSnapshot*>(
                MapViewOfFile(g_artMapping, FILE_MAP_ALL_ACCESS,
                    0, 0, sizeof(ArtworkSnapshot)));
            if (g_art) {
                ZeroMemory(g_art, sizeof(ArtworkSnapshot));
                g_art->magic = 0x4152544D; // ARTM
                g_art->version = 1;
                publishArtwork(album_art_data::ptr(), false);
            }
            if (!g_art) {
                CloseHandle(g_artMapping);
                g_artMapping = nullptr;
            }
        }

        // Use Foobar2000's centralized now-playing album-art loader, which
        // obtains front cover art for this playback item (including embedded
        // pictures), rather than GSMTC's potentially stale thumbnail.
        try {
            if (g_art) {
                g_artNotify = now_playing_album_art_notify_manager::get()->add(
                    [](album_art_data::ptr artwork) {
                        // Do not overwrite a definite embedded cover with a
                        // different external/previous-cover result.
                        if (!g_embeddedCoverActive) publishArtwork(artwork);
                    });
            }
        } catch (...) {
            console::print("foo_melisma_bridge: native artwork notify unavailable");
        }

        g_trackEvents = new track_events();
        try {
            metadb_handle_ptr playing;
            if (playback_control::get()->get_now_playing(playing)) {
                ++g_generation;
                publishArtwork(album_art_data::ptr(), false);
                publishMetadata(playing);
                g_embeddedCoverActive = publishEmbeddedFrontCover(playing);
                if (g_artNotify && !g_embeddedCoverActive) {
                    try {
                        auto existing = now_playing_album_art_notify_manager::get()->current();
                        if (existing.is_valid()) publishArtwork(existing);
                    } catch (...) {}
                }
            }
        } catch (...) {}

    g_publishing = true;
    publishFromFoobar();
    console::print("foo_melisma_bridge: timeline, native tags, and front cover ready");
}

class bridge_lifecycle : public initquit {
public:
    void on_init() override {
        // Different Foobar processes must not both write this shared memory.
        // A legacy v0.3 writer has no mutex, so the heartbeat gate above
        // additionally protects against taking over its live mapping.
        g_writerLock = CreateMutexW(nullptr, FALSE, kWriterLockName);
        if (!g_writerLock) return;
        const DWORD lock = WaitForSingleObject(g_writerLock, 0);
        if (lock != WAIT_OBJECT_0 && lock != WAIT_ABANDONED) {
            CloseHandle(g_writerLock);
            g_writerLock = nullptr;
            console::print("foo_melisma_bridge: another Foobar bridge owns the writer lock");
            return;
        }
        g_writerOwns = true;

        g_mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr,
            PAGE_READWRITE, 0, sizeof(BridgeSnapshot), kMappingName);
        if (!g_mapping) {
            console::print("foo_melisma_bridge: could not create shared memory");
            return;
        }
        g_mappingWasExisting = GetLastError() == ERROR_ALREADY_EXISTS;
        g_shared = static_cast<BridgeSnapshot*>(
            MapViewOfFile(g_mapping, FILE_MAP_ALL_ACCESS, 0, 0,
                sizeof(BridgeSnapshot)));
        if (!g_shared) {
            console::print("foo_melisma_bridge: could not map shared memory");
            return;
        }

        // Retry acquisition after a previous writer exits. In particular,
        // never give up just because a Melisma viewer still has the old
        // memory-mapped file open after an immediate Foobar restart.
        tryStartPublisher();
        g_timer = SetTimer(nullptr, 0, 100, timerProc);
        if (!g_timer)
            console::print("foo_melisma_bridge: timer setup failed");
    }
    void on_quit() override {
        if (g_timer) { KillTimer(nullptr, g_timer); g_timer = 0; }
        delete g_trackEvents;
        g_trackEvents = nullptr;
        if (g_artNotify) {
            try { now_playing_album_art_notify_manager::get()->remove(g_artNotify); }
            catch (...) {}
            g_artNotify = nullptr;
        }
        if (g_art) { UnmapViewOfFile(g_art); g_art = nullptr; }
        if (g_artMapping) { CloseHandle(g_artMapping); g_artMapping = nullptr; }
        if (g_meta) { UnmapViewOfFile(g_meta); g_meta = nullptr; }
        if (g_metaMapping) { CloseHandle(g_metaMapping); g_metaMapping = nullptr; }
        if (g_shared) { UnmapViewOfFile(g_shared); g_shared = nullptr; }
        if (g_mapping) { CloseHandle(g_mapping); g_mapping = nullptr; }
        g_publishing = false;
        if (g_writerOwns && g_writerLock) ReleaseMutex(g_writerLock);
        if (g_writerLock) CloseHandle(g_writerLock);
        g_writerLock = nullptr;
        g_writerOwns = false;
    }
};
static initquit_factory_t<bridge_lifecycle> g_lifecycle;
} // namespace

DECLARE_COMPONENT_VERSION(
    "Melisma Foobar2000 Bridge",
    "0.4.0",
    "Read-only Foobar playback timing and metadata for Melisma Karaoke.\n"
    "Reports position, duration, tags, front cover, and track changes. "
    "Never modifies audio, playlists or playback.");
VALIDATE_COMPONENT_FILENAME("foo_melisma_bridge.dll");
