using LibObs;
using NAudio.Wave;
using Segra.Backend.Services;
using Segra.Models;
using Serilog;
using System.IO.Compression;
using System.Runtime.InteropServices;
using static LibObs.Obs;

namespace Segra.Backend.Utils
{
    public static class OBSUtils
    {
        public static bool IsInitialized { get; private set; }
        public static string CurrentTrackedFileName { get; set; }
        static bool signalOutputStop = false;
        static IntPtr output = IntPtr.Zero;
        static IntPtr displaySource = IntPtr.Zero;
        static List<IntPtr> micSources = new List<IntPtr>();
        static List<IntPtr> desktopSources = new List<IntPtr>();
        static IntPtr videoEncoder = IntPtr.Zero;
        static IntPtr audioEncoder = IntPtr.Zero;

        static signal_callback_t outputStopCallback = (data, cd) =>
        {
            signalOutputStop = true;
        };

        static signal_callback_t hookedCallback;
        static signal_callback_t unhookedCallback;

        private static bool _isGameCaptureHooked = false;

        public static async Task InitializeAsync()
        {
            if (IsInitialized)
                return;

            await CheckIfExistsOrDownloadAsync();

            if (obs_initialized())
                throw new Exception("Error: OBS is already initialized.");

            base_set_log_handler(new log_handler_t((level, msg, args, p) =>
            {
                try
                {
                    string formattedMessage = MarshalUtils.GetLogMessage(msg, args);

                    if (formattedMessage.Contains("Starting capture"))
                        _isGameCaptureHooked = true;

                    if (formattedMessage.Contains("capture stopped"))
                        _isGameCaptureHooked = false;

                    Log.Information($"{((LogErrorLevel)level)}: {formattedMessage}");
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                    Log.Error(e.StackTrace);
                }
            }), IntPtr.Zero);

            Log.Information("libobs version: " + obs_get_version_string());

            // Step 1: Call obs_startup() as per documentation
            if (!obs_startup("en-US", null, IntPtr.Zero))
                throw new Exception("Error during OBS startup.");

            // Step 2: Set modules path
            obs_add_data_path("./data/libobs/");
            obs_add_module_path("./obs-plugins/64bit/", "./data/obs-plugins/%module%/");

            // BUG: According to the documentation, ResetVideoSettings() should be called before loading modules but this causes black screen on recordings
            // https://github.com/Segergren/Segra/issues/1

            // Step 3: Reset audio settings as per documentation
            if (!ResetAudioSettings())
                throw new Exception("Failed to initialize audio settings.");

            // Step 4: Load modules
            obs_load_all_modules();
            obs_log_loaded_modules();


            // Step 5: Should be called before Step 4 as per documentation but this causes black screen on recordings
            // This probably causes the lag
            if (!ResetVideoSettings())
                throw new Exception("Failed to initialize video settings.");

            // Step 6: Post-load modules
            obs_post_load_modules();

            IsInitialized = true;
            Settings.Instance.State.HasLoadedObs = true;
            Log.Information("OBS initialized successfully!");

            GameDetectionService.StartAsync();
        }

        private static bool ResetAudioSettings()
        {
            obs_audio_info audioInfo = new obs_audio_info()
            {
                samples_per_sec = 44100,
                speakers = speaker_layout.SPEAKERS_STEREO
            };

            return obs_reset_audio(ref audioInfo);
        }

        private static bool ResetVideoSettings(uint? customFps = null, uint? customOutputWidth = null, uint? customOutputHeight = null)
        {
            uint baseWidth, baseHeight;
            SettingsUtils.GetPrimaryMonitorResolution(out baseWidth, out baseHeight);

            // Use custom values if provided, otherwise use defaults
            uint outputWidth = customOutputWidth ?? baseWidth;
            uint outputHeight = customOutputHeight ?? baseHeight;

            obs_video_info videoInfo = new obs_video_info()
            {
                adapter = 0,
                graphics_module = "libobs-d3d11",
                fps_num = customFps ?? 60, // Default to 60 FPS if not specified
                fps_den = 1,
                base_width = baseWidth,
                base_height = baseHeight,
                output_width = outputWidth,
                output_height = outputHeight,
                output_format = video_format.VIDEO_FORMAT_NV12,
                gpu_conversion = true,
                colorspace = video_colorspace.VIDEO_CS_DEFAULT,
                range = video_range_type.VIDEO_RANGE_DEFAULT,
                scale_type = obs_scale_type.OBS_SCALE_BILINEAR
            };

            return obs_reset_video(ref videoInfo) == 0; // Returns true if successful
        }

