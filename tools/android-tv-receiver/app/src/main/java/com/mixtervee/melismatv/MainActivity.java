package com.mixtervee.melismatv;

import android.app.Activity;
import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioTrack;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Build;
import android.os.Bundle;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.content.Context;
import android.net.wifi.WifiManager;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.NetworkInterface;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.SocketException;
import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.util.Collections;
import java.util.Enumeration;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Full-screen, browser-free karaoke receiver for Shield, Onn and Google TV.
 * It consumes JPEG frames and optional paired PCM playback, requests no microphone,
 * and doesn't expose a WebView or arbitrary URLs.
 */
public final class MainActivity extends Activity {
    private static final int STREAM_PORT = 49831;
    private static final int AUDIO_PORT = 49832;
    private static final int DISCOVERY_PORT = 47916;
    private static final int MAX_JPEG_BYTES = 2 * 1024 * 1024;
    private static final String PROBE = "MELISMA_DISCOVER_V1";
    private static final String REPLY = "MELISMA_TV_V1|";

    private ImageView imageView;
    private TextView statusText;
    private LinearLayout instructionPanel;
    private ExecutorService workers;
    private ServerSocket tcpServer;
    private DatagramSocket udpServer;
    private volatile Socket currentClient;
    private volatile Socket pairedClient;
    private volatile ServerSocket audioServer;
    private volatile Socket audioClient;
    private volatile AudioTrack audioTrack;
    private WifiManager.MulticastLock multicastLock;
    private volatile boolean active;
    private Bitmap previousBitmap;
    private String code;
    private boolean showInstructions = true;

    @Override
    public void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON
                | WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().getDecorView().setSystemUiVisibility(
            View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
            | View.SYSTEM_UI_FLAG_FULLSCREEN
            | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
            | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
            | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
            | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN);

        code = getPreferences(MODE_PRIVATE).getString("pairCode", null);
        if (code == null || !code.matches("[0-9]{6}")) {
            code = String.format(java.util.Locale.US, "%06d",
                100000 + new SecureRandom().nextInt(900000));
            getPreferences(MODE_PRIVATE).edit().putString("pairCode", code).apply();
        }

