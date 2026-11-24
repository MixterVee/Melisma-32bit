// Melisma Foobar2000 Bridge v0.1
// Read-only playback timeline for local Windows karaoke. No audio modifications,
// playback commands, network listeners, filesystem or registry access.
// Foobar2000 SDK playback_control requires the main thread. The Win32 timer
// runs there and publishes a small versioned shared-memory snapshot.
#include <foobar2000/SDK/foobar2000.h>
#include <windows.h>
#include <cstdint>
#include <cmath>
#include <cstddef>

namespace {
constexpr wchar_t kMappingName[] = L"Local\\MelismaFoobarBridge_v1";
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

HANDLE g_mapping = nullptr;
BridgeSnapshot* g_shared = nullptr;
UINT_PTR g_timer = 0;
std::uint64_t g_generation = 0;

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
    publishFromFoobar();
}

class track_events final : public play_callback_impl_base {
public:
    track_events() : play_callback_impl_base(play_callback::flag_on_playback_new_track) {}
    void on_playback_new_track(metadb_handle_ptr) override {
        ++g_generation;
        publishFromFoobar();
    }
};
track_events* g_trackEvents = nullptr;

class bridge_lifecycle final : public initquit {
public:
    void on_init() override {
        g_mapping = CreateFileMappingW(
            INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
            0, sizeof(BridgeSnapshot), kMappingName);
        if (g_mapping == nullptr) {
            console::print("foo_melisma_bridge: could not create shared memory");
            return;
        }
        g_shared = static_cast<BridgeSnapshot*>(
            MapViewOfFile(g_mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(BridgeSnapshot)));
        if (g_shared == nullptr) {
            CloseHandle(g_mapping);
            g_mapping = nullptr;
            console::print("foo_melisma_bridge: could not map shared memory");
            return;
        }
        // The name is confined to the interactive Windows user session.
        // Do not overwrite a competing Foobar instance's live bridge mapping.
        if (GetLastError() == ERROR_ALREADY_EXISTS) {
            UnmapViewOfFile(g_shared);
            g_shared = nullptr;
            CloseHandle(g_mapping);
            g_mapping = nullptr;
            console::print("foo_melisma_bridge: mapping already exists");
            return;
        }
        ZeroMemory(g_shared, sizeof(*g_shared));
        g_shared->magic = kMagic;
        g_shared->version = kVersion;
        g_shared->durationUs = -1;
        g_trackEvents = new track_events();
        // SetTimer(NULL) is delivered on THIS thread's message queue, the
        // foobar2000 main thread. Playback SDK is never called from a worker.
        g_timer = SetTimer(nullptr, 0, 100, timerProc);
        if (g_timer == 0) console::print("foo_melisma_bridge: timer setup failed");
        publishFromFoobar();
        console::print("foo_melisma_bridge: read-only timeline bridge ready");
    }
    void on_quit() override {
        if (g_timer) { KillTimer(nullptr, g_timer); g_timer = 0; }
        delete g_trackEvents;
        g_trackEvents = nullptr;
        if (g_shared) { UnmapViewOfFile(g_shared); g_shared = nullptr; }
        if (g_mapping) { CloseHandle(g_mapping); g_mapping = nullptr; }
    }
};
static initquit_factory_t<bridge_lifecycle> g_lifecycle;
} // namespace

DECLARE_COMPONENT_VERSION(
    "Melisma Foobar2000 Bridge",
    "0.1.0",
    "Read-only, local shared-memory playback timing for Melisma Karaoke.\n"
    "Reports elapsed position, duration, pause and track changes. "
    "Never modifies audio, playlists or playback.");
VALIDATE_COMPONENT_FILENAME("foo_melisma_bridge.dll");
