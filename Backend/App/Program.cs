using NuGet.Versioning;
using Photino.NET;
using Photino.NET.Server;
using Segra.Backend.Api;
using Segra.Backend.Core.Models;
using Segra.Backend.Recorder;
using Segra.Backend.Services;
using Segra.Backend.Utils;
using Segra.Backend.Windows.Input;
using Segra.Backend.Windows.Power;
using Segra.Backend.Windows.Storage;
using Serilog;
using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Velopack;

namespace Segra.Backend.App
{
    class Program
    {
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        /// <summary>WebView2 init args for the monitoring PiP window.</summary>
        private const string MonitoringBrowserInitParameters =
            "--enable-blink-features=AudioVideoTracks";

        const int SW_HIDE = 0;
        /// <summary>Win32: restore normal position/size before applying real maximize.</summary>
        private const int SW_RESTORE = 9;
        /// <summary>Win32 <c>SW_SHOWMAXIMIZED</c> — maximize into work area.</summary>
        private const int SW_SHOWMAXIMIZED = 3;
        public static bool IsFirstRun { get; private set; } = false;
        private static readonly AutoResetEvent ShowWindowEvent = new AutoResetEvent(false);
        public static bool hasLoadedInitialSettings = false;
        public static PhotinoWindow? Window { get; private set; }
        public static PhotinoWindow? MonitoringWindow { get; private set; }
        private static bool _monitoringWindowTopMost = true;
        private static Point? _monitoringWindowLocation;
        private static Size? _monitoringWindowSize;
        private static readonly string MonitoringWindowBoundsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Segra",
            "monitoring-window-bounds.json");
        private static readonly string LogFilePath =
          Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra", "logs.log");
        private const string PipeName = "Segra_SingleInstance";
        private static Mutex? singleInstanceMutex;
        private static Thread? pipeServerThread;
        private static string? appUrl;
        private const long maxFileSizeBytes = 10 * 1024 * 1024; // 10MB
        private const long trimTargetBytes = 8 * 1024 * 1024; // trim down to 8MB when limit is hit
        private const string LogOutputTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        [STAThread]
        static void Main(string[] args)
        {
            // Set process DPI aware to ensure we capture at physical resolution
            SetProcessDPIAware();

            // In debug mode, kill any existing instances before starting
#if DEBUG
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var existingProcesses = Process.GetProcessesByName(currentProcess.ProcessName)
                    .Where(p => p.Id != currentProcess.Id);

                foreach (var process in existingProcesses)
                {
                    Console.WriteLine($"[DEBUG] Killing existing instance: PID {process.Id}");
                    process.Kill();
                    process.WaitForExit(3000); // Wait up to 3 seconds for graceful exit
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Failed to kill existing instance: {ex.Message}");
            }
#endif

            // Try to create a named mutex - this will fail if another instance exists
            singleInstanceMutex = new Mutex(true, "SegraApplicationMutex", out bool createdNew);

            if (!createdNew)
            {
                // Another instance exists, send a message to it via named pipe
                int exitCode = 0;
                try
                {
                    using (var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        pipeClient.Connect(3000);

                        using (var writer = new StreamWriter(pipeClient))
                        {
                            writer.WriteLine("SHOW_WINDOW");
                            writer.Flush();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to communicate with existing instance: {ex.Message}");
                    exitCode = 1;
                }

                // Important: never continue startup when mutex indicates another instance exists.
                Environment.Exit(exitCode);
                return;
            }

            StartNamedPipeServer();

            var logDirectory = Path.GetDirectoryName(LogFilePath);
            if (logDirectory != null && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            ConfigureLogging();

            // Get the current version
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            VelopackApp.Build()
                .OnBeforeUpdateFastCallback((v) =>
                {
                    if (UpdateService.UpdateManager == null)
                    {
                        Log.Error("UpdateManager is null");
                        return;
                    }
                    SemanticVersion? currentVersion = UpdateService.UpdateManager.CurrentVersion;
                    if (currentVersion == null)
                    {
                        Log.Error("Current version is null");
                        return;
                    }
                    Log.Information($"Updating from version {currentVersion} to {v}");
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "segra.tmp"), currentVersion.ToString());
                })
                .OnAfterUpdateFastCallback((v) =>
                {
                    string previousVersionPath = Path.Combine(Path.GetTempPath(), "segra.tmp");
                    if (File.Exists(previousVersionPath))
                    {
                        string previousVersion = File.ReadAllText(previousVersionPath);
                        Log.Information($"Updated from version {previousVersion} to {v}");
                        Task.Run(async () =>
                        {
                            await Task.Delay(5000);
                            _ = MessageService.SendFrontendMessage("ShowReleaseNotes", previousVersion);
                        });
                        File.Delete(previousVersionPath);
                    }
                })
                .OnFirstRun((v) =>
                {
                    Log.Information($"First run of Segra {v}");
                })
                .Run();

            try
            {
                Log.Information("Application starting up...");

                // Always prefer frontend from disk wwwroot next to the executable.
                Directory.SetCurrentDirectory(AppContext.BaseDirectory);
                bool IsDebugMode =
#if DEBUG
                    true;
#else
                    false;
#endif
                if (!IsDebugMode)
                {
                    string webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                    string webRootIndexPath = Path.Combine(webRootPath, "index.html");
                    if (!Directory.Exists(webRootPath) || !File.Exists(webRootIndexPath))
                    {
                        throw new DirectoryNotFoundException(
                            $"Missing frontend build output at '{webRootPath}'. Embedded fallback is disabled.");
                    }
                }

                // Set up the PhotinoServer
                PhotinoServer
                    .CreateStaticFileServer(args, out string baseUrl)
                    .RunAsync();

                // Add a startup cache-buster so WebView cannot reuse stale disk-cached index.html.
                // Static assets are content-hashed by Vite, so this only forces fresh app shell load.
                string startupCacheBust = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                appUrl = IsDebugMode
                    ? $"http://localhost:2882/?v={startupCacheBust}"
                    : $"{baseUrl}/index.html?v={startupCacheBust}";

                if (IsDebugMode)
                {
                    Task.Run(() =>
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = "/c npm run dev",
                            WorkingDirectory = Path.Join(GetSolutionPath(), @"Frontend")
                        };

                        using (HttpClient client = new())
                        {
                            client.DefaultRequestHeaders.ExpectContinue = false;
                            try
                            {
                                // Set a short timeout since we're just checking if the server is running
                                client.Timeout = TimeSpan.FromSeconds(1);
                                var response = client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "http://localhost:2882/index.html")).Result;
                            }
                            catch (Exception)
                            {
                                Process.Start(startInfo);
                            }
                        }
                    });
                }

                // Get the directory containing the executable
                Log.Information("Serving React app at {AppUrl}", appUrl);

                Task.Run(() =>
                {
                    string prefix = "http://localhost:2222/";
                    ContentServer.StartServer(prefix);
                });

                IsFirstRun = !SettingsService.LoadSettings();
                hasLoadedInitialSettings = true;
                Settings.Instance.State.Initialize();
                SettingsService.SaveSettings();
                if (IsFirstRun)
                {
                    _ = SettingsService.LoadContentFromFolderIntoState(true);
                    StartupService.SetStartupStatus(true);
                    Settings.Instance.State.GpuVendor = GeneralUtils.DetectGpuVendor();
                    if (Settings.Instance.State.GpuVendor == GeneralUtils.GpuVendor.Nvidia)
                    {
                        Settings.Instance.State.CudaComputeCapability = GeneralUtils.DetectCudaComputeCapability();
                    }
                    SettingsService.SelectDefaultDevices();
                    _ = PresetsService.ApplyVideoPreset("high");
                    _ = PresetsService.ApplyClipPreset("standard");
                }

                // Ensure content folder exists
                if (!Directory.Exists(Settings.Instance.ContentFolder))
                {
                    Directory.CreateDirectory(Settings.Instance.ContentFolder);
                }

                // Run data migrations
                Task.Run(MigrationService.RunMigrations);

                // Start WebSocket and Load Settings
                Task.Run(MessageService.StartWebsocket);
                Task.Run(MessageService.StartLegacyPortFallback);
                Task.Run(StorageService.EnsureStorageBelowLimit);

                // Check for updates
                Task.Run(UpdateService.UpdateAppIfNecessary);

                // Check if application was launched from startup
                bool startMinimized = IsLaunchedFromStartup();
                Log.Information($"Starting application{(startMinimized ? " minimized from startup" : "")}");

                AddNotifyIcon();

                // Start monitoring system power state changes (sleep/wake)
                Task.Run(PowerModeMonitor.StartMonitoring);

                // Run the OBS Initializer in a separate thread and application to make sure someting on the main thread doesn't block
                Task.Run(() => Application.Run(new OBSWindow()));

                // Global keybind hook (low-level keyboard hook + message loop) — runs for the lifetime of the app.
                Task.Run(KeybindCaptureService.Start);

                if (!startMinimized)
                {
                    LoadFrontend();
                }

                // Wait for show window events
                while (true)
                {
                    int signalIndex = WaitHandle.WaitAny([ShowWindowEvent]);
                    Log.Information($"Signal received: {signalIndex}");
                    if (signalIndex == 0)
                    {
                        Log.Information("Show window event triggered");
                        ShowApplicationWindow().GetAwaiter().GetResult();
                        Log.Information("Show window event completed");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly.");
            }
            finally
            {
                Shutdown();
            }
        }

        private static void Shutdown()
        {
            Log.Information("Application shutting down.");

            // Shutdown OBS if it was initialized
            OBSService.Shutdown();

            Log.CloseAndFlush(); // Ensure all logs are written before the application exits

            // Release the mutex when closing (only if we own it)
            if (singleInstanceMutex != null)
            {
                try
                {
                    singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Mutex was not owned by this thread, which is fine
                    // This can happen when exiting from the tray icon thread
                }
                finally
                {
                    singleInstanceMutex.Dispose();
                }
            }
        }

        public static void ConfigureLogging()
        {
            PurgeOldLogs();

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                //.WriteTo.Debug(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Warning) // Remove restricted minimum level to show all logs but increase lag while debugging
                .WriteTo.Sink(new TrimmingFileSink(LogFilePath, maxFileSizeBytes, trimTargetBytes, LogOutputTemplate))
                .CreateLogger();
        }

        private static void PurgeOldLogs()
        {
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra");

                if (!Directory.Exists(logDirectory))
                    return;

                var logFiles = Directory.GetFiles(logDirectory, "*.log");

                if (logFiles.Length == 0)
                    return;

                // Get the first .log file found
                var logFilePath = logFiles[0];
                var fileInfo = new FileInfo(logFilePath);

                if (!fileInfo.Exists || fileInfo.Length <= maxFileSizeBytes)
                    return;

                var lines = File.ReadAllLines(logFilePath).ToList();
                var avgLineSize = fileInfo.Length / lines.Count;
                var linesToKeep = (int)(trimTargetBytes / avgLineSize);

                if (linesToKeep < lines.Count)
                {
                    var recentLines = lines.Skip(lines.Count - linesToKeep).ToList();
                    File.WriteAllLines(logFilePath, recentLines);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error purging logs: {ex.Message}");
            }
        }

        private static Size? _windowSizeBeforeFullscreen;
        private static Point? _windowLocationBeforeFullscreen;
        private static bool _wasMaximizedBeforeFullscreen;

        /// <summary>
        /// After OS fullscreen on Windows, <see cref="PhotinoWindow.SetMaximized"/> alone can leave the HWND sized to the monitor
        /// instead of the work area; bounce via Win32 Restore → ShowMaximized when possible.
        /// </summary>
        private static void RestoreMaximizedAfterFullscreen(PhotinoWindow window)
        {
            window.SetMaximized(false);

            if (PhotinoWindow.IsWindowsPlatform)
            {
                try
                {
                    var hwnd = window.WindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, SW_RESTORE);
                        ShowWindow(hwnd, SW_SHOWMAXIMIZED);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Win32 restore→maximize after fullscreen failed; Photino maximize only.");
                }
            }

            // Keep Photino's managed fullscreen/maximize bookkeeping in sync with the HWND.
            window.SetMaximized(true);
        }

        public static void SetFullscreen(bool enabled)
        {
            try
            {
                if (Window == null) return;

                if (enabled)
                {
                    _wasMaximizedBeforeFullscreen = Window.Maximized;
                    _windowSizeBeforeFullscreen = Window.Size;
                    _windowLocationBeforeFullscreen = Window.Location;
                    // If still maximized, Win32 fullscreen can behave like bounded maximize (taskbar stays visible).
                    if (Window.Maximized)
                        Window.SetMaximized(false);
                    // Native OS fullscreen fills the monitor; maximize alone only fills the work area.
                    Window.SetFullScreen(true);
                }
                else
                {
                    Window.SetFullScreen(false);

                    if (_wasMaximizedBeforeFullscreen)
                        RestoreMaximizedAfterFullscreen(Window);
                    else if (_windowSizeBeforeFullscreen.HasValue && _windowLocationBeforeFullscreen.HasValue)
                    {
                        Window.SetMaximized(false);
                        Window.SetSize(_windowSizeBeforeFullscreen.Value);
                        Window.SetLocation(_windowLocationBeforeFullscreen.Value);
                    }
                    else
                        Window.SetMaximized(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error setting fullscreen state");
            }
        }

        public static void ApplyMonitoringWindowLayout(bool open)
        {
            if (open)
                ShowMonitoringWindow();
            else
                CloseMonitoringWindow();
        }

        /// <summary>Opens the monitoring window when recording starts, if it is not already open.</summary>
        public static void ShowMonitoringWindowIfClosed()
        {
            if (MonitoringWindow != null) return;
            ShowMonitoringWindow();
        }

        private static void LoadMonitoringWindowBounds()
        {
            if (_monitoringWindowLocation.HasValue) return;

            try
            {
                if (!File.Exists(MonitoringWindowBoundsPath)) return;

                var json = File.ReadAllText(MonitoringWindowBoundsPath);
                var bounds = JsonSerializer.Deserialize<MonitoringWindowBounds>(json);
                if (bounds == null || bounds.Width <= 0 || bounds.Height <= 0) return;

                _monitoringWindowLocation = new Point(bounds.X, bounds.Y);
                _monitoringWindowSize = new Size(bounds.Width, bounds.Height);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load monitoring window bounds");
            }
        }

        private static void SaveMonitoringWindowBounds(PhotinoWindow window)
        {
            try
            {
                var location = window.Location;
                var size = window.Size;
                if (size.Width <= 0 || size.Height <= 0) return;

                _monitoringWindowLocation = location;
                _monitoringWindowSize = size;

                var bounds = new MonitoringWindowBounds
                {
                    X = location.X,
                    Y = location.Y,
                    Width = size.Width,
                    Height = size.Height,
                };

                Directory.CreateDirectory(Path.GetDirectoryName(MonitoringWindowBoundsPath)!);
                File.WriteAllText(
                    MonitoringWindowBoundsPath,
                    JsonSerializer.Serialize(bounds, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save monitoring window bounds");
            }
        }

        private sealed class MonitoringWindowBounds
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        private static string BuildMonitoringWindowUrl(string baseUrl)
        {
            string withoutHash = baseUrl.Contains('#') ? baseUrl.Split('#')[0] : baseUrl;
            string separator = withoutHash.Contains('?') ? "&" : "?";
            return $"{withoutHash}{separator}window=monitoring#/monitoring";
        }

        public static void ShowMonitoringWindow()
        {
            if (Window == null || appUrl == null)
            {
                Log.Warning("ShowMonitoringWindow skipped: main window or appUrl is null");
                return;
            }

            try
            {
                Window.Invoke(() => ShowMonitoringWindowOnUiThread());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ShowMonitoringWindow Invoke failed; trying direct UI thread call");
                ShowMonitoringWindowOnUiThread();
            }
        }

        private static PhotinoWindow CreateMonitoringPhotinoWindow(string monitoringUrl, bool chromeless)
        {
            LoadMonitoringWindowBounds();

            var defaultSize = new Size(336, 348);
            var size = _monitoringWindowSize ?? defaultSize;

            var windowBuilder = new PhotinoWindow(Window)
                .SetBrowserControlInitParameters(MonitoringBrowserInitParameters)
                .SetNotificationsEnabled(false)
                // Chromeless on Windows requires explicit size AND location (not OS defaults).
                .SetUseOsDefaultSize(false)
                .SetUseOsDefaultLocation(false)
                .SetIconFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"))
                .SetSize(size)
                .SetResizable(true)
                .SetTopMost(_monitoringWindowTopMost)
                .SetContextMenuEnabled(false)
                .RegisterWebMessageReceivedHandler((sender, message) =>
                {
                    _ = MessageService.HandleMessage(message);
                })
                .RegisterWindowClosingHandler((sender, eventArgs) =>
                {
                    if (MonitoringWindow != null)
                    {
                        SaveMonitoringWindowBounds((PhotinoWindow)sender);
                        MonitoringWindow = null;
                        _ = MessageService.SendFrontendMessage("MonitoringWindowState", new { open = false });
                    }
                    return false;
                });

            if (_monitoringWindowLocation.HasValue)
                windowBuilder = windowBuilder.SetLocation(_monitoringWindowLocation.Value);
            else
                windowBuilder = windowBuilder.Center();

            var window = windowBuilder;

            if (chromeless)
                window = window.SetChromeless(true).SetTransparent(true);

            return window.Load(monitoringUrl);
        }

        public static void SetMonitoringWindowTopMost(bool enabled)
        {
            _monitoringWindowTopMost = enabled;
            if (MonitoringWindow == null || Window == null) return;

            try
            {
                Window.Invoke(() => MonitoringWindow?.SetTopMost(enabled));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SetMonitoringWindowTopMost failed");
            }
        }

        private static void ShowMonitoringWindowOnUiThread()
        {
            try
            {
                if (Window == null || appUrl == null) return;

                if (MonitoringWindow != null)
                {
                    MonitoringWindow.SetMinimized(false);
                    if (_monitoringWindowTopMost)
                        MonitoringWindow.SetTopMost(true);
                    return;
                }

                string monitoringUrl = BuildMonitoringWindowUrl(appUrl);
                Log.Information("Opening monitoring window at {MonitoringUrl}", monitoringUrl);

                try
                {
                    MonitoringWindow = CreateMonitoringPhotinoWindow(monitoringUrl, chromeless: true);
                    MonitoringWindow.SetTitle("Segra 監控");
                    MonitoringWindow.WaitForClose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Chromeless monitoring window failed; falling back to framed window");
                    MonitoringWindow = CreateMonitoringPhotinoWindow(monitoringUrl, chromeless: false);
                    MonitoringWindow.SetTitle("Segra 監控");
                    MonitoringWindow.WaitForClose();
                }

                _ = MessageService.SendFrontendMessage("MonitoringWindowState", new { open = true });
                _ = Task.Run(async () =>
                {
                    await Task.Delay(400);
                    await MessageService.SendSettingsToFrontend("Monitoring window opened");
                    await MessageService.SendGameList();
                });
                Log.Information("Monitoring window opened");
            }
            catch (Exception ex)
            {
                MonitoringWindow = null;
                Log.Error(ex, "Error showing monitoring window");
            }
        }

        /// <summary>
        /// Win32 title-bar drag for chromeless monitoring window (WebView2 CSS drag regions are unreliable in Photino).
        /// </summary>
        public static void BeginMonitoringWindowDrag()
        {
            if (MonitoringWindow == null || Window == null) return;

            void StartDrag()
            {
                try
                {
                    IntPtr hwnd = MonitoringWindow!.WindowHandle;
                    if (hwnd == IntPtr.Zero) return;

                    ReleaseCapture();
                    SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "BeginMonitoringWindowDrag failed");
                }
            }

            try
            {
                Window.Invoke(StartDrag);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BeginMonitoringWindowDrag Invoke failed; trying direct");
                StartDrag();
            }
        }

        public static void CloseMonitoringWindow()
        {
            if (Window == null)
            {
                MonitoringWindow?.Close();
                MonitoringWindow = null;
                return;
            }

            Window.Invoke(CloseMonitoringWindowOnUiThread);
        }

        private static void CloseMonitoringWindowOnUiThread()
        {
            try
            {
                if (MonitoringWindow == null) return;

                var window = MonitoringWindow;
                MonitoringWindow = null;
                SaveMonitoringWindowBounds(window);
                window.Close();
                _ = MessageService.SendFrontendMessage("MonitoringWindowState", new { open = false });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error closing monitoring window");
            }
        }

        private static async Task ShowApplicationWindow()
        {
            Log.Information("Showing application window. Window is " + (Window == null ? "null" : "not null"));
            if (Window == null)
            {
                // Schedule the foreground operations with a delay before calling LoadFrontend
                _ = Task.Run(async () =>
                {
                    await Task.Delay(200);
                    Log.Information("Bringing application window to foreground from scheduled task");
                    if (Window != null)
                    {
                        Window.SetMinimized(false);
                        Window.SetTopMost(true);
                        await Task.Delay(200);
                        Window.SetTopMost(false);
                        Log.Information("Application window brought to foreground");
                    }
                });

                LoadFrontend();
            }
            else
            {
                Log.Information("Bringing application window to foreground. Window is not null");
                Window.SetMinimized(false);
                Window.SetTopMost(true);
                await Task.Delay(200);
                Window.SetTopMost(false);
                Log.Information("Application window brought to foreground");
            }
        }

        private static void HideApplicationWindow()
        {
            Window?.SetMinimized(true);

            IntPtr hWnd = Process.GetCurrentProcess().MainWindowHandle;
            ShowWindow(hWnd, SW_HIDE); // Hides the window from the taskbar

            Log.Information("Application window hidden");
        }

        private static void LoadFrontend()
        {
            Log.Information("Loading frontend, app url is " + appUrl);
            // Initialize the PhotinoWindow
            Window = new PhotinoWindow()
                .SetBrowserControlInitParameters("--enable-blink-features=AudioVideoTracks")
                .SetNotificationsEnabled(false) // Disabled due to it creating a second start menu entry with incorrect start path. See https://github.com/tryphotino/photino.NET/issues/85
                .SetUseOsDefaultSize(false)
                .SetIconFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"))
                .SetSize(new Size(1280, 720))
                .Center()
                .SetResizable(true)
                .RegisterWebMessageReceivedHandler((sender, message) =>
                {
                    Window = (PhotinoWindow)sender!;
                    _ = MessageService.HandleMessage(message);
                })
                .Load(appUrl);

            Log.Information("Window variable has been set");

            // intentional space after name because of https://github.com/tryphotino/photino.NET/issues/106
            Window.SetTitle("Segra ");

            Window.RegisterWindowClosingHandler((sender, eventArgs) =>
            {
                HideApplicationWindow();
                return true;
            });

            Window.WaitForClose();
        }

        private static void StartNamedPipeServer()
        {
            pipeServerThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.In))
                        {
                            pipeServer.WaitForConnection();

                            using (var reader = new StreamReader(pipeServer))
                            {
                                string? message = reader.ReadLine();
                                if (message == "SHOW_WINDOW")
                                {
                                    if (Window != null)
                                    {
                                        Window.SetMinimized(false);
                                        Window.SetTopMost(true);
                                        Thread.Sleep(200);
                                        Window.SetTopMost(false);
                                        Log.Information("Window brought to foreground directly from pipe server");
                                    }
                                    else
                                    {
                                        // Only signal the main thread to create the window if it doesn't exist
                                        ShowWindowEvent.Set();
                                        Log.Information("ShowWindowEvent set");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (Log.Logger != null)
                        {
                            Log.Error(ex, "Error in named pipe server");
                        }
                        else
                        {
                            Console.WriteLine($"Error in named pipe server: {ex.Message}");
                        }

                        Thread.Sleep(1000);
                    }
                }
            });

            pipeServerThread.IsBackground = true;
            pipeServerThread.Start();
        }

        // Check if the application was launched from startup
        private static bool IsLaunchedFromStartup()
        {
            return Environment.GetCommandLineArgs().Contains("--from-startup");
        }

        private static void AddNotifyIcon()
        {
            var trayThread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var icon = new NotifyIcon())
                {
                    icon.Icon = Properties.Resources.icon;
                    icon.Text = "Segra";
                    icon.Visible = true;

                    var menu = new ContextMenuStrip();
                    menu.Items.Add("Open", null, async (s, e) => await ShowApplicationWindow());
                    menu.Items.Add("Exit", null, (s, e) =>
                    {
                        Shutdown();
                        Environment.Exit(0);
                    });
                    icon.ContextMenuStrip = menu;

                    icon.MouseDoubleClick += async (s, e) =>
                    {
                        if (e.Button == MouseButtons.Left)
                            await ShowApplicationWindow();
                    };

                    NotifyIconService.Initialize(icon);

                    Application.Run();
                }
            });
            trayThread.SetApartmentState(ApartmentState.STA);
            trayThread.IsBackground = true;
            trayThread.Start();
        }

        private static string GetSolutionPath()
        {
            string currentDirectory = Directory.GetCurrentDirectory();

            string directory = currentDirectory;
            while (!string.IsNullOrEmpty(directory) && !Directory.GetFiles(directory, "*.sln").Any())
            {
                directory = Directory.GetParent(directory)?.FullName!;
            }

            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Solution path could not be found. Ensure you are running this application within a solution directory.");
            }

            return directory;
        }
    }
}
