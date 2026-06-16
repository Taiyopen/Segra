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
using Segra.Backend.Utils;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using static Segra.Backend.Utils.GeneralUtils;
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
        public static string? InstalledOBSVersion { get; private set; } = null;

        // OBS context
        private static ObsContext? _obsContext;

        // OBS output resources (replay buffer uses the same pipeline as session recording)
        private static ReplayBuffer? _bufferOutput;

        /// <summary>Holds scene, sources, encoders and outputs for the single active session recording (dual-slot SessionSlots removed).</summary>
        private sealed class SessionPipeline
        {
            public Scene? MainScene;
            public SceneItem? GameCaptureItem;
            public SceneItem? DisplayItem;
            public RecordingOutput? SessionOutput;
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
        }

        private static readonly SessionPipeline _pipeline = new();

        private static Scene? _mainScene
        {
            get => _pipeline.MainScene;
            set => _pipeline.MainScene = value;
        }
        private static SceneItem? _gameCaptureItem
        {
            get => _pipeline.GameCaptureItem;
            set => _pipeline.GameCaptureItem = value;
        }
        private static SceneItem? _displayItem
        {
            get => _pipeline.DisplayItem;
            set => _pipeline.DisplayItem = value;
        }
        private static RecordingOutput? _output
        {
            get => _pipeline.SessionOutput;
            set => _pipeline.SessionOutput = value;
        }
        private static MonitorCapture? _displaySource
        {
            get => _pipeline.DisplaySource;
            set => _pipeline.DisplaySource = value;
        }
        private static List<AudioInputCapture> _micSources => _pipeline.MicSources;
        private static List<AudioOutputCapture> _desktopSources => _pipeline.DesktopSources;
        private static Source? _discordAudioSource
        {
            get => _pipeline.DiscordAudioSource;
            set => _pipeline.DiscordAudioSource = value;
        }
        private static VideoEncoder? _videoEncoder
        {
            get => _pipeline.VideoEncoder;
            set => _pipeline.VideoEncoder = value;
        }
        private static List<AudioEncoder> _audioEncoders => _pipeline.AudioEncoders;

        private static string? _hookedExecutableFileName
        {
            get => _pipeline.HookedExecutableFileName;
            set => _pipeline.HookedExecutableFileName = value;
        }
        private static System.Threading.Timer? _gameCaptureHookTimeoutTimer
        {
            get => _pipeline.GameCaptureHookTimeoutTimer;
            set => _pipeline.GameCaptureHookTimeoutTimer = value;
        }

        /// <summary>Current game capture (used by OCR, game detection, audio routing).</summary>
        public static GameCapture? GameCaptureSource
        {
            get => _pipeline.GameCapture;
            private set => _pipeline.GameCapture = value;
        }
        /// <summary>Used by OBS log processing to avoid false-positive stop when capture blips.</summary>
        private static bool _isStillHookedAfterUnhook = false;

        // Recording/output state
        private static bool _isStoppingOrStopped = false;
        private static uint _currentBaseWidth;
        private static uint _currentBaseHeight;

        // Replay buffer state
        private static bool _replaySaved = false;

        // Signal connection for replay buffer saved event
        private static SignalConnection? _replaySavedConnection;

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

        public static async Task<bool> SaveReplayBuffer()
        {
            if (_bufferOutput == null || !_bufferOutput.IsActive)
            {
                Log.Warning("Cannot save replay buffer: buffer is not active");
                return false;
            }

            await ReplayBufferSaveLock.WaitAsync();
            try
            {
                string? savedPath = await SaveReplayBufferInternalGetPathAsync();
                if (string.IsNullOrEmpty(savedPath))
                    return false;

                Log.Information($"Replay buffer saved to: {savedPath}");
                string game = Settings.Instance.State.Recording?.Game ?? "Unknown";
                string? exePath = Settings.Instance.State.Recording?.ExePath;
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
                    await ResetReplayBuffer();
                    _replaySaved = false;
                    return false;
                }

                if (savedDuration.TotalSeconds < 0.5)
                {
                    Log.Warning(
                        "Replay buffer save too short ({Seconds:0.###}s), discarding duplicate/empty save",
                        savedDuration.TotalSeconds);
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer();
                    _replaySaved = false;
                    return false;
                }

                await ContentService.CreateMetadataFile(savedPath, Content.ContentType.Buffer, game, igdbId: igdbId, audioTrackNames: Settings.Instance.State.Recording?.AudioTrackNames);
                await ContentService.CreateThumbnail(savedPath, Content.ContentType.Buffer);
                _ = Task.Run(async () => await ContentService.CreateWaveformFile(savedPath, Content.ContentType.Buffer));

                await SettingsService.LoadContentFromFolderIntoState(true);

                Log.Information("Replay buffer save process completed successfully");

                await ResetReplayBuffer();

                _replaySaved = false;

                return true;
            }
            finally
            {
                ReplayBufferSaveLock.Release();
            }
        }

        /// <summary>
        /// VRChat VVMW: triggers a replay buffer save, then keeps only the last <paramref name="tailDurationSeconds"/>
        /// (PlaybackEnded − PlaybackStart wall time) into Clips via FFmpeg <c>-sseof</c>. Does not register a full Replay Buffer item.
        /// Requires Hybrid or Replay Buffer mode with an active buffer. Set replay buffer duration ≥ longest expected playback.
        /// </summary>
        public static async Task<bool> TrySaveReplayBufferTailAsClipAsync(
            double tailDurationSeconds,
            string clipTitle,
            string preferredOutputFileNameBase,
            int clipIgdbId)
        {
            if (_bufferOutput == null || !_bufferOutput.IsActive)
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
                string? savedPath = await SaveReplayBufferInternalGetPathAsync();
                if (string.IsNullOrEmpty(savedPath))
                    return false;

                await EnsureFileReady(savedPath);

                var recording = Settings.Instance.State.Recording;
                if (recording == null)
                {
                    Log.Warning("[VRChat VVMW] No active recording state; discarding replay save");
                    TryDeleteReplayTempFile(savedPath);
                    await ResetReplayBuffer();
                    _replaySaved = false;
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
                    await ResetReplayBuffer();
                    _replaySaved = false;
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
                    await ResetReplayBuffer();
                    _replaySaved = false;
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
                    await ResetReplayBuffer();
                    _replaySaved = false;
                    return false;
                }

                TryDeleteReplayTempFile(savedPath);

                if (!File.Exists(tempOut))
                {
                    await ResetReplayBuffer();
                    _replaySaved = false;
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
                    await ResetReplayBuffer();
                    _replaySaved = false;
                    return false;
                }

                await EnsureFileReady(outPath);

                await ContentService.CreateMetadataFile(outPath, Content.ContentType.Clip, game, null, clipTitle, igdbId: clipIgdbId, audioTrackNames: recording.AudioTrackNames);
                await ContentService.CreateThumbnail(outPath, Content.ContentType.Clip);
                await ContentService.CreateWaveformFile(outPath, Content.ContentType.Clip);
                await SettingsService.LoadContentFromFolderIntoState(true);

                Log.Information("[VRChat VVMW] Saved replay tail clip: {Path}", outPath);

                await ResetReplayBuffer();
                _replaySaved = false;
                return true;
            }
            finally
            {
                ReplayBufferSaveLock.Release();
            }
        }

        /// <summary>
        /// Uses <c>{baseName}.mp4</c>, or <c>{baseName}_2.mp4</c>, <c>{baseName}_3.mp4</c>, … when the base name is already taken (same pattern intent as manual clips avoiding collisions).
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
        private static async Task<string?> SaveReplayBufferInternalGetPathAsync()
        {
            Log.Information("Attempting to save replay buffer...");
            _replaySaved = false;

            try
            {
                _bufferOutput!.Save();
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to save replay buffer: {ex.Message}");
                return null;
            }

            Log.Information("Waiting for replay buffer saved callback...");
            int attempts = 0;
            while (!_replaySaved && attempts < 50)
            {
                await Task.Delay(100);
                attempts++;
            }

            if (!_replaySaved)
            {
                Log.Warning("Replay buffer may not have saved correctly");
                return null;
            }

            string? savedPath = _bufferOutput.GetLastReplayPath();

            for (int i = 0; i < 10 && string.IsNullOrEmpty(savedPath); i++)
            {
                savedPath = _bufferOutput.GetLastReplayPath();
                if (string.IsNullOrEmpty(savedPath))
                    await Task.Delay(100);
            }

            if (string.IsNullOrEmpty(savedPath))
            {
                Log.Error("Replay buffer path is null or empty");
                return null;
            }

            return savedPath;
        }

        /// <summary>
        /// Stops and restarts the replay buffer so that subsequent saves
        /// only contain footage recorded after the last save.
        /// </summary>
        private static async Task ResetReplayBuffer()
        {
            if (_bufferOutput == null)
                return;

            Log.Information("Resetting replay buffer...");

            bool stopped = _bufferOutput.Stop(waitForCompletion: true, timeoutMs: 30000);

            if (!stopped)
            {
                Log.Warning("Replay buffer did not stop within timeout for reset. Forcing stop.");
                _bufferOutput.ForceStop();
                await Task.Delay(500);
            }

            bool started = _bufferOutput.Start();

            if (!started)
            {
                string error = _bufferOutput.LastError ?? "Unknown error";
                Log.Error($"Failed to restart replay buffer after reset: {error}");
            }
            else
            {
                Log.Information("Replay buffer restarted successfully");
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
                        // Some games will show the "capture window no longer exists" message when they are still running, so we wait a second to make sure it's not a false positive
                        Log.Information("Capture window no longer exists, waiting a second to make sure it's not a false positive.");
                        await Task.Delay(1000);
                        Log.Information("Checking if hook is still active: {_isStillHookedAfterUnhook}", _isStillHookedAfterUnhook);

                        // Check if any output is still active
                        if ((AnySessionOutputActive() || _bufferOutput != null) && !_isStillHookedAfterUnhook)
                        {
                            Log.Information("Capture stopped. Stopping recording.");
                            _ = Task.Run(StopRecording);
                        }
                        _isStillHookedAfterUnhook = false;
                    }

                    // This means the game is still running after unhooking. We need this to prevent the method above to accidentally stop the recording.
                    if (formattedMessage.Contains("existing hook found"))
                    {
                        _isStillHookedAfterUnhook = true;
                    }

                    // Parse window dimensions from OBS game capture logs
                    if (formattedMessage.Contains("BufferDesc.Width:"))
                    {
                        var match = BufferDescWidthRegex().Match(formattedMessage);
                        if (match.Success && uint.TryParse(match.Groups[1].Value, out uint width))
                        {
                            CapturedWindowWidth = width;
                            Log.Information($"Captured window width: {width}");
                        }
                    }

                    if (formattedMessage.Contains("BufferDesc.Height:"))
                    {
                        var match = BufferDescHeightRegex().Match(formattedMessage);
                        if (match.Success && uint.TryParse(match.Groups[1].Value, out uint height))
                        {
                            CapturedWindowHeight = height;
                            Log.Information($"Captured window height: {height}");
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

        private static bool AnySessionOutputActive() => _pipeline.SessionOutput != null;

        private static int AllocateSessionSlotIndex(bool startManually)
        {
            bool wantsSessionFile = Settings.Instance.RecordingMode == RecordingMode.Session
                || Settings.Instance.RecordingMode == RecordingMode.Hybrid;
            if (wantsSessionFile && Settings.Instance.State.Recording != null)
                return -1;
            return 0;
        }

        private static bool IsSlotCaptureHooked(int slot) => _pipeline.GameCapture?.IsHooked ?? false;

        private static void ClearPendingPreRecordingForSlot(int slot)
        {
            if (slot == 0)
                Settings.Instance.State.PreRecording = null;
        }

        private static void ClearAllPendingPreRecordings()
        {
            Settings.Instance.State.PreRecording = null;
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
                Settings.Instance.State.HasLoadedObs = true;
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
                Settings.Instance.State.HasLoadedObs = true;
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
                Settings.Instance.State.HasLoadedObs = true;
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
                // Example: 1279 → 1280 instead of OBS rounding down to 1276
                outputWidth = (uint)(Math.Round(outputWidth / 4.0) * 4);
                outputHeight = (uint)(Math.Round(outputHeight / 4.0) * 4);

                Log.Information($"Downscaling from {baseWidth}x{baseHeight} to {outputWidth}x{outputHeight} (max height: {maxHeight})");
            }

            _currentBaseWidth = baseWidth;
            _currentBaseHeight = baseHeight;

            Obs.SetVideo(v => v
                .BaseResolution(baseWidth, baseHeight)
                .OutputResolution(outputWidth, outputHeight)
                .Fps(customFps ?? 60));
        }

        public static bool StartRecording(string name = "Manual Recording", string exePath = "Unknown", bool startManually = false, int? pid = null)
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

            int slot = AllocateSessionSlotIndex(startManually);
            if (slot < 0)
            {
                Log.Information("Cannot start recording: no free output slot or invalid mode.");
                ClearAllPendingPreRecordings();
                return false;
            }

            // Reset the stopping flag when starting a new recording
            _isStoppingOrStopped = false;

            // Configure video settings specifically for this recording/buffer
            ResetVideoSettings(out _, customFps: (uint)Settings.Instance.FrameRate);

            // Create main scene for this recording
            _mainScene = new Scene($"Recording Scene {slot}");
            Log.Information($"Created recording scene (slot {slot})");

            // For manual recording, use display capture directly without game hooking
            if (startManually)
            {
                Log.Information("Manual recording started - using display capture");
                AddMonitorCapture();
                // Use base dimensions for bounds - scene canvas is at base resolution
                _displayItem?.SetBounds(ObsBoundsType.ScaleInner, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
            }
            else
            {
                // Add display capture first (bottom layer - fallback)
                AddMonitorCapture();

                // Create game capture source for automatic game detection
                try
                {
                    GameCaptureSource = new GameCapture($"gameplay_{slot}", GameCapture.CaptureMode.SpecificWindow);
                    GameCaptureSource.SetWindow($"*:*:{fileName}");

                    // Enable capture_audio on game capture when using GameOnly or GameAndDiscord mode
                    if (Settings.Instance.AudioOutputMode != AudioOutputMode.All)
                    {
                        GameCaptureSource.Update(s => s.Set("capture_audio", true));
                        Log.Information($"Game capture audio enabled (mode: {Settings.Instance.AudioOutputMode})");
                    }

                    Log.Information($"Game capture configured for: {fileName}");

                    // Add game capture to scene (top layer - visible when hooked)
                    _gameCaptureItem = _mainScene.AddSource(GameCaptureSource);

                    // Start a timer to check if game capture hooks within 90 seconds
                    StartGameCaptureHookTimeoutTimer(slot);

                    // Subscribe to GameCapture's hooked/unhooked events (IsHooked is tracked automatically)
                    _pipeline.HookedSubscription = c => OnGameCaptureHookedEvent(c, slot);
                    _pipeline.UnhookedSubscription = c => OnGameCaptureUnhookedEvent(c, slot);
                    GameCaptureSource!.Hooked += _pipeline.HookedSubscription;
                    GameCaptureSource.Unhooked += _pipeline.UnhookedSubscription;
                }
                catch (Exception ex)
                {
                    Log.Warning($"Game Capture source not available: {ex.Message}. Using Display Capture only.");
                    GameCaptureSource = null;
                }

                // Try to get the window dimensions for the game
                if (WindowUtils.GetWindowDimensionsByPreRecordingExeOrPid(out uint windowWidth, out uint windowHeight))
                {
                    ResetVideoSettings(
                        out bool is4by3,
                        customFps: (uint)Settings.Instance.FrameRate,
                        customOutputWidth: windowWidth,
                        customOutputHeight: windowHeight
                    );

                    // Scene item bounds must use BASE dimensions (not output) because the scene canvas is at base resolution.
                    // For 4:3 content: base is 4:3, output is 16:9 - OBS handles the stretch at the output level.
                    // For non-4:3: base == output, ScaleInner ensures content scales with black bars if window shrinks.
                    var boundsType = is4by3 ? ObsBoundsType.Stretch : ObsBoundsType.ScaleInner;
                    _gameCaptureItem?.SetBounds(boundsType, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
                    _displayItem?.SetBounds(boundsType, _currentBaseWidth, _currentBaseHeight).SetPosition(0, 0);
                }
                else
                {
                    _ = Task.Run(StopRecording);
                    return false;
                }
            }

            // Set scene as program output (channel 0 = main mix, 1 = first aux mix for dual session)
            Obs.SetOutputSource((uint)slot, _mainScene);

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
                Settings.Instance.State.CudaComputeCapability != null &&
                Settings.Instance.State.CudaComputeCapability < 7.0)
            {
                videoEncoderSettings.Set("bf", 0);
                Log.Information("NVENC b-frames disabled (CUDA compute capability < 7.0)");
            }

            _videoEncoder = new VideoEncoder(encoderId, "Segra Recorder", videoEncoderSettings);

            // Create audio sources and add to scene
            if (Settings.Instance.InputDevices != null && Settings.Instance.InputDevices.Count > 0)
            {
                foreach (var deviceSetting in Settings.Instance.InputDevices)
                {
                    if (!string.IsNullOrEmpty(deviceSetting.Id))
                    {
                        string sourceName = $"Microphone_{_micSources.Count + 1}";
                        var micSource = deviceSetting.Id == "default"
                            ? AudioInputCapture.FromDefault(sourceName)
                            : AudioInputCapture.FromDevice(deviceSetting.Id, sourceName);

                        // Apply Force Mono if enabled
                        SetForceMono(micSource, Settings.Instance.ForceMonoInputSources);

                        micSource.Volume = deviceSetting.Volume;

                        _mainScene!.AddSource(micSource);
                        _micSources.Add(micSource);

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

            var audioOutputMode = Settings.Instance.AudioOutputMode;

            // Always add desktop audio sources - they serve as fallback until game hooks in GameOnly/GameAndDiscord modes
            if (Settings.Instance.OutputDevices != null && Settings.Instance.OutputDevices.Count > 0)
            {
                foreach (var deviceSetting in Settings.Instance.OutputDevices)
                {
                    if (!string.IsNullOrEmpty(deviceSetting.Id))
                    {
                        string sourceName = $"DesktopAudio_{_desktopSources.Count + 1}";
                        var desktopSource = deviceSetting.Id == "default"
                            ? AudioOutputCapture.FromDefault(sourceName)
                            : AudioOutputCapture.FromDevice(deviceSetting.Id, sourceName);

                        desktopSource.Volume = deviceSetting.Volume;

                        _mainScene!.AddSource(desktopSource);
                        _desktopSources.Add(desktopSource);

                        Log.Information($"Added output device: {deviceSetting.Name} ({deviceSetting.Id}) as {sourceName} with volume {deviceSetting.Volume}");
                    }
                }
            }

            // In GameAndDiscord mode, also create Discord application audio capture (starts muted until game hooks)
            if (audioOutputMode == AudioOutputMode.GameAndDiscord && GameCaptureSource != null)
            {
                try
                {
                    _discordAudioSource = new Source("wasapi_process_output_capture", "Discord Audio");
                    _discordAudioSource.Update(s =>
                    {
                        // Window format: title:class:executable
                        s.Set("window", "Discord:Chrome_WidgetWin_1:Discord.exe");
                        s.Set("priority", 2); // WINDOW_PRIORITY_EXE
                    });
                    _discordAudioSource.IsMuted = true; // Muted until game hooks (desktop audio covers Discord until then)
                    _mainScene!.AddSource(_discordAudioSource);
                    Log.Information("Added Discord application audio capture source (muted until game hooks)");
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to create Discord audio capture source: {ex.Message}");
                    _discordAudioSource = null;
                }
            }

            // Configure mixers and audio encoders.
            // Single-track mode: all sources → Track 1.
            // Multi-track mode: each source uses its configured OBS mixer bitmask (tracks 1–6).
            const int maxTracks = 6;
            bool separateTracks = Settings.Instance.EnableSeparateAudioTracks;
            _audioEncoders.Clear();
            var actualAudioTrackNames = new List<string>();
            uint recordingTracksMask;
            int trackCount;

            if (!separateTracks)
            {
                const uint singleTrackMask = 1u;
                ApplyAudioTrackMask(_micSources, singleTrackMask, "microphone");
                ApplyAudioTrackMask(_desktopSources, singleTrackMask, "desktop");
                if (GameCaptureSource != null)
                    ApplyAudioTrackMask(GameCaptureSource, singleTrackMask, "game");
                if (_discordAudioSource != null)
                    ApplyAudioTrackMask(_discordAudioSource, singleTrackMask, "discord");

                trackCount = 1;
                recordingTracksMask = 1;
                actualAudioTrackNames.Add("Full Mix");

                int recordingAudioKbps = GetRecordingAudioBitrateKbps();
                _audioEncoders.Add(AudioEncoder.CreateAac("Full Mix", recordingAudioKbps, 0));
            }
            else
            {
                var inputDevices = Settings.Instance.InputDevices ?? new List<DeviceSetting>();
                for (int i = 0; i < _micSources.Count; i++)
                {
                    uint mask = i < inputDevices.Count
                        ? SanitizeAudioTrackMask(inputDevices[i].AudioTrackMask)
                        : 1u;
                    ApplyAudioTrackMask(_micSources[i], mask, $"microphone {i + 1}");
                }

                var outputDevices = Settings.Instance.OutputDevices ?? new List<DeviceSetting>();
                for (int i = 0; i < _desktopSources.Count; i++)
                {
                    uint mask = i < outputDevices.Count
                        ? SanitizeAudioTrackMask(outputDevices[i].AudioTrackMask)
                        : 1u;
                    ApplyAudioTrackMask(_desktopSources[i], mask, $"desktop {i + 1}");
                }

                if (GameCaptureSource != null)
                {
                    ApplyAudioTrackMask(
                        GameCaptureSource,
                        SanitizeAudioTrackMask(Settings.Instance.GameAudioTrackMask),
                        "game");
                }

                if (_discordAudioSource != null)
                {
                    ApplyAudioTrackMask(
                        _discordAudioSource,
                        SanitizeAudioTrackMask(Settings.Instance.DiscordAudioTrackMask),
                        "discord");
                }

                recordingTracksMask = 0;
                foreach (var device in inputDevices.Where(d => !string.IsNullOrEmpty(d.Id)))
                    recordingTracksMask |= SanitizeAudioTrackMask(device.AudioTrackMask);
                foreach (var device in outputDevices.Where(d => !string.IsNullOrEmpty(d.Id)))
                    recordingTracksMask |= SanitizeAudioTrackMask(device.AudioTrackMask);
                if (GameCaptureSource != null)
                    recordingTracksMask |= SanitizeAudioTrackMask(Settings.Instance.GameAudioTrackMask);
                if (_discordAudioSource != null)
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
                    _audioEncoders.Add(AudioEncoder.CreateAac(trackName, recordingAudioKbps, t));
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

                _bufferOutput = new ReplayBuffer("replay_buffer_output", Settings.Instance.ReplayBufferDuration, Settings.Instance.ReplayBufferMaxSize);
                _bufferOutput.SetDirectory(bufferDir);
                _bufferOutput.SetFilenameFormat("%CCYY-%MM-%DD_%hh-%mm-%ss");
                _bufferOutput.Update(s => s.Set("extension", "mp4").Set("tracks", (long)bufferTracksMask));

                // Set encoders
                _bufferOutput.WithVideoEncoder(_videoEncoder);
                for (int t = 0; t < _audioEncoders.Count; t++)
                {
                    _bufferOutput.WithAudioEncoder(_audioEncoders[t], track: t);
                }

                // Connect signal handler for replay saved
                _replaySavedConnection = _bufferOutput!.ConnectSignal(OutputSignal.Saved, OnReplaySaved);
            }

            if (isSessionMode || isHybridMode)
            {
                videoOutputPath = $"{sessionDir}/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";

                uint recordTracksMask = recordingTracksMask;

                bool useHybridMp4 = SupportsHybridMp4();
                Log.Information($"Using recording output type: {(useHybridMp4 ? "mp4_output" : "ffmpeg_muxer")} (Hybrid MP4: {useHybridMp4})");

                if (useHybridMp4)
                {
                    _output = new RecordingOutput($"simple_output_{slot}", videoOutputPath);
                    _output.SetFormat(RecordingFormat.HybridMp4);
                }
                else
                {
                    _output = new RecordingOutput($"simple_output_{slot}", videoOutputPath, "mp4");
                }
                _output.Update(s => s.Set("tracks", (long)recordTracksMask));

                // Set encoders
                _output.WithVideoEncoder(_videoEncoder);
                for (int t = 0; t < _audioEncoders.Count; t++)
                {
                    _output.WithAudioEncoder(_audioEncoders[t], track: t);
                }
            }

            // Overwrite the file name with the hooked executable name if using game hook
            fileName = _hookedExecutableFileName ?? fileName;

            DateTime? startTime = null;
            bool hasPlayedStartSound = false;

            if (_output != null)
            {
                if (!_output.Start())
                {
                    string error = _output.LastError ?? "Unknown error";
                    Log.Error($"Failed to start recording: {error}");
                    Task.Run(() => ShowModal("Recording failed", "Failed to start recording. Check the log for more details.", "error"));
                    Task.Run(() => PlaySound("error", 500));
                    ClearAllPendingPreRecordings();
                    _ = Task.Run(StopRecording);
                    return false;
                }

                // Set the exact start time for session recording (Full Session has bookmarks)
                startTime = DateTime.Now;
                _ = Task.Run(() => PlaySound("start"));
                hasPlayedStartSound = true;

                Log.Information("Session recording started successfully");
            }

            if (_bufferOutput != null)
            {
                if (!_bufferOutput.Start())
                {
                    string error = _bufferOutput.LastError ?? "Unknown error";
                    Log.Error($"Failed to start replay buffer: {error}");
                    Task.Run(() => ShowModal("Replay buffer failed", "Failed to start replay buffer. Check the log for more details.", "error"));
                    Task.Run(() => PlaySound("error", 500));
                    ClearAllPendingPreRecordings();
                    _ = Task.Run(StopRecording);
                    return false;
                }

                if (!hasPlayedStartSound)
                {
                    _ = Task.Run(() => PlaySound("start"));
                    hasPlayedStartSound = true;
                }

                Log.Information("Replay buffer started successfully");
            }

            string? gameImage = GameIconUtils.ExtractIconAsBase64(exePath);

            var newRecording = new Recording()
            {
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
                Slot = slot
            };
            Settings.Instance.State.Recording = newRecording;

            ClearPendingPreRecordingForSlot(slot);
            Program.ShowMonitoringWindowIfClosed();
            uint previewFps = (uint)Math.Max(1, Settings.Instance.FrameRate);
            _ = Task.Run(async () =>
            {
                await MessageService.SendSettingsToFrontend("OBS Start recording");
                if (_isStoppingOrStopped || Settings.Instance.State.Recording == null)
                    return;
                RecordingPreviewService.OnRecordingStarted(previewFps);
            });

            NotifyIconService.SetNotifyIconStatus(NotifyIconState.Recording);

            Log.Information("Recording started: " + videoOutputPath);
            GeneralUtils.SetProcessPriority(ProcessPriorityClass.High);
            if (!isReplayBufferMode)
            {
                _ = GameIntegrationService.Start(GameUtils.GetIgdbIdFromExePath(exePath), exePath);
            }
            else if (Settings.Instance.GameIntegrations.VrChat.Enabled &&
                     !string.IsNullOrEmpty(exePath) &&
                     exePath.EndsWith("VRChat.exe", StringComparison.OrdinalIgnoreCase))
            {
                // Replay Buffer mode: no session file — VRChat VVMW clips use replay buffer tail instead.
                _ = GameIntegrationService.Start(GameUtils.GetIgdbIdFromExePath(exePath), exePath);
            }
            Task.Run(KeybindCaptureService.Start);
            return true;
        }

        /// <summary>Re-creates monitor capture (e.g. after display settings change while not game-hooked).</summary>
        public static void RefreshMonitorCaptureForSlot(int slotIndex)
        {
            _ = slotIndex;
            DisposeDisplaySource();
            AddMonitorCapture();
        }

        public static void AddMonitorCapture()
        {
            if (_mainScene == null)
            {
                Log.Warning("Cannot add monitor capture: scene not created");
                return;
            }

            int monitorIndex = 0;

            if (Settings.Instance.SelectedDisplay != null)
            {
                int? foundIndex = Settings.Instance.State.Displays
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

            _displaySource = MonitorCapture.FromMonitor(monitorIndex, "display")
                .SetCaptureMethod(captureMethod);

            // Add to scene (display is behind game capture in layer order)
            _displayItem = _mainScene.AddSource(_displaySource);

            Log.Information($"Display capture added for monitor {monitorIndex} using {Settings.Instance.DisplayCaptureMethod} method");
        }

        public static async Task StopRecording()
        {
            // Prevent race conditions when multiple callers try to stop recording simultaneously
            await _stopRecordingSemaphore.WaitAsync();
            try
            {
                // Check if already stopping or stopped
                if (_isStoppingOrStopped)
                {
                    Log.Information("StopRecording called but already stopping or stopped.");
                    return;
                }

                // Mark as stopping to prevent concurrent stop attempts
                _isStoppingOrStopped = true;

                GeneralUtils.SetProcessPriority(ProcessPriorityClass.Normal);

                StopGameCaptureHookTimeoutTimer();

                RecordingPreviewService.OnRecordingStopped();

                bool isReplayBufferMode = Settings.Instance.RecordingMode == RecordingMode.Buffer;
                bool isHybridMode = Settings.Instance.RecordingMode == RecordingMode.Hybrid;

                if (isReplayBufferMode && _bufferOutput != null)
                {
                    // Stop replay buffer
                    Log.Information("Stopping replay buffer...");
                    bool successfullyStopped = _bufferOutput.Stop(waitForCompletion: true, timeoutMs: 30000);

                    if (successfullyStopped)
                    {
                        Log.Information("Replay buffer stopped.");
                        // Small delay just to be sure
                        Thread.Sleep(200);
                    }
                    else
                    {
                        Log.Warning("Replay buffer did not stop within timeout. Forcing stop.");
                        _bufferOutput.ForceStop();
                        Thread.Sleep(500); // Brief wait after force stop
                    }

                    DisposeOutput();
                    DisposeSources();
                    DisposeEncoders();

                    NotifyIconService.SetNotifyIconStatus(NotifyIconState.Idle);

                    Log.Information("Replay buffer stopped and disposed.");

                    _ = GameIntegrationService.Shutdown();
                    KeybindCaptureService.Stop();

                    // Reload content list
                    await SettingsService.LoadContentFromFolderIntoState(false);
                }
                else if (!isReplayBufferMode && !isHybridMode && AnySessionOutputActive())
                {
                    if (Settings.Instance.State.Recording != null)
                        Settings.Instance.State.UpdateRecordingEndTime(DateTime.Now);

                    var outp = _pipeline.SessionOutput;
                    if (outp != null)
                    {
                        Log.Information("Stopping recording...");
                        bool successfullyStopped = outp.Stop(waitForCompletion: true, timeoutMs: 30000);

                        if (successfullyStopped)
                        {
                            Log.Information("Recording stopped.");
                            Thread.Sleep(200);
                        }
                        else
                        {
                            Log.Warning("Recording did not stop within timeout. Forcing stop.");
                            outp.ForceStop();
                            Thread.Sleep(500);
                        }
                    }

                    DisposeOutput();
                    DisposeSources();
                    DisposeEncoders();

                    NotifyIconService.SetNotifyIconStatus(NotifyIconState.Idle);

                    Log.Information("Session recording(s) stopped and disposed.");

                    _ = GameIntegrationService.Shutdown();
                    KeybindCaptureService.Stop();

                    async Task FinalizeSessionFileAsync(Recording? rec)
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

                        Log.Information($"Recording details (slot {rec.Slot}):");
                        Log.Information($"Start Time: {rec.StartTime}");
                        Log.Information($"End Time: {rec.EndTime}");
                        Log.Information($"Duration: {rec.Duration}");
                        Log.Information($"File Path: {rec.FilePath}");
                    }

                    var sessionRec0 = Settings.Instance.State.Recording;

                    await FinalizeSessionFileAsync(sessionRec0);

                    await VrChatVvmwIntegration.FlushDeferredClipsAsync();

                    if (Settings.Instance.EnableAi && Settings.Instance.AutoGenerateHighlights)
                    {
                        if (sessionRec0 != null && sessionRec0.FilePath != null && sessionRec0.Bookmarks.Any(b => b.Type.IncludeInHighlight()))
                            _ = AiService.CreateHighlight(Path.GetFileNameWithoutExtension(sessionRec0.FilePath));
                    }

                    await SettingsService.LoadContentFromFolderIntoState(false);

                    Settings.Instance.State.Recording = null;
                    ClearAllPendingPreRecordings();
                }
                else if (isHybridMode)
                {
                    if (Settings.Instance.State.Recording != null)
                        Settings.Instance.State.UpdateRecordingEndTime(DateTime.Now);

                    // Stop replay buffer first if running
                    if (_bufferOutput != null)
                    {
                        Log.Information("Hybrid: Stopping replay buffer...");
                        bool successfullyStopped = _bufferOutput.Stop(waitForCompletion: true, timeoutMs: 30000);

                        if (successfullyStopped)
                        {
                            Log.Information("Hybrid: Replay buffer stopped.");
                            // Small delay just to be sure
                            Thread.Sleep(200);
                        }
                        else
                        {
                            Log.Warning("Hybrid: Replay buffer did not stop within timeout. Forcing stop.");
                            _bufferOutput.ForceStop();
                            Thread.Sleep(500);
                        }
                    }

                    // Stop session recording
                    if (_output != null)
                    {
                        Log.Information("Hybrid: Stopping recording...");
                        bool successfullyStopped = _output.Stop(waitForCompletion: true, timeoutMs: 30000);

                        if (successfullyStopped)
                        {
                            Log.Information("Hybrid: Recording stopped.");
                            // Small delay just to be sure
                            Thread.Sleep(200);
                        }
                        else
                        {
                            Log.Warning("Hybrid: Recording did not stop within timeout. Forcing stop.");
                            _output.ForceStop();
                            Thread.Sleep(500);
                        }
                    }

                    DisposeOutput();
                    DisposeSources();
                    DisposeEncoders();

                    NotifyIconService.SetNotifyIconStatus(NotifyIconState.Idle);

                    Log.Information("Hybrid: All outputs stopped and disposed.");

                    _ = GameIntegrationService.Shutdown();
                    KeybindCaptureService.Stop();

                    if (Settings.Instance.State.Recording != null && Settings.Instance.State.Recording.FilePath != null)
                    {
                        // Check if we should discard the session due to no manual bookmarks
                        bool hasManualBookmarks = Settings.Instance.State.Recording.Bookmarks.Any(b => b.Type == BookmarkType.Manual);
                        if (Settings.Instance.DiscardSessionsWithoutBookmarks && !hasManualBookmarks)
                        {
                            Log.Information("Hybrid: Discarding session recording without manual bookmarks");
                            try
                            {
                                if (File.Exists(Settings.Instance.State.Recording.FilePath))
                                {
                                    File.Delete(Settings.Instance.State.Recording.FilePath);
                                    Log.Information($"Deleted video file: {Settings.Instance.State.Recording.FilePath}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"Failed to delete discarded session file: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Ensure file is fully written to disk/network before thumbnail generation
                            await EnsureFileReady(Settings.Instance.State.Recording.FilePath!);

                            int? igdbId = !string.IsNullOrEmpty(Settings.Instance.State.Recording.ExePath)
                                ? GameUtils.GetIgdbIdFromExePath(Settings.Instance.State.Recording.ExePath)
                                : null;
                            string hybridSessionFilePath = Settings.Instance.State.Recording.FilePath!;
                            await ContentService.CreateMetadataFile(hybridSessionFilePath, Content.ContentType.Session, Settings.Instance.State.Recording.Game, Settings.Instance.State.Recording.Bookmarks, igdbId: igdbId, audioTrackNames: Settings.Instance.State.Recording.AudioTrackNames);
                            await ContentService.CreateThumbnail(hybridSessionFilePath, Content.ContentType.Session);
                            _ = Task.Run(async () => await ContentService.CreateWaveformFile(hybridSessionFilePath, Content.ContentType.Session));
                            await VrChatVvmwIntegration.FlushDeferredClipsAsync();
                        }
                    }

                    await SettingsService.LoadContentFromFolderIntoState(false);
                }
                else
                {
                    DisposeOutput();
                    DisposeSources();
                    DisposeEncoders();
                    Settings.Instance.State.Recording = null;
                    ClearAllPendingPreRecordings();
                }

                await StorageService.EnsureStorageBelowLimit();

                // Reset hooked executable file name and captured dimensions
                _hookedExecutableFileName = null;
                CapturedWindowWidth = null;
                CapturedWindowHeight = null;

                // If the recording ends before it started, don't do anything (session+dual paths clear state earlier)
                if (Settings.Instance.State.Recording == null || (!isReplayBufferMode && Settings.Instance.State.Recording.FilePath == null))
                {
                    ClearAllPendingPreRecordings();
                    return;
                }

                // Get the file path before nullifying the recording (FilePath is not null at this point because of the previous check)
                string filePath = Settings.Instance.State.Recording.FilePath!;

                // Get the bookmarks before nullifying the recording
                List<Bookmark> bookmarks = Settings.Instance.State.Recording.Bookmarks;

                // Reset the recording and pre-recording
                Settings.Instance.State.Recording = null;
                ClearAllPendingPreRecordings();

                // If the recording is not a replay buffer recording, AI is enabled, user is authenticated, and auto generate highlights is enabled -> analyze the video!
                if (Settings.Instance.EnableAi && Settings.Instance.AutoGenerateHighlights && !isReplayBufferMode && bookmarks.Any(b => b.Type.IncludeInHighlight()))
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    _ = AiService.CreateHighlight(fileName);
                }
            }
            finally
            {
                _stopRecordingSemaphore.Release();
            }
        }

        /// <summary>
        /// Event handler for GameCapture.Hooked event.
        /// </summary>
        private static void OnGameCaptureHookedEvent(GameCapture capture, int slotIndex)
        {
            try
            {
                _ = slotIndex;
                // GameCapture now provides hooked info directly via its properties
                string? title = capture.HookedWindowTitle?.Trim();
                string? windowClass = capture.HookedWindowClass?.Trim();
                string? executable = capture.HookedExecutable?.Trim();

                // IsHooked is now managed by GameCapture automatically
                Log.Information($"Game hooked: Title='{title}', Class='{windowClass}', Executable='{executable}'");

                StopGameCaptureHookTimeoutTimer();

                // Remove display capture to save resources while game is hooked
                DisposeDisplaySource();

                var sl = _pipeline;
                // Switch output audio: mute desktop sources and unmute game/discord sources.
                // When using hybrid multi-track (master = mic+desktop, game/discord on other tracks), do not mute
                // desktop — muting removes it from all mixers so Track 1 would lose desktop audio.
                var audioOutputMode = Settings.Instance.AudioOutputMode;
                if (audioOutputMode != AudioOutputMode.All)
                {
                    // Separate tracks implies output capture may be routed to iso tracks + master; muting on hook
                    // would drop those tracks entirely (not only the old "fallback duplicate" case).
                    bool preserveDesktopForHybridTracks = Settings.Instance.EnableSeparateAudioTracks;

                    if (!preserveDesktopForHybridTracks)
                    {
                        foreach (var desktopSource in sl.DesktopSources)
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

                    if (audioOutputMode == AudioOutputMode.GameAndDiscord && sl.DiscordAudioSource != null)
                    {
                        try { sl.DiscordAudioSource.IsMuted = false; }
                        catch (Exception ex) { Log.Warning($"Failed to unmute Discord source: {ex.Message}"); }
                        Log.Information("Unmuted Discord audio source (game hooked)");
                    }
                }

                if (Settings.Instance.State.Recording != null)
                {
                    Settings.Instance.State.Recording.IsUsingGameHook = true;
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
            _ = slotIndex;
            // IsHooked is now managed by GameCapture automatically
            Log.Information("Game unhooked.");

            var sl = _pipeline;
            // Switch output audio back: unmute desktop sources and mute discord source
            var audioOutputMode = Settings.Instance.AudioOutputMode;
            if (audioOutputMode != AudioOutputMode.All)
            {
                foreach (var desktopSource in sl.DesktopSources)
                {
                    try { desktopSource.IsMuted = false; }
                    catch (Exception ex) { Log.Warning($"Failed to unmute desktop source: {ex.Message}"); }
                }
                Log.Information("Unmuted desktop audio sources (game unhooked, falling back to desktop audio)");

                if (audioOutputMode == AudioOutputMode.GameAndDiscord && sl.DiscordAudioSource != null)
                {
                    try { sl.DiscordAudioSource.IsMuted = true; }
                    catch (Exception ex) { Log.Warning($"Failed to mute Discord source: {ex.Message}"); }
                    Log.Information("Muted Discord audio source (game unhooked)");
                }
            }
        }

        private static void OnReplaySaved(nint calldata)
        {
            _replaySaved = true;
            Log.Information("Replay buffer saved callback received");
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

        public static void DisposeSources()
        {
            var sl = _pipeline;

            if (sl.MainScene != null)
            {
                try
                {
                    sl.MainScene.Dispose();
                    Log.Information("Scene disposed");
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose scene: {ex.Message}");
                }
                sl.MainScene = null;
            }

            sl.GameCaptureItem = null;
            sl.DisplayItem = null;

            DisposeDisplaySource();
            DisposeGameCaptureSource();

            foreach (var micSource in sl.MicSources)
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
            sl.MicSources.Clear();

            foreach (var desktopSource in sl.DesktopSources)
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
            sl.DesktopSources.Clear();

            if (sl.DiscordAudioSource != null)
            {
                try
                {
                    sl.DiscordAudioSource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose Discord audio source: {ex.Message}");
                }
                sl.DiscordAudioSource = null;
            }
        }

        public static void DisposeGameCaptureSource()
        {
            var sl = _pipeline;
            if (sl.GameCaptureItem != null)
            {
                try
                {
                    sl.GameCaptureItem.Remove();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to remove game capture scene item: {ex.Message}");
                }
                sl.GameCaptureItem = null;
            }

            if (sl.GameCapture != null)
            {
                try
                {
                    if (sl.HookedSubscription != null)
                        sl.GameCapture.Hooked -= sl.HookedSubscription;
                    if (sl.UnhookedSubscription != null)
                        sl.GameCapture.Unhooked -= sl.UnhookedSubscription;
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to unsubscribe from game capture events: {ex.Message}");
                }

                sl.HookedSubscription = null;
                sl.UnhookedSubscription = null;

                try
                {
                    sl.GameCapture.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose game capture source: {ex.Message}");
                }
                sl.GameCapture = null;
            }
            // Dispose the timer if it exists
            StopGameCaptureHookTimeoutTimer();
        }

        private static void StartGameCaptureHookTimeoutTimer(int slotIndex)
        {
            _ = slotIndex;
            StopGameCaptureHookTimeoutTimer();

            // Create a new timer that checks after 90 seconds
            _gameCaptureHookTimeoutTimer = new System.Threading.Timer(
                CheckGameCaptureHookStatus,
                null,
                90000, // 90 seconds delay
                Timeout.Infinite // Don't repeat
            );

            Log.Information("Started game capture hook timer (90 seconds)");
        }

        private static void StopGameCaptureHookTimeoutTimer()
        {
            if (_gameCaptureHookTimeoutTimer != null)
            {
                _gameCaptureHookTimeoutTimer.Dispose();
                _gameCaptureHookTimeoutTimer = null;
                Log.Information("Stopped game capture hook timer");
            }
        }

        private static void CheckGameCaptureHookStatus(object? state)
        {
            bool hooked = _pipeline.GameCapture?.IsHooked ?? false;
            // Check if game capture has hooked
            if (!hooked)
            {
                Log.Warning("Game capture did not hook within 90 seconds. Removing game capture source.");
                DisposeGameCaptureSource();
            }
            else
            {
                Log.Information("Game capture hook check completed. Hook status: {0}", hooked ? "Hooked" : "Not hooked");
                // Just stop the timer without disposing the game capture source if it's hooked
                StopGameCaptureHookTimeoutTimer();
            }
        }

        public static void DisposeDisplaySource()
        {
            var sl = _pipeline;
            if (sl.DisplayItem != null)
            {
                try
                {
                    Log.Information("Removing display scene item from scene");
                    sl.DisplayItem.Remove();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to remove display scene item: {ex.Message}");
                }
                sl.DisplayItem = null;
            }

            if (sl.DisplaySource != null)
            {
                try
                {
                    Log.Information("Disposing display source (expect OBS 'source destroyed' log to confirm WGC cleanup)");
                    sl.DisplaySource.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to dispose display source: {ex.Message}");
                }
                sl.DisplaySource = null;
            }
        }

        /// <summary>
        /// Clears encoder references. Encoders are auto-disposed by OBSKit.NET when outputs stop.
        /// </summary>
        public static void DisposeEncoders()
        {
            _pipeline.VideoEncoder = null;
            _pipeline.AudioEncoders.Clear();
        }

        /// <summary>
        /// Clears output references and signal connections. Outputs are auto-disposed by OBSKit.NET when Stop() is called.
        /// </summary>
        public static void DisposeOutput()
        {
            _replaySavedConnection?.Dispose();
            _replaySavedConnection = null;
            _pipeline.SessionOutput = null;
            _bufferOutput = null;
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
                versionToDownload = Settings.Instance.State.AvailableOBSVersions
                    .FirstOrDefault(v => v.Version == selectedVersion);

                if (versionToDownload == null)
                {
                    Log.Warning($"Selected OBS version {selectedVersion} not found in available versions. Using latest stable version.");
                }
            }

            // If no specific version was selected or found, use the latest non-beta version
            if (versionToDownload == null)
            {
                versionToDownload = Settings.Instance.State.AvailableOBSVersions
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
                // ── NVIDIA NVENC ────────────────────────────────────
                ["jim_nvenc"] = "NVIDIA NVENC H.264",
                ["jim_hevc_nvenc"] = "NVIDIA NVENC H.265",
                ["jim_av1_nvenc"] = "NVIDIA NVENC AV1",

                // ── AMD AMF ────────────────────────────────────────
                ["h264_texture_amf"] = "AMD AMF H.264",
                ["h265_texture_amf"] = "AMD AMF H.265",
                ["av1_texture_amf"] = "AMD AMF AV1",

                // ── Intel Quick Sync ───────────────────────────────
                ["obs_qsv11_v2"] = "Intel QSV H.264",
                ["obs_qsv11_hevc"] = "Intel QSV H.265",
                ["obs_qsv11_av1"] = "Intel QSV AV1",

                // ── CPU / software paths ───────────────────────────
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
                    Settings.Instance.State.Codecs.Add(new Codec { InternalEncoderId = encoderId, FriendlyName = friendlyName, IsHardwareEncoder = isHardware });
                }
                idx++;
            }

            Log.Information($"Total encoders found: {idx}");

            if (Settings.Instance.Codec == null)
            {
                Settings.Instance.Codec = SelectDefaultCodec(Settings.Instance.Encoder, Settings.Instance.State.Codecs);
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
            if (!IsInitialized || _pipeline.MainScene == null)
                return null;

            try
            {
                var gcap = _pipeline.GameCapture;
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

                var disp = _pipeline.DisplaySource;
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

                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TryGetRecordingPreviewJpeg failed");
                return null;
            }
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