        public static bool StartRecording(string name = "Unknown")
        {
            if (output != IntPtr.Zero)
            {
                Log.Information("Recording is already in progress.");
                return false;
            }

            signalOutputStop = false;

            // Note: According to docs, audio settings cannot be reconfigured after initialization
            // but video can be reset as long as no outputs are active

            // Configure video settings specifically for this recording
            uint outputWidth, outputHeight;
            SettingsUtils.GetResolution(Settings.Instance.Resolution, out outputWidth, out outputHeight);

            if (!ResetVideoSettings(
                customFps: (uint)Settings.Instance.FrameRate,
                customOutputWidth: outputWidth,
                customOutputHeight: outputHeight))
            {
                throw new Exception("Failed to configure video settings for recording.");
            }

            _isGameCaptureHooked = false;

            IntPtr videoSourceSettings = obs_data_create();
            obs_data_set_string(videoSourceSettings, "capture_mode", "any_fullscreen");
            displaySource = obs_source_create("game_capture", "gameplay", videoSourceSettings, IntPtr.Zero);
            obs_data_release(videoSourceSettings);
            obs_set_output_source(0, displaySource);

            // Connect to 'hooked' and 'unhooked' signals for game capture
            IntPtr signalHandler = obs_source_get_signal_handler(displaySource);
            hookedCallback = new signal_callback_t(OnGameCaptureHooked);
            unhookedCallback = new signal_callback_t(OnGameCaptureUnhooked);
            signal_handler_connect(signalHandler, "hooked", hookedCallback, IntPtr.Zero);
            signal_handler_connect(signalHandler, "unhooked", unhookedCallback, IntPtr.Zero);

            bool hooked = WaitUntilGameCaptureHooks();

            if (!hooked)
            {
                Log.Error("Game Capture did not hook within 40 seconds.");
                DisposeSources();

                if (!Settings.Instance.EnableDisplayRecording)
                {
                    Log.Information("Display recording is disabled, stopping recording.");
                    StopRecording();
                    return false;
                }

                Log.Information("Using display capture instead");

                IntPtr displayCaptureSettings = obs_data_create();
                displaySource = obs_source_create("monitor_capture", "display", displayCaptureSettings, IntPtr.Zero);
                obs_data_release(displayCaptureSettings);
                obs_set_output_source(0, displaySource);
            }

            IntPtr videoEncoderSettings = obs_data_create();
            obs_data_set_string(videoEncoderSettings, "preset", "Quality");
            obs_data_set_string(videoEncoderSettings, "profile", "high");
            obs_data_set_bool(videoEncoderSettings, "use_bufsize", true);
            obs_data_set_string(videoEncoderSettings, "rate_control", Settings.Instance.RateControl);

            switch (Settings.Instance.RateControl)
            {
                case "CBR":
                    int videoBitrateKbps = Settings.Instance.Bitrate * 1000;
                    obs_data_set_int(videoEncoderSettings, "bitrate", (uint)videoBitrateKbps);
                    break;

                case "VBR":
                    videoBitrateKbps = Settings.Instance.Bitrate * 1000;
                    obs_data_set_int(videoEncoderSettings, "bitrate", (uint)videoBitrateKbps);
                    break;

                case "CRF":
                    obs_data_set_int(videoEncoderSettings, "crf", (uint)Settings.Instance.CrfValue);
                    break;

                case "CQP":
                    obs_data_set_int(videoEncoderSettings, "qp", (uint)Settings.Instance.CqLevel);
                    break;

                default:
                    throw new Exception("Unsupported Rate Control method.");
            }

            videoEncoder = obs_video_encoder_create("jim_nvenc", "Segra Recorder", videoEncoderSettings, IntPtr.Zero);
            obs_data_release(videoEncoderSettings);
            obs_encoder_set_video(videoEncoder, obs_get_video());

            if (Settings.Instance.InputDevices != null && Settings.Instance.InputDevices.Count > 0)
            {
                int audioSourceIndex = 1;
                
                foreach (var deviceId in Settings.Instance.InputDevices)
                {
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        IntPtr micSettings = obs_data_create();
                        obs_data_set_string(micSettings, "device_id", deviceId);
                        
                        string sourceName = $"Microphone_{micSources.Count + 1}";
                        IntPtr micSource = obs_source_create("wasapi_input_capture", sourceName, micSettings, IntPtr.Zero);
                        
                        obs_data_release(micSettings);
                        
                        obs_set_output_source((uint)audioSourceIndex, micSource);
                        micSources.Add(micSource);
                        
                        audioSourceIndex++;
                        Log.Information($"Added input device: {deviceId} as {sourceName}");
                    }
                }
            }

