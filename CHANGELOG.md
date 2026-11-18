# Changelog

All notable changes to `com.ponyudev.sherpa-onnx` will be documented in this file.

## [0.3.0] - 2026-11-18

First release of the maintained fork ([Emanx140/Unity-Sherpa-ONNX](https://github.com/Emanx140/Unity-Sherpa-ONNX)), updated for sherpa-onnx 2.0.1.

### Changed

- Default sherpa-onnx version is **2.0.1** (was 1.13.0).
- `VadSegment.StartSample` is `long`, matching sherpa-onnx 2.0's 64-bit `SpeechSegment.Start`. A 32-bit index overflows after about 37 hours of audio at 16 kHz, which long-running VAD streams reach. `StartTime` is computed in double precision.
- iOS libraries are downloaded from this fork's releases (`sherpa-v<version>`), built by the new **iOS libraries** GitHub Actions workflow on macOS with the existing `Tools~/ios` scripts.
- UniTask dependency is 2.5.11: 2.5.10 fails to compile on Unity 6000.2+ (`TreeViewItem` is obsolete). Upstream issue #29.
- Package repository, documentation and changelog URLs point at the fork.
- Info-level log messages are written without stack traces; warnings and errors keep them.

### Added

- `SherpaNativeLibraries`: on Windows, loads the bundled `onnxruntime.dll` and `sherpa-onnx-c-api.dll` by full path before anything else, so Windows does not resolve the older Windows ML `onnxruntime.dll` from System32 and crash on the first model. Reports the reason through `Problem` / `ThrowIfUnavailable()` when another copy is already loaded or a file is missing.
- Kitten TTS profiles have a `kittenLexicon` field (added to sherpa-onnx's Kitten config in 2.0), filled automatically on import and shown in the profile editor.

### Fixed

- TAR extraction kept only the first 100 characters of long file names, so models such as `sherpa-onnx-streaming-zipformer-ar_en_id_ja_ru_th_vi_zh-2025-02-10` lost their file extensions and auto-configuration failed. GNU long-name (`L`) and PAX (`x`) entries are now applied, and the ustar name prefix is read only from POSIX archives. Upstream issue #30.
- Native crash (`SIGSEGV` in `SherpaOnnxOfflineTtsSampleRate`) when a TTS profile fails to load, for example while switching profiles on Android. The engine read the sample rate of an instance whose native handle was NULL; it now checks the handle first, as the ASR and VAD engines already do, and reports the failure. Upstream issue #27.

## [0.2.0] - 2026-05-10

Runtime API surface revamp, on-disk profile management overhaul, and a unified sample scene. Driven by the items Android testing surfaced during the cycle.

### Added

- **`ProfileReadyEvent` semantic init API** on every service (TTS / offline ASR / online ASR / VAD). `InitializeAsync(Action<ProfileReadyEvent>, ct)` reports Download / DownloadRetrying / Extract / Init / Ready / Failed phases, each with its own 0..100 percent. Unified-bar weighting documented in the runtime-usage guides.
- **`RemoteProfileFetcher`** — runtime download of `.zip` / `.tar.gz` / `.tar.bz2` model archives via `UnityWebRequest + DownloadHandlerFile` + SharpZipLib. Streams archives straight to disk (no managed `byte[]`), 3 retries with exponential backoff, marker keyed by SHA-1 of the URL.
- **Per-profile lazy extraction on Android** for `Local` / `Remote` / `LocalZip` sources — every source lands at the same `persistentDataPath/SherpaOnnx/<service>-models/<profileName>/`, and only the active profile's group is materialised on first `LoadProfile`.
- **`IModelDiskUsage`** on every service: `GetExtractedProfiles()`, `GetExtractedProfileSizeBytes(name)`, `TryDeleteExtractedProfile(name)`, `CleanupUnusedExtractedProfiles()`.
- **`keepOnlyActiveProfile`** runtime flag + Project Settings toggle. After every successful Initialize / SwitchProfile, removes every registered profile's extracted directory except the active one.
- **`buildOnlyActiveProfile`** Editor-build flag — strips non-active profile model files (and LocalZip archives) out of StreamingAssets before manifest generation, so the produced build ships only the active profile. Implies `keepOnlyActiveProfile` at runtime.
- **`SwitchProfileAsync(int|string, ct)`** on every service — runs the native engine ctor on the thread pool so the UI thread stays free during the multi-second sherpa-onnx ctor. Re-emits `ProfileReadyEvent` (Init / Ready / Failed) through the cached callback.
- **`IsProfileAvailable(string)`** on every service — picker UIs can hide profiles whose model files are not reachable on the current platform.
- **`MainThreadDispatcher`** (public, `Runtime/Common/Platform/`) — captures Unity's main-thread `SynchronizationContext` once at startup and exposes `Post(...)`. Any consumer wiring UI to `ProfileReadyEvent` from worker threads gets a thread-safe primitive without rolling its own capture.
- **Unified `Samples~/Demo/SherpaOnnxDemo.unity`** — single scene with a top-level menu, three per-module sub-menus, and a per-module profile-picker dropdown that filters through `IsProfileAvailable`. Replaces the three previous sample scenes.
- TTS / ASR / VAD runtime-usage guides updated with the new event API, disk-management semantics, and async switch / availability APIs.

### Changed

- Models from any source (`Local` / `Remote` / `LocalZip`) now share `persistentDataPath/SherpaOnnx/<service>-models/<profileName>/` on Android. Disk-usage APIs treat all three the same.
- `StreamingAssetsCopier` streams files via `DownloadHandlerFile` — no managed `byte[]` is materialised, so 100 MB+ models do not spike GC or OOM low-memory devices. Partial files are removed on cancel / HTTP error.
- Native engine load offloaded to the thread pool — UI no longer freezes at 99% on Android while loading large models.
- In Editor, any non-Local profile is treated as Local — `ProfileSourceResolver` short-circuits and the path resolvers route every source to StreamingAssets, so PlayMode does not re-download or re-unpack archives on every iteration.
- `BZip2InputStream` reads through a `BufferedStream(64 KB)` so SharpZipLib's small-chunk reads do not dominate wall-clock on Android external storage. tar.bz2 extraction emits compressed-bytes progress (throttled to ~4/s) plus per-32-MB info logs.
- `*InitProgressBus` (sample) `IsReady` / `IsFailed` flipped from sticky-after-first-event to current-phase — a recovered switch clears the previous Failed state.
- `RemoteProfileFetcher.StripSingleTopLevelFolder` mirrors `tar --strip-components=1` for archives wrapped under a single top-level directory.

### Fixed

- Android voice capture coexistence with TTS — `AudioSessionBridge` configures `AudioManager.MODE_IN_COMMUNICATION` + speakerphone on Android and switches `AVAudioSession` between `PlayAndRecord` / `Playback` on iOS.
- TTS on IL2CPP — falls back to callback-less `Generate(...)` because sherpa-onnx's chunk callback wraps user delegates as instance methods, which IL2CPP cannot marshal.
- Microphone resampling on Android devices that return native sample rates regardless of the requested 16 kHz; `Linear` and `Lowpass` algorithms selectable.
- `tar.bz2` / `tar.gz` / `zip` extractors rethrow exceptions instead of swallowing them — `RemoteProfileFetcher` no longer stamps a marker on an empty directory and the native engine no longer crashes on missing model files.
- Cross-thread bus events crashed UI Toolkit set-`value` calls. `MainThreadDispatcher` marshals the `Changed` event onto the main thread.
- `RemoteProfileFetcher` validates that the marker is paired with at least one non-marker file before reusing a cached extraction.
- Concurrent profile-switch / engine-use races (sherpa-onnx native ctor is not thread-safe). Each service gates every API entry point on a `volatile bool _isSwitching`; `TtsService` also rotates `_serviceCts` so in-flight `GenerateAsync` is cancelled before the new engine ctor starts.
- Sample sub-menu status label stayed frozen after `SwitchProfile` — services now cache the `InitializeAsync` callback and re-emit Ready / Failed from `SwitchProfile` / `SwitchProfileAsync`.

### Removed

- Three per-module sample scenes (`Samples~/{TTS,ASR,VAD}/`) — replaced by the unified `Samples~/Demo/SherpaOnnxDemo.unity`.
- `autoDeletePreviousProfile` setting — replaced by the more general `keepOnlyActiveProfile`.

### Breaking

- Service interfaces: `InitializeAsync(IProgress<float>)` → `InitializeAsync(Action<ProfileReadyEvent>)`. Mechanical migration: replace `new Progress<float>(p => …)` with `e => …` and switch on `e.Phase`.
- `*SettingsData.autoDeletePreviousProfile` → `keepOnlyActiveProfile`. Pure rename — `*Settings.asset` files lose the old value on first re-serialise; re-tick the box if the flag was set.
- `ITtsService` / `IAsrService` / `IOnlineAsrService` / `IVadService` gain three new members: `SwitchProfileAsync(int, ct)`, `SwitchProfileAsync(string, ct)`, `IsProfileAvailable(string)`. External implementers must add them.
- Sample directory: `Samples~/{TTS,ASR,VAD}/` removed; `Samples~/Demo/` is the only sample. Update any scripts that hard-coded the old paths.

## [0.1.0] - 2026-02-21

First public release with TTS, ASR, and VAD support.

### Added

#### Text-to-Speech (TTS)
- 6 model architectures: Vits (Piper), Matcha, Kokoro, Kitten, ZipVoice, Pocket (voice cloning)
- Editor UI for model import with auto-detection and auto-configuration
- Int8 quantization toggle
- Flexible deployment: Local (StreamingAssets), Remote (runtime download), LocalZip
- Matcha vocoder selector with independent download
- Cache pooling for audio buffers, AudioClips, and AudioSources

#### Speech Recognition (ASR)
- 15 offline architectures: Zipformer, Paraformer, Whisper, SenseVoice, Moonshine, and more
- 5 online (streaming) architectures with real-time microphone capture
- Partial and final result callbacks
- Engine pool for concurrent offline recognizer instances
- Endpoint detection with configurable silence rules

#### Voice Activity Detection (VAD)
- 2 model architectures: Silero VAD, TEN-VAD
- Configurable parameters: threshold, min silence/speech duration, window size
- VAD + ASR pipeline for segment-based recognition

#### Library Installer
- One-click native library install for Windows, macOS, Linux, Android, and iOS
- Update All to re-install every platform at once when changing version
- Desktop libraries from NuGet, mobile from sherpa-onnx GitHub releases
- Patched iOS managed DLL with `DllImport("__Internal")` from this repo's releases

#### Model Importer
- One-click import from URL (tar.gz, tar.bz2, zip)
- Auto-detection of model type and architecture
- Auto-configuration of model paths and profile settings

#### Platform Fixes
- Android: native `AudioRecord` fallback when Unity Microphone returns silence
- Android: StreamingAssets extraction to `persistentDataPath` with version tracking
- Android: locale guard (`LC_NUMERIC = "C"`) to prevent float parsing crashes
- iOS: xcframework architecture filtering (device vs simulator)
- Unity: silent AudioSource workaround to force microphone recording
- Unity: `Microphone.GetPosition()` polling with configurable timeout
- All: built-in sample rate resampler (any rate → 16 kHz)
- Android/iOS: async microphone permission request via UniTask

## [0.0.1] - Initial

- Initial package skeleton.