        FrameLayout root = new FrameLayout(this);
        root.setBackgroundColor(Color.BLACK);
        imageView = new ImageView(this);
        imageView.setScaleType(ImageView.ScaleType.FIT_CENTER);
        imageView.setBackgroundColor(Color.BLACK);
        root.addView(imageView, new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT,
            FrameLayout.LayoutParams.MATCH_PARENT));

        instructionPanel = new LinearLayout(this);
        instructionPanel.setOrientation(LinearLayout.VERTICAL);
        instructionPanel.setGravity(Gravity.CENTER);
        instructionPanel.setPadding(dp(26), dp(26), dp(26), dp(26));
        instructionPanel.setBackgroundColor(Color.rgb(14, 22, 37));

        TextView heading = new TextView(this);
        heading.setText("Melisma Receiver  •  Karaoke TV");
        heading.setTextColor(Color.rgb(135, 240, 226));
        heading.setTextSize(30f);
        heading.setTypeface(null, Typeface.BOLD);
        heading.setGravity(Gravity.CENTER);
        instructionPanel.addView(heading);

        statusText = new TextView(this);
        statusText.setTextSize(21f);
        statusText.setTextColor(Color.WHITE);
        statusText.setGravity(Gravity.CENTER);
        statusText.setPadding(0, dp(20), 0, 0);
        instructionPanel.addView(statusText);

        TextView help = new TextView(this);
        help.setText("\nOn your Windows PC:\n" +
            "Melisma → TV / Cast → Connect directly to Android TV\n\n" +
            "No Chrome or TV browser needed. Lyrics-only or optional audio cast.\n" +
            "Press OK on the TV remote to show/hide this information.");
        help.setTextSize(16f);
        help.setTextColor(Color.rgb(194, 211, 227));
        help.setGravity(Gravity.CENTER);
        instructionPanel.addView(help);

        FrameLayout.LayoutParams panel = new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT,
            FrameLayout.LayoutParams.MATCH_PARENT);
        root.addView(instructionPanel, panel);
        root.setFocusableInTouchMode(true);
        root.requestFocus();
        setContentView(root);
        setStatus("Starting receiver…");
    }

    @Override
    protected void onStart() {
        super.onStart();
        active = true;
        workers = Executors.newFixedThreadPool(3);
        try {
            WifiManager wifi = (WifiManager)getApplicationContext()
                .getSystemService(Context.WIFI_SERVICE);
            if (wifi != null) {
                multicastLock = wifi.createMulticastLock("melisma-tv-discovery");
                multicastLock.setReferenceCounted(false);
                multicastLock.acquire();
            }
        } catch (RuntimeException ignored) { /* Ethernet may not have Wi-Fi. */ }
        workers.execute(this::acceptTcp);
        workers.execute(this::respondToDiscovery);
        workers.execute(this::acceptAudio);
    }

    @Override
    protected void onStop() {
        active = false;
        closeNetwork();
        if (workers != null) {
            workers.shutdownNow();
            workers = null;
        }
        if (multicastLock != null) {
            try { if (multicastLock.isHeld()) multicastLock.release(); }
            catch (RuntimeException ignored) {}
            multicastLock = null;
        }
        super.onStop();
    }

    @Override
    protected void onDestroy() {
        if (previousBitmap != null) {
            previousBitmap.recycle();
            previousBitmap = null;
        }
        super.onDestroy();
    }

    private void closeNetwork() {
        pairedClient = null;
        closeAudioClient();
        try { if (audioServer != null) audioServer.close(); } catch (IOException ignored) {}
        try { if (currentClient != null) currentClient.close(); }
        catch (IOException ignored) {}
        try { if (tcpServer != null) tcpServer.close(); }
        catch (IOException ignored) {}
        if (udpServer != null) udpServer.close();
        currentClient = null;
        tcpServer = null;
        udpServer = null;
    }

    private void acceptTcp() {
        try (ServerSocket server = new ServerSocket(STREAM_PORT)) {
            server.setReuseAddress(true);
            tcpServer = server;
            setStatus("Ready  •  TV: " + Build.MODEL + "\n" +
                "IP address: " + findIpv4() + "\n" +
                "Pairing code: " + code);
            while (active) {
                try {
                    Socket client = server.accept();
                    currentClient = client;
                    receiveFrames(client);
                } catch (IOException e) {
                    if (active)
                        setStatus("Connection ended  •  Waiting for Melisma Windows\n" +
                            "IP: " + findIpv4() + "   Code: " + code);
                } finally {
                    if (currentClient != null) {
                        try { currentClient.close(); } catch (IOException ignored) {}
                        currentClient = null;
                    }
                }
            }
        } catch (IOException e) {
            if (active) setStatus("Cannot start receiver: " + e.getMessage() +
                "\nTry closing and reopening Melisma Receiver.");
        }
    }

    private void receiveFrames(Socket client) throws IOException {
        client.setTcpNoDelay(true);
        client.setSoTimeout(12000);
        try (DataInputStream input = new DataInputStream(client.getInputStream());
             DataOutputStream output = new DataOutputStream(client.getOutputStream())) {
            byte[] handshake = new byte[14]; // "MELISMA1" + 6-digit code
            input.readFully(handshake);
            String hello = new String(handshake, StandardCharsets.US_ASCII);
            boolean paired = hello.equals("MELISMA1" + code);
            output.writeByte(paired ? 1 : 0);
            output.flush();
            if (!paired) {
                setStatus("Incorrect pairing code\nIP: " + findIpv4() +
                    "   Code: " + code);
                return;
            }
            pairedClient = client;
            setStatus("Connected to Melisma Windows");
            while (active && !client.isClosed()) {
                int size = input.readInt(); // 4-byte network-order length
                if (size < 8 || size > MAX_JPEG_BYTES)
                    throw new IOException("Invalid image frame size");
                byte[] data = new byte[size];
                input.readFully(data);
                if ((data[0] & 255) != 0xff || (data[1] & 255) != 0xd8)
                    throw new IOException("Invalid JPEG frame");
                Bitmap bitmap = BitmapFactory.decodeByteArray(data, 0, data.length);
                if (bitmap != null) {
                    runOnUiThread(() -> {
                        if (!active) {
                            bitmap.recycle();
                            return;
                        }
                        Bitmap old = previousBitmap;
                        imageView.setImageBitmap(bitmap);
                        previousBitmap = bitmap;
                        if (old != null && old != bitmap) old.recycle();
                        instructionPanel.setVisibility(View.GONE);
                        showInstructions = false;
                    });
                }
            }
        } finally {
            if (pairedClient == client) {
                pairedClient = null;
                closeAudioClient();
            }
            if (active) {
                setStatus("Disconnected  •  Ready for Melisma Windows\n" +
                    "IP: " + findIpv4() + "   Code: " + code);
                runOnUiThread(() -> {
                    imageView.setImageDrawable(null);
                    instructionPanel.setVisibility(View.VISIBLE);
                    showInstructions = true;
                });
            }
        }
    }

    private void closeAudioClient() {
        Socket s = audioClient;
        if (s != null) try { s.close(); } catch (IOException ignored) {}
        // Unblock a blocking AudioTrack.write on receiver shutdown.
        AudioTrack t = audioTrack;
        if (t != null) try { t.pause(); t.flush(); } catch (RuntimeException ignored) {}
    }

    private void acceptAudio() {
        try (ServerSocket server = new ServerSocket(AUDIO_PORT)) {
            audioServer = server;
            while (active) {
                try (Socket client = server.accept()) {
                    audioClient = client;
                    receiveAudio(client);
                } catch (IOException | RuntimeException e) {
                    // Audio failure must not stop the existing lyric stream.
                    android.util.Log.w("MelismaAudio", "Audio ended", e);
                } finally {
                    audioClient = null;
                }
            }
        } catch (IOException e) {
            if (active) android.util.Log.w("MelismaAudio", "Audio port unavailable", e);
        } finally { audioServer = null; }
    }

    private void receiveAudio(Socket client) throws IOException {
        client.setTcpNoDelay(true);
        client.setSoTimeout(5000);
        AudioTrack track = null;
        try (DataInputStream input = new DataInputStream(client.getInputStream());
             DataOutputStream output = new DataOutputStream(client.getOutputStream())) {
            byte[] hello = new byte[14];
            input.readFully(hello);
            int rate = input.readInt();
            int channels = input.readInt();
            Socket peer = pairedClient;
            if (!active || peer == null || peer.isClosed() ||
                !peer.getInetAddress().equals(client.getInetAddress()) ||
                !new String(hello, StandardCharsets.US_ASCII).equals("MELAUDIO" + code) ||
                rate < 8000 || rate > 96000 || (channels != 1 && channels != 2)) {
                output.writeByte(0); output.flush(); return;
            }
            int mask = channels == 1 ? AudioFormat.CHANNEL_OUT_MONO : AudioFormat.CHANNEL_OUT_STEREO;
            int min = AudioTrack.getMinBufferSize(rate, mask, AudioFormat.ENCODING_PCM_16BIT);
            if (min <= 0) { output.writeByte(0); output.flush(); return; }
            int buffer = Math.max(min, rate * channels * 2 / 10); // target ~100 ms
            track = new AudioTrack.Builder()
                .setAudioAttributes(new AudioAttributes.Builder()
                    .setUsage(AudioAttributes.USAGE_MEDIA)
                    .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
                .setAudioFormat(new AudioFormat.Builder()
                    .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
                    .setSampleRate(rate).setChannelMask(mask).build())
                .setBufferSizeInBytes(buffer)
                .setTransferMode(AudioTrack.MODE_STREAM)
                .build();
            if (track.getState() != AudioTrack.STATE_INITIALIZED ||
                !active || pairedClient != peer || client.isClosed()) {
                output.writeByte(0); output.flush(); return;
            }
            audioTrack = track;
            track.play();
            output.writeByte(1); output.flush();
            // WASAPI may deliver nothing during silence. Keep the audio socket
            // open until the paired lyric connection ends or the sender closes.
            client.setSoTimeout(0);
            while (active && pairedClient == peer && !client.isClosed()) {
                int count = input.readInt();
                if (count <= 0 || count > 262144 || count % (channels * 2) != 0)
                    throw new IOException("Invalid PCM packet length");
                byte[] data = new byte[count];
                input.readFully(data);
                int offset = 0;
                while (offset < count && active && pairedClient == peer && !client.isClosed()) {
                    int written = track.write(data, offset, count - offset, AudioTrack.WRITE_BLOCKING);
                    if (written <= 0) throw new IOException("TV audio write failed: " + written);
                    offset += written;
                }
            }
        } finally {
            if (audioTrack == track) audioTrack = null;
            if (track != null) {
                try { track.pause(); track.flush(); } catch (RuntimeException ignored) {}
                track.release();
            }
        }
    }

    private void respondToDiscovery() {
        try (DatagramSocket socket = new DatagramSocket(DISCOVERY_PORT)) {
            udpServer = socket;
            socket.setBroadcast(true);
            socket.setSoTimeout(1500);
            byte[] buffer = new byte[128];
            while (active) {
                try {
                    DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                    socket.receive(packet);
                    String request = new String(packet.getData(), packet.getOffset(),
                        packet.getLength(), StandardCharsets.US_ASCII);
                    if (!PROBE.equals(request)) continue;
                    String model = Build.MODEL.replace('|', ' ').replace('\n', ' ');
                    String reply = REPLY + model + "|" + STREAM_PORT;
                    byte[] data = reply.getBytes(StandardCharsets.UTF_8);
                    socket.send(new DatagramPacket(data, data.length,
                        packet.getAddress(), packet.getPort()));
                } catch (java.net.SocketTimeoutException ignored) {
                    // Poll every 1.5s so the receiver exits cleanly on Stop.
                } catch (IOException e) {
                    if (active) setStatus("Discovery unavailable: use TV IP " +
                        findIpv4() + "\nCode: " + code);
                }
            }
        } catch (SocketException e) {
            if (active) setStatus("TV discovery unavailable. Use IP: " +
                findIpv4() + "\nPairing code: " + code);
        }
    }

    private String findIpv4() {
        try {
            Enumeration<NetworkInterface> interfaces = NetworkInterface.getNetworkInterfaces();
            for (NetworkInterface nic : Collections.list(interfaces)) {
                if (!nic.isUp() || nic.isLoopback()) continue;
                for (InetAddress address : Collections.list(nic.getInetAddresses())) {
                    if (address instanceof java.net.Inet4Address &&
                        !address.isLoopbackAddress())
                        return address.getHostAddress();
                }
            }
        } catch (SocketException ignored) {}
        return "unavailable";
    }

    private void setStatus(String text) {
        runOnUiThread(() -> {
            if (statusText != null && active)
                statusText.setText(text);
        });
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_DPAD_CENTER ||
            keyCode == KeyEvent.KEYCODE_ENTER) {
            showInstructions = !showInstructions;
            instructionPanel.setVisibility(showInstructions ? View.VISIBLE : View.GONE);
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