            if (Settings.Instance.OutputDevices != null && Settings.Instance.OutputDevices.Count > 0)
            {
                int desktopSourceIndex = micSources.Count + 1;
                
                foreach (var deviceId in Settings.Instance.OutputDevices)
                {
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        IntPtr desktopSettings = obs_data_create();
                        obs_data_set_string(desktopSettings, "device_id", deviceId);
                        
                        string sourceName = $"DesktopAudio_{desktopSources.Count + 1}";
                        IntPtr desktopSource = obs_source_create("wasapi_output_capture", sourceName, desktopSettings, IntPtr.Zero);
                        
                        obs_data_release(desktopSettings);
                        
                        obs_set_output_source((uint)desktopSourceIndex, desktopSource);
                        desktopSources.Add(desktopSource);
                        
                        desktopSourceIndex++;
                        Log.Information($"Added output device: {deviceId} as {sourceName}");
                    }
                }
            }

            IntPtr audioEncoderSettings = obs_data_create();
            obs_data_set_int(audioEncoderSettings, "bitrate", 128);
            audioEncoder = obs_audio_encoder_create("ffmpeg_aac", "simple_aac_encoder", audioEncoderSettings, 0, IntPtr.Zero);
            obs_data_release(audioEncoderSettings);
            obs_encoder_set_audio(audioEncoder, obs_get_audio());

            IntPtr outputSettings = obs_data_create();
            Content.ContentType contentType = Content.ContentType.Session; // TODO (os): implement dynamic if replay buffer
            string videoPath = Settings.Instance.ContentFolder + "/" + contentType.ToString().ToLower() + "s";

            if (!Directory.Exists(videoPath))
                Directory.CreateDirectory(videoPath);

            string videoOutputPath = $"{Settings.Instance.ContentFolder}/{contentType.ToString().ToLower()}s/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";
            string filePath = $"{Settings.Instance.ContentFolder}/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4";

            obs_data_set_string(outputSettings, "path", videoOutputPath);
            obs_data_set_string(outputSettings, "format_name", "mp4");

            output = obs_output_create("ffmpeg_muxer", "simple_output", outputSettings, IntPtr.Zero);
            obs_data_release(outputSettings);

            obs_output_set_video_encoder(output, videoEncoder);
            obs_output_set_audio_encoder(output, audioEncoder, 0);

            signal_handler_connect(obs_output_get_signal_handler(output), "stop", outputStopCallback, IntPtr.Zero);

            // Used to show "Initializing" in frontend
            Settings.Instance.State.Recording = new Recording()
            {
                StartTime = DateTime.Now.AddSeconds(2),
                FilePath = videoOutputPath,
                Game = name,
                IsUsingGameHook = hooked
            };
            MessageUtils.SendSettingsToFrontend();

            Task.Run(PlayStartSound);
            if (!obs_output_start(output))
            {
                Log.Error("Failed to start recording.");
                Settings.Instance.State.Recording = null;
                MessageUtils.SendSettingsToFrontend();
                return false;
            }

            // The recording has actually started, set actual start time
            Settings.Instance.State.Recording = new Recording()
            {
                StartTime = DateTime.Now,
                FilePath = videoOutputPath,
                Game = name,
                IsUsingGameHook = hooked
            };
            MessageUtils.SendSettingsToFrontend();

