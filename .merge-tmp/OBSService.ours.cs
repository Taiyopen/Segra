using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ObsKit.NET;
using ObsKit.NET.Encoders;
using ObsKit.NET.Native.Types;
using ObsKit.NET.Outputs;
using ObsKit.NET.Scenes;
using ObsKit.NET.Signals;
using ObsKit.NET.Sources;
using Segra.Backend.Core.Models;
using Segra.Backend.Services;
using Segra.Backend.Shared;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using static Segra.Backend.Shared.GeneralUtils;
using static Segra.Backend.App.MessageService;
using System.Net.Http.Json;
using Segra.Backend.Media;
using Segra.Backend.App;
using Segra.Backend.Windows.Display;
using Segra.Backend.Games;
using Segra.Backend.Games.VrChat;
using Segra.Backend.Windows.Input;
using Segra.Backend.Windows.Storage;
using System.Threading.Channels;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Segra.Backend.Recorder
{
    public static partial class OBSService
    {
        // Constants
        private const uint OBS_SOURCE_FLAG_FORCE_MONO = 1u << 1; // from obs.h

        // Executables that OBS internally blacklists from game capture (cannot be hooked)
        // https://github.com/obsproject/obs-studio/blob/e448c0a963eda45f48515b2cb9a631daced9d503/plugins/win-capture/game-capture.c#L956
        private static readonly string[] ObsInternalBlacklist =
        [
            "explorer.exe",
            "steam.exe",
            "battle.net.exe",
            "galaxyclient.exe",
            "skype.exe",
            "uplay.exe",
            "origin.exe",
            "devenv.exe",
            "taskmgr.exe",
            "chrome.exe",
            "discord.exe",
            "firefox.exe",
            "systemsettings.exe",
            "applicationframehost.exe",
            "cmd.exe",
            "shellexperiencehost.exe",
            "winstore.app.exe",
            "searchui.exe",
            "lockapp.exe",
            "windowsinternal.composableshell.experiences.textinput.inputapp.exe"
        ];

        // Regex patterns for buffer parsing
        [GeneratedRegex(@"BufferDesc\.Width:\s*(\d+)")]
        private static partial Regex BufferDescWidthRegex();

        [GeneratedRegex(@"BufferDesc\.Height:\s*(\d+)")]
        private static partial Regex BufferDescHeightRegex();

        // Public properties
        public static bool IsInitialized { get; private set; }
        public static GpuVendor DetectedGpuVendor { get; private set; } = DetectGpuVendor();
        public static uint? CapturedWindowWidth { get; private set; } = null;
        public static uint? CapturedWindowHeight { get; private set; } = null;
        private static int _lastGameCaptureLogSlot = -1;
        public static string? InstalledOBSVersion { get; private set; } = null;

        // OBS context
        private static ObsContext? _obsContext;

        /// <summary>Holds scene, sources, encoders and outputs for one recording slot.</summary>
        private sealed class SessionPipeline
        {
            public Scene? MainScene;
            public SceneItem? GameCaptureItem;
            public SceneItem? DisplayItem;
            public RecordingOutput? SessionOutput;
            public ReplayBuffer? BufferOutput;
            public SignalConnection? ReplaySavedConnection;
            public MonitorCapture? DisplaySource;
            public readonly List<AudioInputCapture> MicSources = new();
            public readonly List<AudioOutputCapture> DesktopSources = new();
            public Source? DiscordAudioSource;
            public VideoEncoder? VideoEncoder;
            public readonly List<AudioEncoder> AudioEncoders = new();
            public string? HookedExecutableFileName;
            public System.Threading.Timer? GameCaptureHookTimeoutTimer;
            public GameCapture? GameCapture;
            public Action<GameCapture>? HookedSubscription;
            public Action<GameCapture>? UnhookedSubscription;
            /// <summary>Secondary canvas for slot 1+; slot 0 uses the main canvas.</summary>
            public Canvas? RecordingCanvas;
        }

        private const int MaxSessionSlots = RecordingSlots.Max;
        private static readonly uint?[] _capturedWindowWidthBySlot = new uint?[MaxSessionSlots];
        private static readonly uint?[] _capturedWindowHeightBySlot = new uint?[MaxSessionSlots];
        private static readonly SessionPipeline[] _pipelines = [new SessionPipeline(), new SessionPipeline()];
        private static SessionPipeline Pipeline(int slot) => _pipelines[slot];

        /// <summary>OBS source/encoder names must be globally unique across dual slots.</summary>
        private static string ObsName(int slot, string name) => $"s{slot}_{name}";

        private static bool IsAnotherSlotRecording(int slot)
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (i == slot) continue;
                if (AppState.Instance.GetRecording(i) != null)
                    return true;
                if (_pipelines[i].SessionOutput != null)
                    return true;
                if (_pipelines[i].BufferOutput != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Mic/desktop WASAPI and Discord captures must exist only once across dual slots;
        /// duplicating the same device feeds the global OBS mix twice.
        /// </summary>
        private static readonly object _sharedAudioLock = new();

        private static bool PipelineOwnsSharedAudio(SessionPipeline pl) =>
            pl.MicSources.Count > 0 || pl.DesktopSources.Count > 0 || pl.DiscordAudioSource != null;

        private static SessionPipeline? FindSharedAudioOwner(int excludeSlot = -1)
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (i == excludeSlot) continue;
                if (PipelineOwnsSharedAudio(_pipelines[i]))
                    return _pipelines[i];
            }
            return null;
        }

        private static int FindOtherActiveSlot(int slot)
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (i == slot) continue;
                if (AppState.Instance.GetRecording(i) != null
                    || _pipelines[i].SessionOutput != null
                    || _pipelines[i].BufferOutput != null
                    || _pipelines[i].MainScene != null)
                    return i;
            }
            return -1;
        }

        private static IEnumerable<AudioOutputCapture> EnumerateDesktopSources()
        {
            foreach (var p in _pipelines)
            {
                foreach (var source in p.DesktopSources)
                    yield return source;
            }
        }

        private static Source? FindDiscordAudioSource()
        {
            foreach (var p in _pipelines)
            {
                if (p.DiscordAudioSource != null)
                    return p.DiscordAudioSource;
            }
            return null;
        }

        /// <summary>
        /// Attach an existing shared audio source to another slot's scene so it stays active
        /// on that canvas without opening a second WASAPI/Discord capture.
        /// </summary>
        private static void AttachSharedAudioSourceToScene(Scene scene, Source source, string label)
        {
            try
            {
                scene.AddSource(source);
                Log.Information("Attached shared {Label} audio source to secondary slot scene", label);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to attach shared {Label} audio source to scene: {Message}", label, ex.Message);
            }
        }

        private static void TransferSharedAudioOwnership(SessionPipeline from, SessionPipeline to)
        {
            if (!PipelineOwnsSharedAudio(from))
                return;

            to.MicSources.AddRange(from.MicSources);
            from.MicSources.Clear();

            to.DesktopSources.AddRange(from.DesktopSources);
            from.DesktopSources.Clear();

            if (from.DiscordAudioSource != null)
            {
                to.DiscordAudioSource = from.DiscordAudioSource;
                from.DiscordAudioSource = null;
            }

            Log.Information("Transferred shared WASAPI/Discord audio ownership to remaining recording slot");
        }

        private static void DisposeOwnedSharedAudioSources(SessionPipeline pl)
        {
            foreach (var micSource in pl.MicSources)
            {
                try
                {
                    micSource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose mic source: {ex.Message}");
                }
            }
            pl.MicSources.Clear();

            foreach (var desktopSource in pl.DesktopSources)
            {
                try
                {
                    desktopSource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose desktop source: {ex.Message}");
                }
            }
            pl.DesktopSources.Clear();

            if (pl.DiscordAudioSource != null)
            {
                try
                {
                    pl.DiscordAudioSource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose Discord audio source: {ex.Message}");
                }
                pl.DiscordAudioSource = null;
            }
        }

        public static GameCapture? GetGameCaptureSource(int slot) => Pipeline(slot).GameCapture;

        public static bool IsSlotCaptureHooked(int slot) => Pipeline(slot).GameCapture?.IsHooked ?? false;

        public static string? GetTrackedExecutableFileName(int slot) =>
            Pipeline(slot).HookedExecutableFileName
            ?? AppState.Instance.GetRecording(slot)?.FileName;

        private static int? TryResolveHookedProcessId(string hookedExecutable, int slot)
        {
            string hookedFileName = Path.GetFileName(hookedExecutable);
            if (string.IsNullOrEmpty(hookedFileName))
                return null;

            var otherPids = new HashSet<int>();
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (i == slot) continue;
                if (AppState.Instance.GetRecording(i)?.Pid is int otherPid)
                    otherPids.Add(otherPid);
            }

            int? bestPid = null;
            long bestMemory = -1;

            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(hookedFileName)))
            {
                try
                {
                    if (process.HasExited || otherPids.Contains(process.Id))
                        continue;

                    string? path = null;
                    try
                    {
                        path = process.MainModule?.FileName;
                    }
                    catch
                    {
                        // Access denied for some processes
                    }

                    if (path != null
                        && !path.EndsWith(hookedFileName, StringComparison.OrdinalIgnoreCase)
                        && !hookedFileName.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
                        continue;

                    long mem = process.WorkingSet64;
                    if (mem > bestMemory)
                    {
                        bestMemory = mem;
                        bestPid = process.Id;
                    }
                }
                catch
                {
                    // Ignore processes we cannot inspect
                }
                finally
                {
                    process.Dispose();
                }
            }

            return bestPid;
        }

        /// <summary>First hooked game capture (slot 0, then slot 1), otherwise first available.</summary>
        public static GameCapture? GameCaptureSource =>
            GetGameCaptureSource(0) is { IsHooked: true } gc0 ? gc0
            : GetGameCaptureSource(1) is { IsHooked: true } gc1 ? gc1
            : GetGameCaptureSource(0) ?? GetGameCaptureSource(1);

        /// <summary>Used by OBS log processing to avoid false-positive stop when capture blips.</summary>
        private static readonly bool[] _isStillHookedAfterUnhookBySlot = new bool[MaxSessionSlots];

        // Recording/output state
        private static readonly bool[] _isStoppingSlot = new bool[MaxSessionSlots];
        private static readonly SemaphoreSlim[] _slotLifecycleLocks =
            Enumerable.Range(0, MaxSessionSlots).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
        private static uint _currentBaseWidth;
        private static uint _currentBaseHeight;

        private static (uint width, uint height) EnsureEvenDimensions(uint width, uint height)
        {
            if (width % 2 != 0)
                width--;
            if (height % 2 != 0)
                height--;
            return (Math.Max(2, width), Math.Max(2, height));
        }

        public static bool TryGetCapturedWindowDimensions(int slot, out uint width, out uint height)
        {
            if (slot >= 0
                && slot < MaxSessionSlots
                && _capturedWindowWidthBySlot[slot].HasValue
                && _capturedWindowHeightBySlot[slot].HasValue)
            {
                width = _capturedWindowWidthBySlot[slot]!.Value;
                height = _capturedWindowHeightBySlot[slot]!.Value;
                return true;
            }

            width = 0;
            height = 0;
            return false;
        }

        private static void ClearCapturedWindowDimensions(int slot)
        {
            if (slot < 0 || slot >= MaxSessionSlots)
                return;

            _capturedWindowWidthBySlot[slot] = null;
            _capturedWindowHeightBySlot[slot] = null;

            if (slot == 0)
            {
                CapturedWindowWidth = null;
                CapturedWindowHeight = null;
            }
        }

        private static void ClearAllCapturedWindowDimensions()
        {
            for (int i = 0; i < MaxSessionSlots; i++)
                ClearCapturedWindowDimensions(i);
            _lastGameCaptureLogSlot = -1;
        }

        // Replay buffer save callbacks (one per slot)
        private static readonly bool[] _replaySavedBySlot = new bool[MaxSessionSlots];

        /// <summary>
        /// Parses settings recording audio bitrate (e.g. "128k") to kbps for AAC encoders.
        /// </summary>
        private static int GetRecordingAudioBitrateKbps()
        {
            var raw = Settings.Instance.RecordingAudioBitrate?.Trim();
            if (string.IsNullOrEmpty(raw))
                return 128;
            if (raw.EndsWith("k", StringComparison.OrdinalIgnoreCase))
                raw = raw[..^1];
            if (int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var kbps))
                return Math.Clamp(kbps, 96, 320);
            return 128;
        }

        private static uint SanitizeAudioTrackMask(uint mask) => mask & 0x3Fu;

        private static void ApplyAudioTrackMask(Source source, uint mask, string label)
        {
            try
            {
                source.AudioMixers = mask;
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to set mixers for {label}: {ex.Message}");
            }
        }

        private static void ApplyAudioTrackMask(IReadOnlyList<Source> sources, uint mask, string labelPrefix)
        {
            for (int i = 0; i < sources.Count; i++)
                ApplyAudioTrackMask(sources[i], mask, $"{labelPrefix} {i + 1}");
        }

        /// <summary>
        /// Gets whether the game capture is currently hooked.
        /// Uses the built-in IsHooked property from OBSKit.NET.
        /// </summary>
        private static bool IsGameCaptureHooked => GameCaptureSource?.IsHooked ?? false;

        // Threading primitives
        private static readonly SemaphoreSlim _stopRecordingSemaphore = new SemaphoreSlim(1, 1);
        /// <summary>Serializes replay buffer save, manual F10 save, and VRChat VVMW tail clips.</summary>
        private static readonly SemaphoreSlim ReplayBufferSaveLock = new(1, 1);

        // Log processing queue - prevents OBS thread from blocking on log operations
        private static readonly Channel<(int level, string message)> _logChannel =
            Channel.CreateUnbounded<(int, string)>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        private static DateTime _lastSuppressedAudioTsLogAtUtc = DateTime.MinValue;
        private static int _suppressedAudioTsLogCount = 0;

        public static async Task<bool> SaveReplayBuffer(int slot = 0)
        {
            var pl = Pipeline(slot);
            if (pl.BufferOutput == null || !pl.BufferOutput.IsActive)
            {
                Log.Warning("Cannot save replay buffer: buffer is not active for slot {Slot}", slot);
                return false;
            }

            await ReplayBufferSaveLock.WaitAsync();
            try
            {
                string? savedPath = await SaveReplayBufferInternalGetPathAsync(slot);
                if (string.IsNullOrEmpty(savedPath))
                    return false;

                Log.Information($"Replay buffer saved to: {savedPath}");
                var recording = AppState.Instance.GetRecording(slot);
                string game = recording?.Game ?? "Unknown";
                string? exePath = recording?.ExePath;
                int? igdbId = !string.IsNullOrEmpty(exePath) ? GameUtils.GetIgdbIdFromExePath(exePath) : null;

                await EnsureFileReady(savedPath);

                TimeSpan savedDuration;
                try
                {
                    savedDuration = await FFmpegService.GetVideoDuration(savedPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not read replay buffer save duration");
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    _replaySavedBySlot[slot] = false;
                    return false;
                }

                if (savedDuration.TotalSeconds < 0.5)
                {
                    Log.Warning(
                        "Replay buffer save too short ({Seconds:0.###}s), discarding duplicate/empty save",
                        savedDuration.TotalSeconds);
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    _replaySavedBySlot[slot] = false;
                    return false;
                }

                await ContentService.CreateMetadataFile(savedPath, Content.ContentType.Buffer, game, igdbId: igdbId, audioTrackNames: recording?.AudioTrackNames);
                await ContentService.CreateThumbnail(savedPath, Content.ContentType.Buffer);
                _ = Task.Run(async () => await ContentService.CreateWaveformFile(savedPath, Content.ContentType.Buffer));

                await SettingsService.LoadContentFromFolderIntoState(true);

                Log.Information("Replay buffer save process completed successfully (slot {Slot})", slot);

                await ResetReplayBuffer(slot);

                _replaySavedBySlot[slot] = false;

                return true;
            }
            finally
            {
                ReplayBufferSaveLock.Release();
            }
        }

        /// <summary>
        /// VRChat VVMW: triggers a replay buffer save, then keeps only the last <paramref name="tailDurationSeconds"/>
        /// (PlaybackEnded ??PlaybackStart wall time) into Clips via FFmpeg <c>-sseof</c>. Does not register a full Replay Buffer item.
        /// Requires Hybrid or Replay Buffer mode with an active buffer. Set replay buffer duration ??longest expected playback.
        /// </summary>
        public static async Task<bool> TrySaveReplayBufferTailAsClipAsync(
            int slot,
            double tailDurationSeconds,
            string clipTitle,
            string preferredOutputFileNameBase,
            int clipIgdbId)
        {
            var buffer = Pipeline(slot).BufferOutput;
            if (buffer == null || !buffer.IsActive)
                return false;
            if (tailDurationSeconds < VrChatVvmwIntegration.MinWallClipSeconds)
                return false;
            if (!FFmpegService.FFmpegExists())
            {
                Log.Warning("[VRChat VVMW] FFmpeg not found; cannot create clip from replay buffer");
                return false;
            }

            await ReplayBufferSaveLock.WaitAsync();
            try
            {
                string? savedPath = await SaveReplayBufferInternalGetPathAsync(slot);
                if (string.IsNullOrEmpty(savedPath))
                    return false;

                await EnsureFileReady(savedPath);

                var recording = AppState.Instance.GetRecording(slot);
                if (recording == null)
                {
                    Log.Warning("[VRChat VVMW] No active recording state for slot {Slot}; discarding replay save", slot);
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    _replaySavedBySlot[slot] = false;
                    return false;
                }

                TimeSpan fullDur;
                try
                {
                    fullDur = await FFmpegService.GetVideoDuration(savedPath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VRChat VVMW] Could not read replay save duration");
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    _replaySavedBySlot[slot] = false;
                    return false;
                }

                double fileSec = fullDur.TotalSeconds;
                double tail = Math.Min(tailDurationSeconds, fileSec);

                if (tailDurationSeconds > fileSec + 0.25)
                {
                    Log.Warning(
                        "[VRChat VVMW] Playback duration {Playback}s exceeds replay file length {File}s; clip will be shorter. Increase Replay Buffer duration in Video settings if needed.",
                        tailDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        fileSec.ToString("0.###", CultureInfo.InvariantCulture));
                }

                int cfgSec = Settings.Instance.ReplayBufferDuration;
                if (tailDurationSeconds > cfgSec + 0.5)
                {
                    Log.Warning(
                        "[VRChat VVMW] Playback duration {Playback}s exceeds configured replay buffer ring size ({Config}s); older footage may be lost. Increase replay buffer duration.",
                        tailDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                        cfgSec);
                }

                if (tail <= 0.25)
                {
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    _replaySavedBySlot[slot] = false;
                    return false;
                }

                string game = recording.Game ?? "Unknown";
                string sanitizedGame = StorageService.SanitizeGameNameForFolder(game);
                string clipsRoot = Path.Combine(Settings.Instance.ContentFolder, FolderNames.Clips, sanitizedGame);
                Directory.CreateDirectory(clipsRoot);

                string baseName = StorageService.SanitizeGameNameForFolder((preferredOutputFileNameBase ?? "clip").Trim());
                string outPath = GetNextAvailableVvmwClipPath(clipsRoot, baseName);
                string tempOut = Path.Combine(Path.GetTempPath(), $"vvmw_tail_{Guid.NewGuid():N}.mp4");

                string tailArg = tail.ToString("0.###", CultureInfo.InvariantCulture);
                try
                {
                    await FFmpegService.RunSimple(new[]
                    {
                        // Keep all streams (video + every audio track) when trimming replay buffer tails.
                        // Default mapping may keep only a single audio stream.
                        "-y", "-sseof", "-" + tailArg, "-i", savedPath, "-map", "0", "-c", "copy", "-movflags", "+faststart", tempOut,
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VRChat VVMW] FFmpeg failed to trim replay buffer save");
                    TryDeleteReplayTempFile(tempOut);
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer(slot);
                    _replaySavedBySlot[slot] = false;
                    return false;
                }

                TryDeleteReplayTempFile(savedPath);

                if (!File.Exists(tempOut))
                {
                    await ResetReplayBuffer(slot);
                    _replaySavedBySlot[slot] = false;
                    return false;
                }

                try
                {
                    File.Move(tempOut, outPath, overwrite: false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[VRChat VVMW] Failed to move trimmed clip");
                    TryDeleteReplayTempFile(tempOut);
                    await ResetReplayBuffer(slot);
                    _replaySavedBySlot[slot] = false;
                    return false;
                }

                await EnsureFileReady(outPath);

                await ContentService.CreateMetadataFile(outPath, Content.ContentType.Clip, game, null, clipTitle, igdbId: clipIgdbId, audioTrackNames: recording.AudioTrackNames);
                await ContentService.CreateThumbnail(outPath, Content.ContentType.Clip);
                await ContentService.CreateWaveformFile(outPath, Content.ContentType.Clip);
                await SettingsService.LoadContentFromFolderIntoState(true);

                Log.Information("[VRChat VVMW] Saved replay tail clip: {Path}", outPath);

                await ResetReplayBuffer(slot);
                _replaySavedBySlot[slot] = false;
                return true;
            }
            finally
            {
                ReplayBufferSaveLock.Release();
            }
        }

        /// <summary>
        /// Uses <c>{baseName}.mp4</c>, or <c>{baseName}_2.mp4</c>, <c>{baseName}_3.mp4</c>, ??when the base name is already taken (same pattern intent as manual clips avoiding collisions).
        /// </summary>
        private static string GetNextAvailableVvmwClipPath(string clipsRoot, string baseName)
        {
            string outPath = Path.Combine(clipsRoot, $"{baseName}.mp4");
            int n = 2;
            while (File.Exists(outPath))
            {
                outPath = Path.Combine(clipsRoot, $"{baseName}_{n}.mp4");
                n++;
            }

            return outPath;
        }

        private static void TryDeleteReplayTempFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TryDeleteReplayTempFile {Path}", path);
            }
        }

        /// <summary>Waits for OBS replay save callback and returns the saved file path, or null on failure.</summary>
        private static async Task<string?> SaveReplayBufferInternalGetPathAsync(int slot)
        {
            var buffer = Pipeline(slot).BufferOutput;
            if (buffer == null)
                return null;

            Log.Information("Attempting to save replay buffer (slot {Slot})...", slot);
            _replaySavedBySlot[slot] = false;

            try
            {
                buffer.Save();
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to save replay buffer (slot {slot}): {ex.Message}");
                return null;
            }

            Log.Information("Waiting for replay buffer saved callback (slot {Slot})...", slot);
            int attempts = 0;
            while (!_replaySavedBySlot[slot] && attempts < 50)
            {
                await Task.Delay(100);
                attempts++;
            }

            if (!_replaySavedBySlot[slot])
            {
                Log.Warning("Replay buffer may not have saved correctly (slot {Slot})", slot);
                return null;
            }

            string? savedPath = buffer.GetLastReplayPath();

            for (int i = 0; i < 10 && string.IsNullOrEmpty(savedPath); i++)
            {
                savedPath = buffer.GetLastReplayPath();
                if (string.IsNullOrEmpty(savedPath))
                    await Task.Delay(100);
            }

            if (string.IsNullOrEmpty(savedPath))
            {
                Log.Error("Replay buffer path is null or empty (slot {Slot})", slot);
                return null;
            }

            return savedPath;
        }

        /// <summary>
        /// Stops and restarts the replay buffer so that subsequent saves
        /// only contain footage recorded after the last save.
        /// </summary>
        private static async Task ResetReplayBuffer(int slot)
        {
            var buffer = Pipeline(slot).BufferOutput;
            if (buffer == null)
                return;

            Log.Information("Resetting replay buffer (slot {Slot})...", slot);

            bool stopped = buffer.Stop(waitForCompletion: true, timeoutMs: 30000);

            if (!stopped)
            {
                Log.Warning("Replay buffer did not stop within timeout for reset (slot {Slot}). Forcing stop.", slot);
                buffer.ForceStop();
                await Task.Delay(500);
            }

            bool started = buffer.Start();

            if (!started)
            {
                string error = buffer.LastError ?? "Unknown error";
                Log.Error($"Failed to restart replay buffer after reset (slot {slot}): {error}");
            }
            else
            {
                Log.Information("Replay buffer restarted successfully (slot {Slot})", slot);
            }
        }

        /// <summary>
        /// Processes OBS log messages from the queue asynchronously.
        /// This runs on a background thread to prevent blocking OBS's internal logging thread.
        /// </summary>
        private static async Task ProcessLogQueueAsync()
        {
            await foreach (var (level, formattedMessage) in _logChannel.Reader.ReadAllAsync())
            {
                try
                {
                    // OBS can spam TS smoothing logs at very high frequency, which can flood
                    // the unbounded queue and eventually exhaust memory.
                    if (formattedMessage.Contains("Audio timestamp for '", StringComparison.Ordinal)
                        && formattedMessage.Contains("TS_SMOOTHING_THRESHOLD", StringComparison.Ordinal))
                    {
                        _suppressedAudioTsLogCount++;
                        DateTime now = DateTime.UtcNow;
                        if ((now - _lastSuppressedAudioTsLogAtUtc).TotalSeconds >= 5)
                        {
                            Log.Debug("Suppressed {Count} noisy OBS audio timestamp smoothing logs in the last interval.", _suppressedAudioTsLogCount);
                            _suppressedAudioTsLogCount = 0;
                            _lastSuppressedAudioTsLogAtUtc = now;
                        }
                        continue;
                    }

                    Log.Information($"{(ObsLogLevel)level}: {formattedMessage}");

                    if (formattedMessage.Contains("capture window no longer exists, terminating capture"))
                    {
                        int? affectedSlot = null;
                        for (int i = 0; i < MaxSessionSlots; i++)
                        {
                            if (formattedMessage.Contains($"gameplay_{i}", StringComparison.Ordinal))
                            {
                                affectedSlot = i;
                                break;
                            }
                        }

                        Log.Information("Capture window no longer exists, waiting a second to make sure it's not a false positive.");
                        await Task.Delay(1000);

                        if (affectedSlot is int slot)
                        {
                            Log.Information("Checking if hook is still active for slot {Slot}: {StillHooked}", slot, _isStillHookedAfterUnhookBySlot[slot]);
                            if (AppState.Instance.GetRecording(slot) != null
                                && Pipeline(slot).GameCapture != null
                                && !IsSlotCaptureHooked(slot)
                                && !_isStillHookedAfterUnhookBySlot[slot])
                            {
                                Log.Information("Capture stopped for slot {Slot}. Stopping recording.", slot);
                                ScheduleStopRecordingSlot(slot);
                            }
                            _isStillHookedAfterUnhookBySlot[slot] = false;
                        }
                        else
                        {
                            for (int i = 0; i < MaxSessionSlots; i++)
                            {
                                Log.Information("Checking if hook is still active for slot {Slot}: {StillHooked}", i, _isStillHookedAfterUnhookBySlot[i]);
                                if (AppState.Instance.GetRecording(i) != null
                                    && Pipeline(i).GameCapture != null
                                    && !IsSlotCaptureHooked(i)
                                    && !_isStillHookedAfterUnhookBySlot[i])
                                {
                                    Log.Information("Capture stopped for slot {Slot}. Stopping recording.", i);
                                    ScheduleStopRecordingSlot(i);
                                }
                                _isStillHookedAfterUnhookBySlot[i] = false;
                            }
                        }
                    }

                    if (formattedMessage.Contains("existing hook found"))
                    {
                        for (int i = 0; i < MaxSessionSlots; i++)
                        {
                            if (formattedMessage.Contains($"gameplay_{i}", StringComparison.Ordinal))
                                _isStillHookedAfterUnhookBySlot[i] = true;
                        }
                    }

                    if (formattedMessage.Contains("[game-capture: 'gameplay_", StringComparison.Ordinal))
                    {
                        for (int i = 0; i < MaxSessionSlots; i++)
                        {
                            if (formattedMessage.Contains($"gameplay_{i}", StringComparison.Ordinal))
                            {
                                _lastGameCaptureLogSlot = i;
                                break;
                            }
                        }
                    }

                    // Parse window dimensions from OBS game capture logs
                    if (formattedMessage.Contains("BufferDesc.Width:"))
                    {
                        var match = BufferDescWidthRegex().Match(formattedMessage);
                        if (match.Success && uint.TryParse(match.Groups[1].Value, out uint width))
                        {
                            int slot = _lastGameCaptureLogSlot >= 0 ? _lastGameCaptureLogSlot : 0;
                            _capturedWindowWidthBySlot[slot] = width;
                            if (slot == 0)
                                CapturedWindowWidth = width;
                            Log.Information($"Captured window width for slot {slot}: {width}");
                        }
                    }

                    if (formattedMessage.Contains("BufferDesc.Height:"))
                    {
                        var match = BufferDescHeightRegex().Match(formattedMessage);
                        if (match.Success && uint.TryParse(match.Groups[1].Value, out uint height))
                        {
                            int slot = _lastGameCaptureLogSlot >= 0 ? _lastGameCaptureLogSlot : 0;
                            _capturedWindowHeightBySlot[slot] = height;
                            if (slot == 0)
                                CapturedWindowHeight = height;
                            Log.Information($"Captured window height for slot {slot}: {height}");
                        }
                    }

                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                    if (e.StackTrace != null)
                    {
                        Log.Error(e.StackTrace);
                    }
                }
            }
        }

        private static bool AnySessionOutputActive()
        {
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (Pipeline(i).SessionOutput != null)
                    return true;
            }
            return false;
        }

        private static int AllocateSessionSlotIndex(bool startManually)
        {
            _ = startManually;
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (AppState.Instance.GetRecording(i) != null)
                    continue;
                if (Pipeline(i).SessionOutput != null)
                    continue;
                if (Pipeline(i).BufferOutput != null)
                    continue;
                return i;
            }
            return -1;
        }

        private static void ClearPendingPreRecordingForSlot(int slot) =>
            AppState.Instance.ClearPreRecording(slot);

        private static void ClearAllPendingPreRecordings() =>
            AppState.Instance.ClearAllPreRecordings();

        private static bool SlotHasPipelineResources(int slot)
        {
            var pl = Pipeline(slot);
            return pl.MainScene != null
                || pl.SessionOutput != null
                || pl.BufferOutput != null
                || pl.GameCapture != null
                || pl.VideoEncoder != null;
        }

        private static bool ShouldStopSlot(int slot)
        {
            if (AppState.Instance.GetRecording(slot) != null)
                return true;
            if (SlotHasPipelineResources(slot))
                return true;
            return false;
        }

        /// <summary>
        /// Tears down a slot that never reached a stable recording state (e.g. start cancelled or failed mid-setup).
        /// </summary>
        private static void CleanupPartialSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSessionSlots)
                return;

            StopGameCaptureHookTimeoutTimer(slot);

            var pl = Pipeline(slot);
            try
            {
                if (pl.BufferOutput != null)
                {
                    if (!pl.BufferOutput.Stop(waitForCompletion: false))
                        pl.BufferOutput.ForceStop();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to stop replay buffer during partial cleanup (slot {Slot}): {Message}", slot, ex.Message);
            }

            try
            {
                if (pl.SessionOutput != null)
                {
                    if (!pl.SessionOutput.Stop(waitForCompletion: false))
                        pl.SessionOutput.ForceStop();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to stop session output during partial cleanup (slot {Slot}): {Message}", slot, ex.Message);
            }

            DisposeOutput(slot);
            DisposeSources(slot);
            DisposeEncoders(slot);

            if (slot == 0 && !IsAnotherSlotRecording(slot))
                Obs.SetOutputSource(0, (Scene?)null);

            pl.HookedExecutableFileName = null;
            ClearPendingPreRecordingForSlot(slot);
            ClearCapturedWindowDimensions(slot);
        }

        public static async Task InitializeAsync()
        {
            // Detect GPU vendor early in initialization
            DetectGpuVendor();

            if (IsInitialized)
                return;

            try
            {
                await CheckIfExistsOrDownloadAsync();
            }
            catch (Exception ex)
            {
                Log.Error($"OBS installation failed: {ex.Message}");
                await MessageService.ShowModal(
                    "Recorder Error",
                    "The recorder installation failed. Please check your internet connection and try again. If you have any games running, please close them and restart Segra.",
                    "error",
                    "Could not install recorder"
                );
                AppState.Instance.HasLoadedObs = true;
                return;
            }

            if (Obs.IsInitialized)
                throw new Exception("Error: OBS is already initialized.");

            // Start the log queue processor before setting the log handler
            _ = Task.Run(ProcessLogQueueAsync);

            try
            {
                // Initialize OBS using ObsKit.NET fluent API
                _obsContext = Obs.Initialize(config =>
                {
                    config
                        .WithLocale("en-US")
                        .WithDataPath("./data/libobs/")
                        .WithModulePath("./obs-plugins/64bit/", "./data/obs-plugins/%module%/")
                        .WithVideo(v => v
                            .Resolution(1920, 1080)
                            .Fps(60))
                        .WithAudio(a => a
                            .WithSampleRate(44100)
                            .WithSpeakers(SpeakerLayout.Stereo))
                        .WithLogging((level, message) =>
                        {
                            try
                            {
                                // Queue the message for async processing - this is non-blocking
                                _logChannel.Writer.TryWrite(((int)level, message));
                            }
                            catch
                            {
                                // Silently ignore marshaling errors to never block OBS
                            }
                        });
                });

                // Disable auto-dispose for manual resource management
                Obs.AutoDispose = false;

                InstalledOBSVersion = Obs.Version;
                Log.Information("OBS version: " + InstalledOBSVersion);

                // Set available encoders in state
                SetAvailableEncodersInState();

                IsInitialized = true;
                AppState.Instance.HasLoadedObs = true;
                Log.Information("OBS initialized successfully!");

                _ = Task.Run(RecoveryService.CheckForOrphanedFilesAsync);
                GameDetectionService.StartAsync();
                GameDetectionService.ForegroundHook.Start();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize OBS: {ex.Message}");
                await MessageService.ShowModal(
                    "Recorder Error",
                    "Failed to initialize the recorder. Please check the logs for more details.",
                    "error",
                    "Could not initialize recorder"
                );
                AppState.Instance.HasLoadedObs = true;
            }
        }

        public static void Shutdown()
        {
            if (!IsInitialized)
            {
                Log.Information("OBS is not initialized, skipping shutdown");
                return;
            }

            try
            {
                Log.Information("Shutting down OBS...");

                // Dispose the OBS context to properly clean up OBS resources
                _obsContext?.Dispose();
                _obsContext = null;

                IsInitialized = false;
                Log.Information("OBS shutdown completed successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during OBS shutdown");
            }
        }

        /// <summary>
        /// Configures OBS video settings based on the provided dimensions.
        /// </summary>
        /// <param name="is4by3">True if the content was detected as 4:3 and stretched to 16:9.</param>
        private static void ResetVideoSettings(out bool is4by3, uint? customFps = null, uint? customOutputWidth = null, uint? customOutputHeight = null)
        {
            SettingsService.GetPrimaryMonitorResolution(out uint baseWidth, out uint baseHeight);

            // Use custom values if provided, otherwise use defaults
            baseWidth = customOutputWidth ?? baseWidth;
            baseHeight = customOutputHeight ?? baseHeight;

            // Get the maximum height from resolution setting
            SettingsService.GetResolution(Settings.Instance.Resolution, out uint maxWidth, out uint maxHeight);

            // Calculate output dimensions respecting the max height cap while preserving aspect ratio
            uint outputWidth = baseWidth;
            uint outputHeight = baseHeight;

            // Check if the input aspect ratio is close to 4:3 (1.33)
            double aspectRatio = (double)baseWidth / baseHeight;
            is4by3 = Math.Abs(aspectRatio - 4.0 / 3.0) < 0.1 && Settings.Instance.Stretch4By3;

            // If the content is 4:3 and stretching is enabled, stretch it to 16:9 while preserving height
            // Only modify output dimensions, not base dimensions (base = actual capture size)
            if (is4by3)
            {
                // Calculate 16:9 width based on the current height for output only
                outputWidth = (uint)(baseHeight * (16.0 / 9.0));
                Log.Information($"Stretching 4:3 content to 16:9: {baseWidth}x{baseHeight} -> {outputWidth}x{outputHeight}");
            }

            // If content height exceeds max height setting, downscale proportionally
            if (outputHeight > maxHeight)
            {
                double scale = (double)maxHeight / outputHeight;
                outputWidth = (uint)(outputWidth * scale);
                outputHeight = maxHeight;

                // Round to nearest multiple of 4 (required by video encoders)
                // Example: 1279 ??1280 instead of OBS rounding down to 1276
                outputWidth = (uint)(Math.Round(outputWidth / 4.0) * 4);
                outputHeight = (uint)(Math.Round(outputHeight / 4.0) * 4);

                Log.Information($"Downscaling from {baseWidth}x{baseHeight} to {outputWidth}x{outputHeight} (max height: {maxHeight})");
            }

            (baseWidth, baseHeight) = EnsureEvenDimensions(baseWidth, baseHeight);

            _currentBaseWidth = baseWidth;
            _currentBaseHeight = baseHeight;

            Obs.SetVideo(v => v
                .BaseResolution(baseWidth, baseHeight)
                .OutputResolution(outputWidth, outputHeight)
                .Fps(customFps ?? 60));
        }

        public static bool StartRecording(string name = "Manual Recording", string exePath = "Unknown", bool startManually = false, int? pid = null, int? reservedSlot = null)
        {
            // Wait for pending StopRecording to complete before starting. Prevents race conditions where a new recording starts before cleanup finishes
            _stopRecordingSemaphore.Wait();
            _stopRecordingSemaphore.Release();

            if (!IsOBSInstalled())
            {
                Log.Information("OBS is not installed. Skipping recording.");
                return false;
            }

            if (!IsInitialized)
            {
                Log.Information("OBS is not initialized. Skipping recording.");
                return false;
            }

            bool isReplayBufferMode = Settings.Instance.RecordingMode == RecordingMode.Buffer;
            bool isSessionMode = Settings.Instance.RecordingMode == RecordingMode.Session;
            bool isHybridMode = Settings.Instance.RecordingMode == RecordingMode.Hybrid;

            string fileName = Path.GetFileName(exePath);

            int slot;
            if (reservedSlot.HasValue)
            {
                slot = reservedSlot.Value;
                if (slot < 0 || slot >= MaxSessionSlots)
                {
                    Log.Information("Cannot start recording: invalid reserved slot {Slot}.", slot);
                    ClearAllPendingPreRecordings();
                    return false;
                }
                if (AppState.Instance.GetRecording(slot) != null)
                {
                    Log.Information("Cannot start recording: slot {Slot} already has an active recording.", slot);
                    ClearPendingPreRecordingForSlot(slot);
                    return false;
                }
            }
            else
            {
                slot = AllocateSessionSlotIndex(startManually);
                if (slot < 0)
                {
                    Log.Information("Cannot start recording: no free output slot or invalid mode.");
                    ClearAllPendingPreRecordings();
                    return false;
                }
            }

            _slotLifecycleLocks[slot].Wait();
            try
            {
                if (!startManually && reservedSlot.HasValue && AppState.Instance.GetPreRecording(slot) == null)
                {
                    Log.Information("Start recording for slot {Slot} was cancelled.", slot);
                    return false;
                }

                _isStoppingSlot[slot] = false;

                var pl = Pipeline(slot);
                bool anotherSlotActive = IsAnotherSlotRecording(slot);
                bool usesDedicatedCanvas = slot >= 1;
                uint? dedicatedCanvasWidth = null;
                uint? dedicatedCanvasHeight = null;

                // OBS video settings are global; slot 1+ uses a dedicated canvas and must not touch global video.
                if (slot == 0 && !anotherSlotActive)
                    ResetVideoSettings(out _, customFps: (uint)Settings.Instance.FrameRate);
                else if (slot == 0 && anotherSlotActive)
                    Log.Information("Skipping global video settings change for slot {Slot}: another slot is already recording", slot);
                else
                    Log.Information("Skipping global video settings change for slot {Slot}: uses dedicated canvas", slot);

                // Secondary slots always resolve game dimensions before creating their canvas.
                if (!startManually && usesDedicatedCanvas)
                {
                    if (!WindowUtils.GetWindowDimensionsByPreRecordingExeOrPid(out uint windowWidth, out uint windowHeight, slot))
                    {
                        CleanupPartialSlot(slot);
                        return false;
                    }

                    (windowWidth, windowHeight) = EnsureEvenDimensions(windowWidth, windowHeight);
                    dedicatedCanvasWidth = windowWidth;
                    dedicatedCanvasHeight = windowHeight;
                    Log.Information(
                        "Using dedicated canvas dimensions for slot {Slot}: {Width}x{Height}",
                        slot, windowWidth, windowHeight);
                }

                CreateRecordingScene(slot, pl, dedicatedCanvasWidth, dedicatedCanvasHeight);

                // For manual recording, use display capture directly without game hooking
                if (startManually)
                {
                    Log.Information("Manual recording started - using display capture");
                    AddMonitorCapture(slot);
                    pl.DisplayItem?.SetBounds(ObsBoundsType.ScaleInner, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
                }
                else
                {
                    AddMonitorCapture(slot);

                    try
                    {
                        pl.GameCapture = new GameCapture($"gameplay_{slot}", GameCapture.CaptureMode.SpecificWindow);
                        pl.GameCapture.SetWindow($"*:*:{fileName}");

                        if (Settings.Instance.AudioOutputMode != AudioOutputMode.All)
                        {
                            pl.GameCapture.Update(s => s.Set("capture_audio", true));
                            Log.Information($"Game capture audio enabled (mode: {Settings.Instance.AudioOutputMode})");
                        }

                        Log.Information($"Game capture configured for: {fileName}");

                        pl.GameCaptureItem = pl.MainScene!.AddSource(pl.GameCapture);

                        StartGameCaptureHookTimeoutTimer(slot);

                        pl.HookedSubscription = c => OnGameCaptureHookedEvent(c, slot);
                        pl.UnhookedSubscription = c => OnGameCaptureUnhookedEvent(c, slot);
                        pl.GameCapture.Hooked += pl.HookedSubscription;
                        pl.GameCapture.Unhooked += pl.UnhookedSubscription;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"Game Capture source not available: {ex.Message}. Using Display Capture only.");
                        pl.GameCapture = null;
                    }

                    if (dedicatedCanvasWidth.HasValue && dedicatedCanvasHeight.HasValue)
                    {
                        pl.GameCaptureItem?.SetBounds(
                            ObsBoundsType.ScaleInner,
                            dedicatedCanvasWidth.Value,
                            dedicatedCanvasHeight.Value).SetPosition(0, 0);
                        pl.DisplayItem?.SetBounds(
                            ObsBoundsType.ScaleInner,
                            dedicatedCanvasWidth.Value,
                            dedicatedCanvasHeight.Value).SetPosition(0, 0);
                    }
                    else if (WindowUtils.GetWindowDimensionsByPreRecordingExeOrPid(out uint windowWidth, out uint windowHeight, slot))
                    {
                        if (slot == 0 && !anotherSlotActive)
                        {
                            ResetVideoSettings(
                                out bool is4by3,
                                customFps: (uint)Settings.Instance.FrameRate,
                                customOutputWidth: windowWidth,
                                customOutputHeight: windowHeight
                            );

                            var boundsType = is4by3 ? ObsBoundsType.Stretch : ObsBoundsType.ScaleInner;
                            pl.GameCaptureItem?.SetBounds(boundsType, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
                            pl.DisplayItem?.SetBounds(boundsType, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
                        }
                        else if (usesDedicatedCanvas)
                        {
                            (windowWidth, windowHeight) = EnsureEvenDimensions(windowWidth, windowHeight);
                            Log.Information(
                                "Applying dedicated canvas bounds for slot {Slot} ({Width}x{Height})",
                                slot, windowWidth, windowHeight);
                            pl.GameCaptureItem?.SetBounds(ObsBoundsType.ScaleInner, windowWidth, windowHeight).SetPosition(0, 0);
                            pl.DisplayItem?.SetBounds(ObsBoundsType.ScaleInner, windowWidth, windowHeight).SetPosition(0, 0);
                        }
                        else
                        {
                            (windowWidth, windowHeight) = EnsureEvenDimensions(windowWidth, windowHeight);
                            Log.Information(
                                "Using existing canvas dimensions for slot {Slot} game bounds ({Width}x{Height})",
                                slot, windowWidth, windowHeight);
                            pl.GameCaptureItem?.SetBounds(ObsBoundsType.ScaleInner, windowWidth, windowHeight).SetPosition(0, 0);
                            pl.DisplayItem?.SetBounds(ObsBoundsType.ScaleInner, windowWidth, windowHeight).SetPosition(0, 0);
                        }
                    }
                    else
                    {
                        CleanupPartialSlot(slot);
                        return false;
                    }
                }

                // Only slot 0 drives the main program mix. Additional slots record from their own canvas.
                if (slot == 0)
                    Obs.SetOutputSource(0, pl.MainScene);

                // Create video encoder
                string encoderId = Settings.Instance.Codec!.InternalEncoderId;
                Log.Information($"Using encoder: {Settings.Instance.Codec!.FriendlyName} ({encoderId})");

                using var videoEncoderSettings = new ObsKit.NET.Core.Settings();
                videoEncoderSettings.Set("preset", "Quality");
                videoEncoderSettings.Set("profile", "high");
                videoEncoderSettings.Set("use_bufsize", true);
                videoEncoderSettings.Set("rate_control", Settings.Instance.RateControl);
                videoEncoderSettings.Set("keyint_sec", 1);

                switch (Settings.Instance.RateControl)
                {
                    case "CBR":
                        int targetBitrateKbps = Settings.Instance.Bitrate * 1000;
                        videoEncoderSettings.Set("bitrate", targetBitrateKbps);
                        videoEncoderSettings.Set("max_bitrate", targetBitrateKbps);
                        videoEncoderSettings.Set("bufsize", targetBitrateKbps);
                        break;

                    case "VBR":
                        int minBitrateKbps = Settings.Instance.MinBitrate * 1000;
                        int maxBitrateKbps = Settings.Instance.MaxBitrate * 1000;
                        videoEncoderSettings.Set("bitrate", minBitrateKbps);
                        videoEncoderSettings.Set("max_bitrate", maxBitrateKbps);
                        videoEncoderSettings.Set("bufsize", maxBitrateKbps);
                        break;

                    case "CRF":
                        // Software x264 path mainly; no explicit bitrate
                        videoEncoderSettings.Set("crf", Settings.Instance.CrfValue);
                        break;

                    case "CQP":
                        // Hardware encoders (NVENC/QSV/AMF) often use cqp/cq; provide both cqp and qp for compatibility
                        videoEncoderSettings.Set("cqp", Settings.Instance.CqLevel);
                        videoEncoderSettings.Set("qp", Settings.Instance.CqLevel);
                        break;

                    case "CQVBR":
                        // OBS 31+ NVENC: Variable Bitrate with Target Quality (caps peak bitrate while targeting CQ)
                        if (!encoderId.Contains("nvenc", StringComparison.OrdinalIgnoreCase))
                        {
                            ClearAllPendingPreRecordings();
                            throw new Exception("CQVBR is only supported with NVIDIA NVENC encoders (OBS 31+).");
                        }

                        int cqvbrMaxKbps = Settings.Instance.MaxBitrate * 1000;
                        videoEncoderSettings.Set("max_bitrate", cqvbrMaxKbps);
                        int maxTq = encoderId.Contains("av1", StringComparison.OrdinalIgnoreCase) ? 63 : 51;
                        int targetQ = Math.Clamp(Settings.Instance.CqLevel, 1, maxTq);
                        videoEncoderSettings.Set("target_quality", targetQ);
                        break;

                    default:
                        ClearAllPendingPreRecordings();
                        throw new Exception("Unsupported Rate Control method.");
                }

                // Disable HEVC b-frames on older NVIDIA GPUs (requires compute capability >= 7.0)
                if (encoderId.Equals("jim_hevc_nvenc", StringComparison.OrdinalIgnoreCase) &&
                    AppState.Instance.CudaComputeCapability != null &&
                    AppState.Instance.CudaComputeCapability < 7.0)
                {
                    videoEncoderSettings.Set("bf", 0);
                    Log.Information("NVENC b-frames disabled (CUDA compute capability < 7.0)");
                }

                pl.VideoEncoder = new VideoEncoder(encoderId, ObsName(slot, "Segra Recorder"), videoEncoderSettings);

                var audioOutputMode = Settings.Instance.AudioOutputMode;

                // Mic/desktop WASAPI + Discord must be created once across dual slots (game capture_audio may duplicate).
                lock (_sharedAudioLock)
                {
                    var existingSharedAudio = FindSharedAudioOwner(excludeSlot: slot);
                    if (existingSharedAudio != null)
                    {
                        Log.Information(
                            "Skipping new WASAPI/Discord audio sources for slot {Slot}: reusing sources from the other recording slot",
                            slot);

                        foreach (var micSource in existingSharedAudio.MicSources)
                            AttachSharedAudioSourceToScene(pl.MainScene!, micSource, "microphone");
                        foreach (var desktopSource in existingSharedAudio.DesktopSources)
                            AttachSharedAudioSourceToScene(pl.MainScene!, desktopSource, "desktop");
                        if (existingSharedAudio.DiscordAudioSource != null)
                            AttachSharedAudioSourceToScene(pl.MainScene!, existingSharedAudio.DiscordAudioSource, "discord");
                    }
                    else
                    {
                        // Create audio sources and add to scene
                        if (Settings.Instance.InputDevices != null && Settings.Instance.InputDevices.Count > 0)
                        {
                            foreach (var deviceSetting in Settings.Instance.InputDevices)
                            {
                                if (!string.IsNullOrEmpty(deviceSetting.Id))
                                {
                                    string sourceName = ObsName(slot, $"Microphone_{pl.MicSources.Count + 1}");
                                    var micSource = deviceSetting.Id == "default"
                                        ? AudioInputCapture.FromDefault(sourceName)
                                        : AudioInputCapture.FromDevice(deviceSetting.Id, sourceName);

                                    SetForceMono(micSource, Settings.Instance.ForceMonoInputSources);

                                    micSource.Volume = deviceSetting.Volume;

                                    pl.MainScene!.AddSource(micSource);
                                    pl.MicSources.Add(micSource);

                                    if (Settings.Instance.InputNoiseSuppression)
                                    {
                                        try
                                        {
                                            var noiseGate = new Source("noise_gate_filter", $"{sourceName}_NoiseGate");
                                            noiseGate.Update(s =>
                                            {
                                                s.Set("close_threshold", -48.0);
                                                s.Set("open_threshold", -42.0);
                                                s.Set("attack_time", 25L);
                                                s.Set("hold_time", 200L);
                                                s.Set("release_time", 150L);
                                            });
                                            micSource.AddFilter(noiseGate);
                                            Log.Information($"Added noise suppression filter to {sourceName}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Warning($"Failed to add noise suppression filter to {sourceName}: {ex.Message}");
                                        }
                                    }

                                    Log.Information($"Added input device: {deviceSetting.Id} as {sourceName} with volume {deviceSetting.Volume}");
                                }
                            }
                        }

                        // Always add desktop audio sources - they serve as fallback until game hooks in GameOnly/GameAndDiscord modes
                        if (Settings.Instance.OutputDevices != null && Settings.Instance.OutputDevices.Count > 0)
                        {
                            foreach (var deviceSetting in Settings.Instance.OutputDevices)
                            {
                                if (!string.IsNullOrEmpty(deviceSetting.Id))
                                {
                                    string sourceName = ObsName(slot, $"DesktopAudio_{pl.DesktopSources.Count + 1}");
                                    var desktopSource = deviceSetting.Id == "default"
                                        ? AudioOutputCapture.FromDefault(sourceName)
                                        : AudioOutputCapture.FromDevice(deviceSetting.Id, sourceName);

                                    desktopSource.Volume = deviceSetting.Volume;

                                    pl.MainScene!.AddSource(desktopSource);
                                    pl.DesktopSources.Add(desktopSource);

                                    Log.Information($"Added output device: {deviceSetting.Name} ({deviceSetting.Id}) as {sourceName} with volume {deviceSetting.Volume}");
                                }
                            }
                        }

                        // In GameAndDiscord mode, also create Discord application audio capture (starts muted until game hooks)
                        if (audioOutputMode == AudioOutputMode.GameAndDiscord && pl.GameCapture != null)
                        {
                            try
                            {
                                pl.DiscordAudioSource = new Source("wasapi_process_output_capture", ObsName(slot, "Discord Audio"));
                                pl.DiscordAudioSource.Update(s =>
                                {
                                    s.Set("window", "Discord:Chrome_WidgetWin_1:Discord.exe");
                                    s.Set("priority", 2);
                                });
                                pl.DiscordAudioSource.IsMuted = true;
                                pl.MainScene!.AddSource(pl.DiscordAudioSource);
                                Log.Information("Added Discord application audio capture source (muted until game hooks)");
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"Failed to create Discord audio capture source: {ex.Message}");
                                pl.DiscordAudioSource = null;
                            }
                        }
                    }
                }

                const int maxTracks = 6;
                bool separateTracks = Settings.Instance.EnableSeparateAudioTracks;
                pl.AudioEncoders.Clear();
                var actualAudioTrackNames = new List<string>();
                uint recordingTracksMask;
                int trackCount;

                if (!separateTracks)
                {
                    const uint singleTrackMask = 1u;
                    ApplyAudioTrackMask(pl.MicSources, singleTrackMask, "microphone");
                    ApplyAudioTrackMask(pl.DesktopSources, singleTrackMask, "desktop");
                    if (pl.GameCapture != null)
                        ApplyAudioTrackMask(pl.GameCapture, singleTrackMask, "game");
                    if (pl.DiscordAudioSource != null)
                        ApplyAudioTrackMask(pl.DiscordAudioSource, singleTrackMask, "discord");

                    trackCount = 1;
                    recordingTracksMask = 1;
                    actualAudioTrackNames.Add("Full Mix");

                    int recordingAudioKbps = GetRecordingAudioBitrateKbps();
                    pl.AudioEncoders.Add(AudioEncoder.CreateAac(ObsName(slot, "Full Mix"), recordingAudioKbps, 0));
                }
                else
                {
                    var inputDevices = Settings.Instance.InputDevices ?? new List<DeviceSetting>();
                    for (int i = 0; i < pl.MicSources.Count; i++)
                    {
                        uint mask = i < inputDevices.Count
                            ? SanitizeAudioTrackMask(inputDevices[i].AudioTrackMask)
                            : 1u;
                        ApplyAudioTrackMask(pl.MicSources[i], mask, $"microphone {i + 1}");
                    }

                    var outputDevices = Settings.Instance.OutputDevices ?? new List<DeviceSetting>();
                    for (int i = 0; i < pl.DesktopSources.Count; i++)
                    {
                        uint mask = i < outputDevices.Count
                            ? SanitizeAudioTrackMask(outputDevices[i].AudioTrackMask)
                            : 1u;
                        ApplyAudioTrackMask(pl.DesktopSources[i], mask, $"desktop {i + 1}");
                    }

                    if (pl.GameCapture != null)
                    {
                        ApplyAudioTrackMask(
                            pl.GameCapture,
                            SanitizeAudioTrackMask(Settings.Instance.GameAudioTrackMask),
                            "game");
                    }

                    if (pl.DiscordAudioSource != null)
                    {
                        ApplyAudioTrackMask(
                            pl.DiscordAudioSource,
                            SanitizeAudioTrackMask(Settings.Instance.DiscordAudioTrackMask),
                            "discord");
                    }

                    recordingTracksMask = 0;
                    foreach (var device in inputDevices.Where(d => !string.IsNullOrEmpty(d.Id)))
                        recordingTracksMask |= SanitizeAudioTrackMask(device.AudioTrackMask);
                    foreach (var device in outputDevices.Where(d => !string.IsNullOrEmpty(d.Id)))
                        recordingTracksMask |= SanitizeAudioTrackMask(device.AudioTrackMask);
                    if (pl.GameCapture != null)
                        recordingTracksMask |= SanitizeAudioTrackMask(Settings.Instance.GameAudioTrackMask);
                    // Discord may live on the other slot; still encode its track from the global mix.
                    if (pl.DiscordAudioSource != null || FindDiscordAudioSource() != null)
                        recordingTracksMask |= SanitizeAudioTrackMask(Settings.Instance.DiscordAudioTrackMask);

                    if (recordingTracksMask == 0)
                        recordingTracksMask = 1;

                    trackCount = maxTracks;
                    int recordingAudioKbps = GetRecordingAudioBitrateKbps();
                    var customTrackNames = Settings.Instance.RecordingAudioTrackNames;
                    for (int t = 0; t < maxTracks; t++)
                    {
                        string trackName = customTrackNames != null
                            && t < customTrackNames.Count
                            && !string.IsNullOrWhiteSpace(customTrackNames[t])
                            ? customTrackNames[t].Trim()
                            : $"Track {t + 1}";

                        actualAudioTrackNames.Add(trackName);
                        pl.AudioEncoders.Add(AudioEncoder.CreateAac(ObsName(slot, trackName), recordingAudioKbps, t));
                    }

                    Log.Information(
                        $"Multi-track recording: output mask 0x{recordingTracksMask:X}, {trackCount} encoder(s).");
                }

                // Paths for session recordings and buffer, organized by game
                string sanitizedGameName = StorageService.SanitizeGameNameForFolder(name);
                string sessionDir = Path.Combine(Settings.Instance.ContentFolder, FolderNames.Sessions, sanitizedGameName);
                string bufferDir = Path.Combine(Settings.Instance.ContentFolder, FolderNames.Buffers, sanitizedGameName);
                if (!Directory.Exists(sessionDir)) Directory.CreateDirectory(sessionDir);
                if (!Directory.Exists(bufferDir)) Directory.CreateDirectory(bufferDir);

                string? videoOutputPath = null; // only set for session/hybrid session output

                // Configure outputs depending on mode
                if (isReplayBufferMode || isHybridMode)
                {
                    uint bufferTracksMask = recordingTracksMask;

                    pl.BufferOutput = new ReplayBuffer(ObsName(slot, "replay_buffer"), Settings.Instance.ReplayBufferDuration, Settings.Instance.ReplayBufferMaxSize);
                    pl.BufferOutput.SetDirectory(bufferDir);
                    pl.BufferOutput.SetFilenameFormat("%CCYY-%MM-%DD_%hh-%mm-%ss");
                    pl.BufferOutput.Update(s => s.Set("extension", "mp4").Set("tracks", (long)bufferTracksMask));

                    if (pl.RecordingCanvas != null)
                        pl.BufferOutput.WithVideoEncoder(pl.VideoEncoder, pl.RecordingCanvas);
                    else
                        pl.BufferOutput.WithVideoEncoder(pl.VideoEncoder);

                    for (int t = 0; t < pl.AudioEncoders.Count; t++)
                    {
                        pl.BufferOutput.WithAudioEncoder(pl.AudioEncoders[t], track: t);
                    }

                    pl.ReplaySavedConnection = pl.BufferOutput.ConnectSignal(OutputSignal.Saved, _ => OnReplaySaved(slot));
                }

                if (isSessionMode || isHybridMode)
                {
                    videoOutputPath = $"{sessionDir}/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";

                    uint recordTracksMask = recordingTracksMask;

                    bool useHybridMp4 = SupportsHybridMp4();
                    Log.Information($"Using recording output type: {(useHybridMp4 ? "mp4_output" : "ffmpeg_muxer")} (Hybrid MP4: {useHybridMp4})");

                    if (useHybridMp4)
                    {
                        pl.SessionOutput = new RecordingOutput($"simple_output_{slot}", videoOutputPath);
                        pl.SessionOutput.SetFormat(RecordingFormat.HybridMp4);
                    }
                    else
                    {
                        pl.SessionOutput = new RecordingOutput($"simple_output_{slot}", videoOutputPath, "mp4");
                    }
                    pl.SessionOutput.Update(s => s.Set("tracks", (long)recordTracksMask));

                    if (pl.RecordingCanvas != null)
                        pl.SessionOutput.WithVideoEncoder(pl.VideoEncoder, pl.RecordingCanvas);
                    else
                        pl.SessionOutput.WithVideoEncoder(pl.VideoEncoder);

                    for (int t = 0; t < pl.AudioEncoders.Count; t++)
                    {
                        pl.SessionOutput.WithAudioEncoder(pl.AudioEncoders[t], track: t);
                    }
                }

                fileName = pl.HookedExecutableFileName ?? fileName;

                DateTime? startTime = null;
                bool hasPlayedStartSound = false;

                if (pl.SessionOutput != null)
                {
                    if (!pl.SessionOutput.Start())
                    {
                        string error = pl.SessionOutput.LastError ?? "Unknown error";
                        Log.Error($"Failed to start recording: {error}");
                        Task.Run(() => ShowModal("Recording failed", "Failed to start recording. Check the log for more details.", "error"));
                        Task.Run(() => PlaySound("error", 500));
                        ClearPendingPreRecordingForSlot(slot);
                        CleanupPartialSlot(slot);
                        return false;
                    }

                    // Set the exact start time for session recording (Full Session has bookmarks)
                    startTime = DateTime.Now;
                    _ = Task.Run(() => PlaySound("start"));
                    hasPlayedStartSound = true;

                    Log.Information("Session recording started successfully");
                }

                if (pl.BufferOutput != null)
                {
                    if (!pl.BufferOutput.Start())
                    {
                        string error = pl.BufferOutput.LastError ?? "Unknown error";
                        Log.Error($"Failed to start replay buffer (slot {slot}): {error}");
                        Task.Run(() => ShowModal("Replay buffer failed", "Failed to start replay buffer. Check the log for more details.", "error"));
                        Task.Run(() => PlaySound("error", 500));
                        ClearPendingPreRecordingForSlot(slot);
                        CleanupPartialSlot(slot);
                        return false;
                    }

                    if (!hasPlayedStartSound)
                    {
                        _ = Task.Run(() => PlaySound("start"));
                        hasPlayedStartSound = true;
                    }

                    Log.Information("Replay buffer started successfully (slot {Slot})", slot);
                }

                string? gameImage = GameIconUtils.ExtractIconAsBase64(exePath);

                var newRecording = new Recording()
                {
                    Slot = slot,
                    StartTime = startTime ?? DateTime.Now,
                    Game = name,
                    FilePath = videoOutputPath,
                    FileName = fileName,
                    Pid = pid,
                    IsUsingGameHook = IsSlotCaptureHooked(slot),
                    GameImage = gameImage,
                    ExePath = exePath,
                    CoverImageId = GameUtils.GetCoverImageIdFromExePath(exePath),
                    AudioTrackNames = actualAudioTrackNames,
                };
                AppState.Instance.SetRecording(slot, newRecording);

                ClearPendingPreRecordingForSlot(slot);
                Program.ShowMonitoringWindowIfClosed();
                uint previewFps = (uint)Math.Max(1, Settings.Instance.FrameRate);
                _ = Task.Run(async () =>
                {
                    await MessageService.SendSettingsToFrontend("OBS Start recording");
                    if (_isStoppingSlot[slot] || AppState.Instance.GetRecording(slot) == null)
                        return;
                    RecordingPreviewService.OnRecordingStarted(previewFps, slot);
                });

                NotifyIconService.SetNotifyIconStatus(NotifyIconState.Recording);

                Log.Information("Recording started: " + videoOutputPath);
                GeneralUtils.SetProcessPriority(ProcessPriorityClass.High);
                if (!isReplayBufferMode)
                {
                    _ = GameIntegrationService.Start(GameUtils.GetIgdbIdFromExePath(exePath), name, exePath, slot: slot);
                }
                else if (Settings.Instance.GameIntegrations.VrChat.Enabled &&
                         !string.IsNullOrEmpty(exePath) &&
                         exePath.EndsWith("VRChat.exe", StringComparison.OrdinalIgnoreCase))
                {
                    _ = GameIntegrationService.Start(GameUtils.GetIgdbIdFromExePath(exePath), name, exePath, slot: slot);
                }
                Task.Run(KeybindCaptureService.Start);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "StartRecording failed for slot {Slot}", slot);
                CleanupPartialSlot(slot);
                return false;
            }
            finally
            {
                _slotLifecycleLocks[slot].Release();
            }
        }

        /// <summary>Re-creates monitor capture (e.g. after display settings change while not game-hooked).</summary>
        public static void RefreshMonitorCaptureForSlot(int slotIndex)
        {
            DisposeDisplaySource(slotIndex);
            AddMonitorCapture(slotIndex);
        }

        public static void AddMonitorCapture(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.MainScene == null)
            {
                Log.Warning("Cannot add monitor capture: scene not created");
                return;
            }

            int monitorIndex = 0;

            if (Settings.Instance.SelectedDisplay != null)
            {
                int? foundIndex = AppState.Instance.Displays
                    .Select((d, i) => new { Display = d, Index = i })
                    .Where(x => x.Display.DeviceId == Settings.Instance.SelectedDisplay?.DeviceId)
                    .Select(x => (int?)x.Index)
                    .FirstOrDefault();

                if (foundIndex.HasValue)
                {
                    monitorIndex = foundIndex.Value;
                }
                else
                {
                    _ = MessageService.ShowModal("Display recording", $"Could not find selected display. Defaulting to first automatically detected display.", "warning");
                }
            }

            var captureMethod = Settings.Instance.DisplayCaptureMethod switch
            {
                DisplayCaptureMethod.DXGI => MonitorCaptureMethod.DesktopDuplication,
                DisplayCaptureMethod.WGC => MonitorCaptureMethod.WindowsGraphicsCapture,
                _ => MonitorCaptureMethod.Auto
            };

            pl.DisplaySource = MonitorCapture.FromMonitor(monitorIndex, ObsName(slot, "display"))
                .SetCaptureMethod(captureMethod);

            pl.DisplayItem = pl.MainScene.AddSource(pl.DisplaySource);

            Log.Information($"Display capture added for monitor {monitorIndex} using {Settings.Instance.DisplayCaptureMethod} method");
        }

        private static async Task FinalizeSessionFileAsync(Recording? rec)
        {
            if (rec == null || rec.FilePath == null)
                return;

            bool hasManualBookmarks = rec.Bookmarks.Any(b => b.Type == BookmarkType.Manual);
            if (Settings.Instance.DiscardSessionsWithoutBookmarks && !hasManualBookmarks)
            {
                Log.Information("Discarding session recording without manual bookmarks");
                try
                {
                    if (File.Exists(rec.FilePath))
                    {
                        File.Delete(rec.FilePath);
                        Log.Information($"Deleted video file: {rec.FilePath}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to delete discarded session file: {ex.Message}");
                }
                return;
            }

            await EnsureFileReady(rec.FilePath!);

            int? igdbId = !string.IsNullOrEmpty(rec.ExePath)
                ? GameUtils.GetIgdbIdFromExePath(rec.ExePath)
                : null;
            await ContentService.CreateMetadataFile(rec.FilePath!, Content.ContentType.Session, rec.Game, rec.Bookmarks, igdbId: igdbId, audioTrackNames: rec.AudioTrackNames);
            await ContentService.CreateThumbnail(rec.FilePath!, Content.ContentType.Session);
            string sessionFilePath = rec.FilePath!;
            _ = Task.Run(async () => await ContentService.CreateWaveformFile(sessionFilePath, Content.ContentType.Session));

            Log.Information("Recording details:");
            Log.Information($"Start Time: {rec.StartTime}");
            Log.Information($"End Time: {rec.EndTime}");
            Log.Information($"Duration: {rec.Duration}");
            Log.Information($"File Path: {rec.FilePath}");
        }

        private static async Task StopRecordingSlotCore(int slot)
        {
            if (slot < 0 || slot >= MaxSessionSlots)
                return;

            if (_isStoppingSlot[slot])
            {
                Log.Information("StopRecordingSlot({Slot}) called but slot is already stopping.", slot);
                return;
            }

            _isStoppingSlot[slot] = true;

            try
            {
                var pl = Pipeline(slot);
                var recording = AppState.Instance.GetRecording(slot);

                bool isReplayBufferMode = Settings.Instance.RecordingMode == RecordingMode.Buffer;
                bool isHybridMode = Settings.Instance.RecordingMode == RecordingMode.Hybrid;
                bool isSessionMode = Settings.Instance.RecordingMode == RecordingMode.Session;

                StopGameCaptureHookTimeoutTimer(slot);

                if (recording != null)
                    AppState.Instance.UpdateRecordingEndTime(DateTime.Now, slot);

                if ((isReplayBufferMode || isHybridMode) && pl.BufferOutput != null)
                {
                    Log.Information(isHybridMode ? "Hybrid: Stopping replay buffer (slot {Slot})..." : "Stopping replay buffer (slot {Slot})...", slot);
                    bool successfullyStopped = pl.BufferOutput.Stop(waitForCompletion: true, timeoutMs: 30000);

                    if (successfullyStopped)
                    {
                        Log.Information(isHybridMode ? "Hybrid: Replay buffer stopped (slot {Slot})." : "Replay buffer stopped (slot {Slot}).", slot);
                        Thread.Sleep(200);
                    }
                    else
                    {
                        Log.Warning(isHybridMode ? "Hybrid: Replay buffer did not stop within timeout (slot {Slot}). Forcing stop." : "Replay buffer did not stop within timeout (slot {Slot}). Forcing stop.", slot);
                        pl.BufferOutput.ForceStop();
                        Thread.Sleep(500);
                    }
                }

                if (pl.SessionOutput != null)
                {
                    Log.Information(isHybridMode ? "Hybrid: Stopping recording..." : "Stopping recording...");
                    bool successfullyStopped = pl.SessionOutput.Stop(waitForCompletion: true, timeoutMs: 30000);

                    if (successfullyStopped)
                    {
                        Log.Information(isHybridMode ? "Hybrid: Recording stopped." : "Recording stopped.");
                        Thread.Sleep(200);
                    }
                    else
                    {
                        Log.Warning(isHybridMode ? "Hybrid: Recording did not stop within timeout. Forcing stop." : "Recording did not stop within timeout. Forcing stop.");
                        pl.SessionOutput.ForceStop();
                        Thread.Sleep(500);
                    }
                }

                DisposeOutput(slot);
                DisposeSources(slot);
                DisposeEncoders(slot);

                if (slot == 0)
                    Obs.SetOutputSource(0, (Scene?)null);
                DisposeRecordingCanvas(slot);
                pl.HookedExecutableFileName = null;

                if (isSessionMode || isHybridMode)
                {
                    await FinalizeSessionFileAsync(recording);

                    // Load session metadata into AppState before highlight creation (CreateHighlight reads AppState.Content).
                    await SettingsService.LoadContentFromFolderIntoState(true);

                    if (Settings.Instance.EnableAi && Settings.Instance.AutoGenerateHighlights
                        && recording?.FilePath != null
                        && recording.Bookmarks.Any(b => b.Type.IncludeInHighlight()))
                    {
                        await AiService.CreateHighlight(Path.GetFileNameWithoutExtension(recording.FilePath));
                    }
                }

                AppState.Instance.ClearRecording(slot);
                ClearPendingPreRecordingForSlot(slot);

                _ = GameIntegrationService.Shutdown(slot);

                RecordingPreviewService.OnRecordingStopped(slot);
                ClearCapturedWindowDimensions(slot);

                if (!AppState.Instance.HasAnyRecording())
                {
                    GeneralUtils.SetProcessPriority(ProcessPriorityClass.Normal);
                    KeybindCaptureService.Stop();
                    NotifyIconService.SetNotifyIconStatus(NotifyIconState.Idle);
                    CapturedWindowWidth = null;
                    CapturedWindowHeight = null;
                    ClearAllCapturedWindowDimensions();
                    await VrChatVvmwIntegration.FlushDeferredClipsAsync();
                }
            }
            finally
            {
                _isStoppingSlot[slot] = false;
            }
        }

        /// <summary>
        /// Schedules an async slot stop. Uses a method parameter so the slot is captured by value,
        /// avoiding C# for-loop closure bugs where all lambdas see the final loop index.
        /// </summary>
        private static void ScheduleStopRecordingSlot(int slot)
        {
            _ = Task.Run(() => StopRecordingSlot(slot));
        }

        public static async Task StopRecordingSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSessionSlots)
                return;

            Log.Information("StopRecordingSlot({Slot}) requested.", slot);

            // Unblock StartRecording window-wait loops waiting on this pre-recording entry.
            ClearPendingPreRecordingForSlot(slot);

            await _slotLifecycleLocks[slot].WaitAsync();
            _slotLifecycleLocks[slot].Release();

            await _stopRecordingSemaphore.WaitAsync();
            try
            {
                if (!ShouldStopSlot(slot))
                {
                    Log.Information("StopRecordingSlot({Slot}) called but slot is not active.", slot);
                    return;
                }

                await StopRecordingSlotCore(slot);
                await StorageService.EnsureStorageBelowLimit();
            }
            finally
            {
                _stopRecordingSemaphore.Release();
            }
        }

        public static async Task StopRecording()
        {
            var slotsToStop = new List<int>();
            for (int i = 0; i < MaxSessionSlots; i++)
            {
                if (AppState.Instance.GetPreRecording(i) != null || ShouldStopSlot(i))
                    slotsToStop.Add(i);
            }

            if (slotsToStop.Count == 0)
            {
                Log.Information("StopRecording called but no active slots.");
                return;
            }

            foreach (int slot in slotsToStop)
                await StopRecordingSlot(slot);
        }

        /// <summary>
        /// Event handler for GameCapture.Hooked event.
        /// </summary>
        private static void OnGameCaptureHookedEvent(GameCapture capture, int slotIndex)
        {
            try
            {
                var pl = Pipeline(slotIndex);
                string? title = capture.HookedWindowTitle?.Trim();
                string? windowClass = capture.HookedWindowClass?.Trim();
                string? executable = capture.HookedExecutable?.Trim();

                Log.Information($"Game hooked: Title='{title}', Class='{windowClass}', Executable='{executable}'");

                StopGameCaptureHookTimeoutTimer(slotIndex);

                DisposeDisplaySource(slotIndex);

                var audioOutputMode = Settings.Instance.AudioOutputMode;
                if (audioOutputMode != AudioOutputMode.All)
                {
                    bool preserveDesktopForHybridTracks = Settings.Instance.EnableSeparateAudioTracks;

                    if (!preserveDesktopForHybridTracks)
                    {
                        foreach (var desktopSource in EnumerateDesktopSources())
                        {
                            try { desktopSource.IsMuted = true; }
                            catch (Exception ex) { Log.Warning($"Failed to mute desktop source: {ex.Message}"); }
                        }
                        Log.Information("Muted desktop audio sources (game hooked, using capture_audio)");
                    }
                    else
                    {
                        Log.Information("Keeping desktop audio sources active (separate audio tracks: do not mute output capture on hook).");
                    }

                    var discordSource = FindDiscordAudioSource();
                    if (audioOutputMode == AudioOutputMode.GameAndDiscord && discordSource != null)
                    {
                        try { discordSource.IsMuted = false; }
                        catch (Exception ex) { Log.Warning($"Failed to unmute Discord source: {ex.Message}"); }
                        Log.Information("Unmuted Discord audio source (game hooked)");
                    }
                }

                var recording = AppState.Instance.GetRecording(slotIndex);
                if (recording != null)
                {
                    recording.IsUsingGameHook = true;

                    if (!string.IsNullOrEmpty(executable))
                    {
                        pl.HookedExecutableFileName = Path.GetFileName(executable);
                        recording.FileName = pl.HookedExecutableFileName;

                        int? hookedPid = TryResolveHookedProcessId(executable, slotIndex);
                        if (hookedPid.HasValue)
                        {
                            recording.Pid = hookedPid;
                            Log.Information("Updated recording slot {Slot} tracked PID to {Pid} ({Exe})", slotIndex, hookedPid, pl.HookedExecutableFileName);
                        }
                    }

                    _ = SendSettingsToFrontend("Updated game hook");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing OnGameCaptureHookedEvent");
            }
        }


        /// <summary>
        /// Event handler for GameCapture.Unhooked event.
        /// </summary>
        private static void OnGameCaptureUnhookedEvent(GameCapture capture, int slotIndex)
        {
            _ = capture;
            Log.Information("Game unhooked.");

            var audioOutputMode = Settings.Instance.AudioOutputMode;
            if (audioOutputMode != AudioOutputMode.All)
            {
                // Only restore desktop/Discord fallback when no other slot still has a hooked game.
                bool anotherSlotStillHooked = false;
                for (int i = 0; i < MaxSessionSlots; i++)
                {
                    if (i == slotIndex) continue;
                    if (IsSlotCaptureHooked(i))
                    {
                        anotherSlotStillHooked = true;
                        break;
                    }
                }

                if (!anotherSlotStillHooked)
                {
                    foreach (var desktopSource in EnumerateDesktopSources())
                    {
                        try { desktopSource.IsMuted = false; }
                        catch (Exception ex) { Log.Warning($"Failed to unmute desktop source: {ex.Message}"); }
                    }
                    Log.Information("Unmuted desktop audio sources (game unhooked, falling back to desktop audio)");

                    var discordSource = FindDiscordAudioSource();
                    if (audioOutputMode == AudioOutputMode.GameAndDiscord && discordSource != null)
                    {
                        try { discordSource.IsMuted = true; }
                        catch (Exception ex) { Log.Warning($"Failed to mute Discord source: {ex.Message}"); }
                        Log.Information("Muted Discord audio source (game unhooked)");
                    }
                }
                else
                {
                    Log.Information(
                        "Keeping shared desktop/Discord mute state (another slot is still hooked)");
                }
            }

            var recording = AppState.Instance.GetRecording(slotIndex);
            if (recording != null)
                recording.IsUsingGameHook = false;
        }

        private static void OnReplaySaved(int slot)
        {
            _replaySavedBySlot[slot] = true;
            Log.Information("Replay buffer saved callback received (slot {Slot})", slot);
        }

        private static void SetForceMono(Source source, bool forceMono)
        {
            try
            {
                uint flags = source.Flags;
                bool currentlyMono = (flags & OBS_SOURCE_FLAG_FORCE_MONO) != 0;
                if (forceMono && !currentlyMono)
                {
                    source.Flags = flags | OBS_SOURCE_FLAG_FORCE_MONO;
                }
                else if (!forceMono && currentlyMono)
                {
                    source.Flags = flags & ~OBS_SOURCE_FLAG_FORCE_MONO;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to set force mono on source: {ex.Message}");
            }
        }

        private static void CreateRecordingScene(int slot, SessionPipeline pl, uint? canvasWidth = null, uint? canvasHeight = null)
        {
            if (slot == 0)
            {
                pl.MainScene = new Scene($"Recording Scene {slot}");
                Log.Information("Created recording scene on main canvas (slot {Slot})", slot);
                return;
            }

            var (width, height) = EnsureEvenDimensions(
                canvasWidth ?? _currentBaseWidth,
                canvasHeight ?? _currentBaseHeight);

            pl.RecordingCanvas = Canvas.Create(
                ObsName(slot, "Recording Canvas"),
                width,
                height);
            pl.MainScene = pl.RecordingCanvas.CreateScene($"Recording Scene {slot}");
            pl.RecordingCanvas.SetScene(pl.MainScene);
            Log.Information("Created recording scene on dedicated canvas (slot {Slot}, {Width}x{Height})", slot, width, height);
        }

        private static void DisposeRecordingCanvas(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.RecordingCanvas == null)
                return;

            try
            {
                pl.RecordingCanvas.Dispose();
                Log.Information("Recording canvas disposed (slot {Slot})", slot);
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to dispose recording canvas (slot {slot}): {ex.Message}");
            }

            pl.RecordingCanvas = null;
        }

        public static void DisposeSources(int slot)
        {
            var pl = Pipeline(slot);

            lock (_sharedAudioLock)
            {
                // If this slot owns shared WASAPI/Discord and another slot is still active,
                // transfer ownership so the remaining recording keeps mic/desktop/Discord.
                if (PipelineOwnsSharedAudio(pl) && IsAnotherSlotRecording(slot))
                {
                    int otherSlot = FindOtherActiveSlot(slot);
                    if (otherSlot >= 0)
                        TransferSharedAudioOwnership(pl, Pipeline(otherSlot));
                }
            }

            if (pl.MainScene != null)
            {
                try
                {
                    pl.MainScene.Dispose();
                    Log.Information("Scene disposed");
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose scene: {ex.Message}");
                }
                pl.MainScene = null;
            }

            pl.GameCaptureItem = null;
            pl.DisplayItem = null;

            DisposeDisplaySource(slot);
            DisposeGameCaptureSource(slot);

            lock (_sharedAudioLock)
            {
                // Only dispose shared audio when no other slot still needs it.
                if (PipelineOwnsSharedAudio(pl) && !IsAnotherSlotRecording(slot))
                    DisposeOwnedSharedAudioSources(pl);
                else if (PipelineOwnsSharedAudio(pl))
                {
                    // Safety: another slot became active between checks ??transfer instead of disposing.
                    int otherSlot = FindOtherActiveSlot(slot);
                    if (otherSlot >= 0)
                        TransferSharedAudioOwnership(pl, Pipeline(otherSlot));
                    else
                        DisposeOwnedSharedAudioSources(pl);
                }
            }

            DisposeRecordingCanvas(slot);
        }

        public static void DisposeGameCaptureSource(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.GameCaptureItem != null)
            {
                try
                {
                    pl.GameCaptureItem.Remove();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to remove game capture scene item: {ex.Message}");
                }
                pl.GameCaptureItem = null;
            }

            if (pl.GameCapture != null)
            {
                try
                {
                    if (pl.HookedSubscription != null)
                        pl.GameCapture.Hooked -= pl.HookedSubscription;
                    if (pl.UnhookedSubscription != null)
                        pl.GameCapture.Unhooked -= pl.UnhookedSubscription;
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to unsubscribe from game capture events: {ex.Message}");
                }

                pl.HookedSubscription = null;
                pl.UnhookedSubscription = null;

                try
                {
                    pl.GameCapture.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose game capture source: {ex.Message}");
                }
                pl.GameCapture = null;
            }

            StopGameCaptureHookTimeoutTimer(slot);
        }

        private static void StartGameCaptureHookTimeoutTimer(int slotIndex)
        {
            StopGameCaptureHookTimeoutTimer(slotIndex);

            var pl = Pipeline(slotIndex);
            pl.GameCaptureHookTimeoutTimer = new System.Threading.Timer(
                CheckGameCaptureHookStatus,
                slotIndex,
                90000,
                Timeout.Infinite
            );

            Log.Information("Started game capture hook timer (90 seconds) for slot {Slot}", slotIndex);
        }

        private static void StopGameCaptureHookTimeoutTimer(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.GameCaptureHookTimeoutTimer != null)
            {
                pl.GameCaptureHookTimeoutTimer.Dispose();
                pl.GameCaptureHookTimeoutTimer = null;
                Log.Information("Stopped game capture hook timer for slot {Slot}", slot);
            }
        }

        private static void CheckGameCaptureHookStatus(object? state)
        {
            if (state is not int slot)
                return;

            var pl = Pipeline(slot);
            bool hooked = pl.GameCapture?.IsHooked ?? false;
            if (!hooked)
            {
                Log.Warning("Game capture did not hook within 90 seconds for slot {Slot}. Removing game capture source.", slot);
                DisposeGameCaptureSource(slot);
            }
            else
            {
                Log.Information("Game capture hook check completed for slot {Slot}. Hook status: {Status}", slot, hooked ? "Hooked" : "Not hooked");
                StopGameCaptureHookTimeoutTimer(slot);
            }
        }

        public static void DisposeDisplaySource(int slot)
        {
            var pl = Pipeline(slot);
            if (pl.DisplayItem != null)
            {
                try
                {
                    Log.Information("Removing display scene item from scene");
                    pl.DisplayItem.Remove();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to remove display scene item: {ex.Message}");
                }
                pl.DisplayItem = null;
            }

            if (pl.DisplaySource != null)
            {
                try
                {
                    Log.Information("Disposing display source (expect OBS 'source destroyed' log to confirm WGC cleanup)");
                    pl.DisplaySource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose display source: {ex.Message}");
                }
                pl.DisplaySource = null;
            }
        }

        public static void DisposeEncoders(int slot)
        {
            var pl = Pipeline(slot);
            pl.VideoEncoder = null;
            pl.AudioEncoders.Clear();
        }

        public static void DisposeOutput(int slot)
        {
            var pl = Pipeline(slot);
            pl.ReplaySavedConnection?.Dispose();
            pl.ReplaySavedConnection = null;
            pl.BufferOutput = null;
            pl.SessionOutput = null;
        }

        public static async Task AvailableOBSVersionsAsync()
        {
            try
            {
                string url = "https://segra.tv/api/obs/versions";
                List<Core.Models.OBSVersion>? response = null;
                using (HttpClient client = new())
                {
                    try
                    {
                        response = await client.GetFromJsonAsync<List<Core.Models.OBSVersion>>(url);
                        if (response != null)
                        {
                            Log.Information($"Available OBS versions: {string.Join(", ", response.Select(v => v.Version))}");
                        }
                        else
                        {
                            Log.Warning("Received null OBS versions list from API");
                            response = new List<Core.Models.OBSVersion>();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error parsing OBS versions from API: {ex.Message}");
                        response = new List<Core.Models.OBSVersion>();
                    }
                }

                // Filter versions based on current Segra version compatibility
                if (response != null && response.Count > 0)
                {
                    // Get the current Segra version
                    NuGet.Versioning.SemanticVersion currentVersion;
                    if (UpdateService.UpdateManager.CurrentVersion != null)
                    {
                        currentVersion = NuGet.Versioning.SemanticVersion.Parse(UpdateService.UpdateManager.CurrentVersion.ToString());
                    }
                    else
                    {
                        // Running in local development, use a high version to ensure we get the latest stable version
                        currentVersion = NuGet.Versioning.SemanticVersion.Parse("9.9.9");
                        Log.Warning("Could not get current version from UpdateManager, using default version for OBS compatibility check");
                    }

                    // Filter to only compatible versions
                    List<Core.Models.OBSVersion> compatibleVersions = response.Where(v =>
                    {
                        // SupportsFrom: null or empty means no lower limit
                        bool supportsFrom = string.IsNullOrEmpty(v.SupportsFrom) ||
                                          (NuGet.Versioning.SemanticVersion.TryParse(v.SupportsFrom, out var minVersion) &&
                                           currentVersion >= minVersion);

                        // SupportsTo: null or empty means no upper limit
                        bool supportsTo = v.SupportsTo == null ||
                                        string.IsNullOrEmpty(v.SupportsTo) ||
                                        (NuGet.Versioning.SemanticVersion.TryParse(v.SupportsTo, out var maxVersion) &&
                                         currentVersion <= maxVersion);

                        return supportsFrom && supportsTo;
                    }).ToList();

                    Log.Information($"Compatible OBS versions for Segra {currentVersion}: {string.Join(", ", compatibleVersions.Select(v => v.Version))}");
                    response = compatibleVersions;
                }

                SettingsService.SetAvailableOBSVersions(response ?? new List<Core.Models.OBSVersion>());
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get available OBS versions: {ex.Message}");
            }
        }

        public static bool IsOBSInstalled()
        {
            string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "obs.dll");
            return File.Exists(dllPath);
        }

        public static async Task CheckIfExistsOrDownloadAsync(bool isUpdate = false)
        {
            Log.Information("Checking if OBS is installed");

            // Ensure we have the latest available versions
            await AvailableOBSVersionsAsync();

            if (isUpdate)
            {
                // We need to reinstall the Segra app to apply the update, because all OBS resources are placed in the app directory
                Settings.Instance.PendingOBSUpdate = true;
                SettingsService.SaveSettings();
                await UpdateService.ForceReinstallCurrentVersionAsync();
                await ShowModal("OBS Update", "Please restart Segra to apply the update.");
                return;
            }

            if (IsOBSInstalled() && !isUpdate && !Settings.Instance.PendingOBSUpdate)
            {
                Log.Information("OBS is installed");
                return;
            }

            string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // Store obs.zip and hash in AppData to preserve them across updates
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra");
            Directory.CreateDirectory(appDataDir); // Ensure directory exists

            string zipPath = Path.Combine(appDataDir, "obs.zip");
            string localHashPath = Path.Combine(appDataDir, "obs.hash");
            bool needsDownload = true;

            // Determine which version to download
            string? selectedVersion = Settings.Instance.SelectedOBSVersion;
            Core.Models.OBSVersion? versionToDownload = null;

            // If a specific version is selected, try to find it
            if (!string.IsNullOrEmpty(selectedVersion))
            {
                versionToDownload = AppState.Instance.AvailableOBSVersions
                    .FirstOrDefault(v => v.Version == selectedVersion);

                if (versionToDownload == null)
                {
                    Log.Warning($"Selected OBS version {selectedVersion} not found in available versions. Using latest stable version.");
                }
            }

            // If no specific version was selected or found, use the latest non-beta version
            if (versionToDownload == null)
            {
                versionToDownload = AppState.Instance.AvailableOBSVersions
                    .Where(v => !v.IsBeta)
                    .OrderByDescending(v => v.Version)
                    .FirstOrDefault();

                Log.Information($"Using latest stable OBS version: {versionToDownload?.Version}");
            }

            // Download the selected or latest version
            if (versionToDownload != null)
            {
                Log.Information($"Using OBS version: {versionToDownload.Version}");
                string metadataUrl = versionToDownload.Url; // This is the GitHub metadata URL

                using (var httpClient = new HttpClient())
                {
                    // First, fetch the metadata from GitHub
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Segra");
                    httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.json");

                    Log.Information($"Fetching metadata for OBS version {versionToDownload.Version} from {metadataUrl}");
                    var response = await httpClient.GetAsync(metadataUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error($"Failed to fetch metadata from {metadataUrl}. Status: {response.StatusCode}");
                        throw new Exception($"Failed to fetch file metadata: {response.ReasonPhrase}");
                    }

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var metadata = System.Text.Json.JsonSerializer.Deserialize<GitHubFileMetadata>(jsonResponse);

                    if (metadata?.DownloadUrl == null)
                    {
                        Log.Error("Download URL not found in the API response.");
                        throw new Exception("Invalid API response: Missing download URL.");
                    }

                    string remoteHash = metadata.Sha;
                    string actualDownloadUrl = metadata.DownloadUrl;

                    // Check if we already have the file with the correct hash
                    if (!isUpdate && File.Exists(zipPath) && File.Exists(localHashPath))
                    {
                        string localHash = await File.ReadAllTextAsync(localHashPath);
                        if (localHash == remoteHash)
                        {
                            Log.Information("Found existing obs.zip with matching hash. Skipping download.");
                            needsDownload = false;
                        }
                        else
                        {
                            Log.Information("Found existing obs.zip but hash doesn't match. Downloading new version.");
                            needsDownload = true;
                        }
                    }

                    // If this is an update or we need to download, proceed with download
                    if (needsDownload)
                    {
                        Log.Information($"Downloading OBS version {versionToDownload.Version}");

                        httpClient.DefaultRequestHeaders.Clear();

                        // Download with progress reporting
                        using var downloadResponse = await httpClient.GetAsync(actualDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        downloadResponse.EnsureSuccessStatusCode();

                        var totalBytes = downloadResponse.Content.Headers.ContentLength ?? -1L;
                        using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;
                        int lastReportedProgress = -1;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                int progress = (int)((totalBytesRead * 100) / totalBytes);
                                // Only send update if progress changed (avoid flooding)
                                if (progress != lastReportedProgress)
                                {
                                    lastReportedProgress = progress;
                                    await SendFrontendMessage("ObsDownloadProgress", new { progress, status = "downloading" });
                                }
                            }
                        }

                        // Save the hash for future reference
                        await File.WriteAllTextAsync(localHashPath, remoteHash);

                        Log.Information("Download complete");
                    }
                }

                // This should already be deleted on reinstall, but just in case
                if (Settings.Instance.PendingOBSUpdate)
                {
                    string dataPath = Path.Combine(currentDirectory, "data");
                    if (Directory.Exists(dataPath))
                    {
                        Directory.Delete(dataPath, true);
                    }

                    string obsPluginsPath = Path.Combine(currentDirectory, "obs-plugins");
                    if (Directory.Exists(obsPluginsPath))
                    {
                        Directory.Delete(obsPluginsPath, true);
                    }
                }

                try
                {
                    ZipFile.ExtractToDirectory(zipPath, currentDirectory, true);

                    if (Settings.Instance.PendingOBSUpdate)
                    {
                        await ShowModal("OBS Update", $"OBS update to {versionToDownload.Version} applied successfully.");
                        Settings.Instance.PendingOBSUpdate = false;
                        SettingsService.SaveSettings();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to extract OBS: {ex.Message}");
                    await ShowModal("OBS Update", "Failed to apply OBS update. Please try again.", "error");
                    throw;
                }

                Log.Information("OBS setup complete");
                return;
            }

            // If we somehow got here without a version to download, log an error
            Log.Error("No OBS versions available from API. This should not happen.");
        }

        private class GitHubFileMetadata
        {
            [System.Text.Json.Serialization.JsonPropertyName("sha")]
            public required string Sha { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("download_url")]
            public required string DownloadUrl { get; set; }
        }

        public static void PlaySound(string resourceName, int delay = 0)
        {
            Thread.Sleep(delay);
            using var stream = Properties.Resources.ResourceManager.GetStream(resourceName);
            if (stream == null)
                throw new ArgumentException($"Resource '{resourceName}' not found or not a stream.");

            using var reader = new WaveFileReader(stream);
            var sampleProvider = reader.ToSampleProvider();
            var volumeProvider = new VolumeSampleProvider(sampleProvider)
            {
                Volume = Settings.Instance.SoundEffectsVolume
            };

            using var waveOut = new WaveOutEvent { DesiredLatency = 50 };
            waveOut.Init(volumeProvider);
            waveOut.Play();

            while (waveOut.PlaybackState == PlaybackState.Playing)
                Thread.Sleep(10);
        }


        private static readonly Dictionary<string, string> EncoderFriendlyNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // ???? NVIDIA NVENC ????????????????????????????????????????????????????????????????????????
                ["jim_nvenc"] = "NVIDIA NVENC H.264",
                ["jim_hevc_nvenc"] = "NVIDIA NVENC H.265",
                ["jim_av1_nvenc"] = "NVIDIA NVENC AV1",

                // ???? AMD AMF ????????????????????????????????????????????????????????????????????????????????
                ["h264_texture_amf"] = "AMD AMF H.264",
                ["h265_texture_amf"] = "AMD AMF H.265",
                ["av1_texture_amf"] = "AMD AMF AV1",

                // ???? Intel Quick Sync ??????????????????????????????????????????????????????????????
                ["obs_qsv11_v2"] = "Intel QSV H.264",
                ["obs_qsv11_hevc"] = "Intel QSV H.265",
                ["obs_qsv11_av1"] = "Intel QSV AV1",

                // ???? CPU / software paths ??????????????????????????????????????????????????????
                ["obs_x264"] = "Software x264",
                ["ffmpeg_openh264"] = "Software OpenH264",
            };

        private static void SetAvailableEncodersInState()
        {
            Log.Information("Available encoders:");

            // Enumerate all encoder types using ObsKit.NET
            var encoderTypes = Obs.EnumerateEncoderTypes().ToList();
            int idx = 0;

            foreach (var encoderId in encoderTypes)
            {
                EncoderFriendlyNames.TryGetValue(encoderId, out var name);
                string friendlyName = name ?? encoderId;
                bool isHardware = encoderId.Contains("nvenc", StringComparison.OrdinalIgnoreCase) ||
                                  encoderId.Contains("amf", StringComparison.OrdinalIgnoreCase) ||
                                  encoderId.Contains("qsv", StringComparison.OrdinalIgnoreCase);

                Log.Information($"{idx} - {friendlyName} | {encoderId} | {(isHardware ? "Hardware" : "Software")}");
                if (name != null)
                {
                    AppState.Instance.Codecs.Add(new Codec { InternalEncoderId = encoderId, FriendlyName = friendlyName, IsHardwareEncoder = isHardware });
                }
                idx++;
            }

            Log.Information($"Total encoders found: {idx}");

            if (Settings.Instance.Codec == null)
            {
                Settings.Instance.Codec = SelectDefaultCodec(Settings.Instance.Encoder, AppState.Instance.Codecs);
            }
        }

        public static Codec? SelectDefaultCodec(string encoderType, List<Codec> availableCodecs)
        {
            if (availableCodecs == null || availableCodecs.Count == 0)
            {
                return null;
            }

            Codec? selectedCodec = null;

            if (encoderType == "cpu")
            {
                // Prefer obs_x264 if available
                selectedCodec = availableCodecs.FirstOrDefault(
                    c => c.InternalEncoderId.Equals(
                        "obs_x264",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                // If not found, fallback to first software (CPU) encoder
                if (selectedCodec == null)
                {
                    selectedCodec = availableCodecs.FirstOrDefault(
                        c => !c.IsHardwareEncoder
                    );
                }
            }
            else if (encoderType == "gpu")
            {
                // Prefer NVIDIA NVENC (jim_nvenc)
                selectedCodec = availableCodecs.FirstOrDefault(
                    c => c.InternalEncoderId.Equals(
                        "jim_nvenc",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                // If not found, try AMD AMF H.264
                if (selectedCodec == null)
                {
                    selectedCodec = availableCodecs.FirstOrDefault(
                        c => c.InternalEncoderId.Equals(
                            "h264_texture_amf",
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                }

                // If still not found, fallback to first hardware encoder
                if (selectedCodec == null)
                {
                    selectedCodec = availableCodecs.FirstOrDefault(
                        c => c.IsHardwareEncoder
                    );
                }
            }

            // Ultimate fallback: First available encoder if no match or no selection
            if (selectedCodec == null)
            {
                selectedCodec = availableCodecs.FirstOrDefault();
            }

            return selectedCodec;
        }

        public static bool SupportsHybridMp4()
        {
            string? versionToCheck = Settings.Instance.SelectedOBSVersion ?? InstalledOBSVersion;

            if (string.IsNullOrEmpty(versionToCheck))
                return true;

            string cleanVersion = versionToCheck.Split('-')[0].Trim();
            if (Version.TryParse(cleanVersion, out Version? version))
                return version >= new Version(30, 2);

            return true;
        }

        /// <summary>
        /// Returns a downscaled JPEG of the current capture (game capture when hooked, otherwise display capture).
        /// </summary>
        public static byte[]? TryGetRecordingPreviewJpeg(int maxEdgePixels = 220)
        {
            for (int slot = 0; slot < MaxSessionSlots; slot++)
            {
                var jpeg = TryGetRecordingPreviewJpeg(slot, maxEdgePixels);
                if (jpeg != null)
                    return jpeg;
            }

            return null;
        }

        public static byte[]? TryGetRecordingPreviewJpeg(int slot, int maxEdgePixels = 220)
        {
            if (!IsInitialized || slot < 0 || slot >= MaxSessionSlots)
                return null;

            var pl = Pipeline(slot);
            if (pl.MainScene == null)
                return null;

            try
            {
                var gcap = pl.GameCapture;
                if (gcap is { IsHooked: true })
                {
                    uint sw = gcap.Width;
                    uint sh = gcap.Height;
                    if (sw > 0 && sh > 0)
                    {
                        var shot = gcap.TakeScreenshot(0, 0, sw, sh);
                        if (shot != null && shot.Pixels.Length > 0)
                            return EncodeBgraScreenshotToJpeg(shot.Pixels, shot.Width, shot.Height, maxEdgePixels);
                    }
                }

                var disp = pl.DisplaySource;
                if (disp != null)
                {
                    uint sw = disp.Width;
                    uint sh = disp.Height;
                    if (sw > 0 && sh > 0)
                    {
                        var shot = disp.TakeScreenshot(0, 0, sw, sh);
                        if (shot != null && shot.Pixels.Length > 0)
                            return EncodeBgraScreenshotToJpeg(shot.Pixels, shot.Width, shot.Height, maxEdgePixels);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TryGetRecordingPreviewJpeg failed for slot {Slot}", slot);
            }

            return null;
        }

        private static byte[] EncodeBgraScreenshotToJpeg(byte[] pixels, uint width, uint height, int maxEdgePixels)
        {
            int w = (int)width;
            int h = (int)height;

            using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bmpData = bitmap.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                int srcStride = w * 4;
                int dstStride = bmpData.Stride;
                for (int y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        pixels,
                        y * srcStride,
                        bmpData.Scan0 + y * dstStride,
                        srcStride);
                }
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            int maxDim = Math.Max(w, h);
            if (maxDim > maxEdgePixels)
            {
                double scale = (double)maxEdgePixels / maxDim;
                int nw = Math.Max(1, (int)Math.Round(w * scale));
                int nh = Math.Max(1, (int)Math.Round(h * scale));

                using var scaled = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(bitmap, 0, 0, nw, nh);
                }

                return BitmapToJpegBytes(scaled);
            }

            return BitmapToJpegBytes(bitmap);
        }

        private static byte[] BitmapToJpegBytes(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            var jpegCodec = ImageCodecInfo.GetImageEncoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, 72L);
            bmp.Save(ms, jpegCodec, encParams);
            return ms.ToArray();
        }

    }
}