            Log.Information("Recording started: " + filePath);
            GameIntegrationService.Start(name);
            Task.Run(KeybindCaptureService.Start);
            return true;
        }

        public static void StopRecording()
        {
            CurrentTrackedFileName = null;
            if (output != IntPtr.Zero)
            {
                if (Settings.Instance.State.Recording != null)
                    Settings.Instance.State.UpdateRecordingEndTime(DateTime.Now);

                obs_output_stop(output);

                int attempts = 0;

                while (!signalOutputStop && attempts < 300)
                {
                    Thread.Sleep(100);
                    attempts++;
                }

                if (!signalOutputStop)
                {
                    Log.Warning("Failed to stop recording. Forcing stop.");
                    obs_output_force_stop(output);
                }
                else
                {
                    Log.Information("Output stopped.");
                }

                Thread.Sleep(200);

                DisposeOutput();
                DisposeSources();
                DisposeEncoders();

                output = IntPtr.Zero;

                Log.Information("Recording stopped.");

                GameIntegrationService.Shutdown();
                KeybindCaptureService.Stop();

                ContentUtils.CreateMetadataFile(Settings.Instance.State.Recording.FilePath, Content.ContentType.Session, Settings.Instance.State.Recording.Game, Settings.Instance.State.Recording.Bookmarks);
                ContentUtils.CreateThumbnail(Settings.Instance.State.Recording.FilePath, Content.ContentType.Session);

                if (Settings.Instance.State.Recording != null)
                {
                    Log.Information($"Recording details:");
                    Log.Information($"Start Time: {Settings.Instance.State.Recording.StartTime}");
                    Log.Information($"End Time: {Settings.Instance.State.Recording.EndTime}");
                    Log.Information($"Duration: {Settings.Instance.State.Recording.Duration}");
                    Log.Information($"File Path: {Settings.Instance.State.Recording.FilePath}");
                }

                SettingsUtils.LoadContentFromFolderIntoState(false);
            }
            else
            {
                DisposeOutput();
                DisposeSources();
                DisposeEncoders();
            }
            Task.Run(StorageUtils.EnsureStorageBelowLimit);

            // If the recording ends before it started, don't do anything
            if (Settings.Instance.State.Recording == null || Settings.Instance.State.Recording.FilePath == null)
            {
                return;
            }

            string fileName = Path.GetFileNameWithoutExtension(Settings.Instance.State.Recording.FilePath);

            Settings.Instance.State.Recording = null;
            OBSUtils.CurrentTrackedFileName = null;
            if (Settings.Instance.EnableAi && AuthService.IsAuthenticated())
            {
                Task.Run(() => AiService.AnalyzeVideo(fileName));
            }
        }

        [System.Diagnostics.DebuggerStepThrough]
        private static void OnGameCaptureHooked(IntPtr data, calldata_t cd)
        {
            IntPtr cdPtr = Marshal.AllocHGlobal(Marshal.SizeOf<calldata_t>());
            Marshal.StructureToPtr(cd, cdPtr, false);

            if (cdPtr == IntPtr.Zero)
            {
                Log.Warning("GameCaptureHooked callback received null calldata pointer.");
                return;
            }

            try
            {
                calldata_get_string(cdPtr, "title", out IntPtr title);
                calldata_get_string(cdPtr, "class", out IntPtr windowClass);
                calldata_get_string(cdPtr, "executable", out IntPtr executable);

                _isGameCaptureHooked = true;
                Log.Information($"Game hooked: Title='{Marshal.PtrToStringAnsi(title)}', Class='{Marshal.PtrToStringAnsi(windowClass)}', Executable='{Marshal.PtrToStringAnsi(executable)}'");

                // Overwrite the current tracked file name with the hooked one because sometimes the current tracked file name is the startup exe instead of the actual game 
                CurrentTrackedFileName = Path.GetFileName(Marshal.PtrToStringAnsi(executable));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing OnGameCaptureHooked signal");
            }
        }

        private static void OnGameCaptureUnhooked(IntPtr data, calldata_t cd)
        {
            IntPtr cdPtr = Marshal.AllocHGlobal(Marshal.SizeOf<calldata_t>());
            Marshal.StructureToPtr(cd, cdPtr, false);

            _isGameCaptureHooked = false;
            Log.Information("Game unhooked.");
        }

        private static bool WaitUntilGameCaptureHooks(int timeoutMs = 80000)
        {
            int elapsed = 0;
            const int step = 100;

            while (!_isGameCaptureHooked)
            {
                Thread.Sleep(step);
                elapsed += step;
                if (elapsed >= timeoutMs)
                    return false;
            }

            return true;
        }

        public static void DisposeSources()
        {
            if (displaySource != IntPtr.Zero)
            {
                IntPtr signalHandler = obs_source_get_signal_handler(displaySource);

                if (hookedCallback != null)
                {
                    signal_handler_disconnect(signalHandler, "hooked", hookedCallback, IntPtr.Zero);
                    hookedCallback = null;
                }

                if (unhookedCallback != null)
                {
                    signal_handler_disconnect(signalHandler, "unhooked", unhookedCallback, IntPtr.Zero);
                    unhookedCallback = null;
                }

                obs_set_output_source(0, IntPtr.Zero);
                obs_source_release(displaySource);
                displaySource = IntPtr.Zero;
            }

            int micSourcesCount = micSources.Count;
            for (int i = 0; i < micSources.Count; i++)
            {
                if (micSources[i] != IntPtr.Zero)
                {
                    obs_set_output_source((uint)(i + 1), IntPtr.Zero);
                    obs_source_release(micSources[i]);
                    micSources[i] = IntPtr.Zero;
                }
            }
            micSources.Clear();

            for (int i = 0; i < desktopSources.Count; i++)
            {
                if (desktopSources[i] != IntPtr.Zero)
                {
                    int desktopIndex = i + micSourcesCount + 1;
                    obs_set_output_source((uint)desktopIndex, IntPtr.Zero);
                    obs_source_release(desktopSources[i]);
                    desktopSources[i] = IntPtr.Zero;
                }
            }
            desktopSources.Clear();
        }

        public static void DisposeEncoders()
        {
            if (videoEncoder != IntPtr.Zero)
            {
                obs_encoder_release(videoEncoder);
                videoEncoder = IntPtr.Zero;
            }

            if (audioEncoder != IntPtr.Zero)
            {
                obs_encoder_release(audioEncoder);
                audioEncoder = IntPtr.Zero;
            }
        }

        public static void DisposeOutput()
        {
            if (output != IntPtr.Zero)
            {
                signal_handler_disconnect(obs_output_get_signal_handler(output), "stop", outputStopCallback, IntPtr.Zero);
                obs_output_release(output);
                output = IntPtr.Zero;
            }
        }

        private static async Task CheckIfExistsOrDownloadAsync()
        {
            Log.Information("Checking if OBS is installed");

            string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string dllPath = Path.Combine(currentDirectory, "obs.dll");

            if (File.Exists(dllPath))
            {
                Log.Information("OBS is installed");
                return;
            }

            string zipPath = Path.Combine(currentDirectory, "obs.zip");
            string apiUrl = "https://api.github.com/repos/Segergren/Segra/contents/obs.zip?ref=main";

            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Segra");
                httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.json");

                Log.Information("Fetching download URL...");

                var response = await httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Error($"Failed to fetch metadata from {apiUrl}. Status: {response.StatusCode}");
                    throw new Exception($"Failed to fetch file metadata: {response.ReasonPhrase}");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var metadata = System.Text.Json.JsonSerializer.Deserialize<GitHubFileMetadata>(jsonResponse);

                if (metadata?.DownloadUrl == null)
                {
                    Log.Error("Download URL not found in the API response.");
                    throw new Exception("Invalid API response: Missing download URL.");
                }

                Log.Information("Downloading OBS...");

                httpClient.DefaultRequestHeaders.Clear();
                var zipBytes = await httpClient.GetByteArrayAsync(metadata.DownloadUrl);
                await File.WriteAllBytesAsync(zipPath, zipBytes);

                Log.Information("Download complete");
            }

            Log.Information("Extracting OBS...");
            ZipFile.ExtractToDirectory(zipPath, currentDirectory);
            File.Delete(zipPath);
            Log.Information("OBS setup complete");
        }

        private class GitHubFileMetadata
        {
            [System.Text.Json.Serialization.JsonPropertyName("download_url")]
            public string DownloadUrl { get; set; }
        }

        private static void PlayStartSound()
        {
            using (var unmanagedStream = Properties.Resources.start)
            using (var memoryStream = new MemoryStream())
            {
                unmanagedStream.CopyTo(memoryStream);
                byte[] audioData = memoryStream.ToArray();

                using (var audioReader = new WaveFileReader(new MemoryStream(audioData)))
                using (var waveOut = new WaveOutEvent())
                {
                    var volumeStream = new VolumeWaveProvider16(audioReader)
                    {
                        Volume = 0.5f
                    };

                    waveOut.Init(volumeStream);
                    waveOut.Play();

                    while (waveOut.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(100);
                }
            }
        }
    }
}