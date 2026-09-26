using Serilog;
using Velopack;
using Photino.NET;
using System.IO.Pipes;
using Segra.Backend.Api;
using Photino.NET.Server;
using Segra.Backend.Core;
using System.Diagnostics;
using System.Drawing;
using Segra.Backend.Shared;
using Segra.Backend.Platform;
using Segra.Backend.Recorder;
using Segra.Backend.Core.Models;
using Segra.Backend.Windows.Storage;
using System.Reflection;
using System.Runtime.InteropServices;
#if WINDOWS
using Segra.Backend.Windows.Power;
using Segra.Backend.Windows.GameMode;
using Segra.Backend.Windows.WebView2;
#endif

namespace Segra.Backend.App
{
    partial class Program
    {
#if WINDOWS
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        const int SW_HIDE = 0;
        const int SW_SHOWNOACTIVATE = 4;
        const int SW_SHOW = 5;
        const int SW_RESTORE = 9;
        const int SM_CXFULLSCREEN = 16;
        const int SM_CYFULLSCREEN = 17;
        const int GWLP_HWNDPARENT = -8;
        const uint MONITOR_DEFAULTTONEAREST = 2;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint SWP_SHOWWINDOW = 0x0040;
        static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
#endif

#if WINDOWS
        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private const int SW_SHOWMAXIMIZED = 3;
#endif

        public static bool IsFirstRun { get; private set; } = false;
        private static readonly AutoResetEvent ShowWindowEvent = new(false);
        public static bool hasLoadedInitialSettings = false;
        public static PhotinoWindow? Window { get; private set; }

        private static readonly string LogFilePath =
          Segra.Backend.Shared.PathUtils.Normalize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra", "logs.log"));
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
            PlatformServices.Initialize();

#if WINDOWS
            // Set process DPI aware to ensure we capture at physical resolution
            SetProcessDPIAware();
#else
            // Re-exec once with LD_LIBRARY_PATH set so libobs is loadable (never returns on first launch).
            Segra.Backend.Platform.Linux.LinuxObsRuntime.ConfigureAndReexecIfNeeded();
#endif

            // Pin the working directory to the app directory so relative-path lookups
            // (OBS modules, bundled ffmpeg.exe) resolve regardless of how Segra was launched.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

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

                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to communicate with existing instance: {ex.Message}");
                }
            }

            StartNamedPipeServer();

            var logDirectory = Path.GetDirectoryName(LogFilePath);
            if (logDirectory != null && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            ConfigureLogging();

            VelopackApp.Build()
                .OnBeforeUpdateFastCallback((v) =>
                {
                    if (UpdateService.UpdateManager == null)
                    {
                        Log.Error("UpdateManager is null");
                        return;
                    }
                    var currentVersion = UpdateService.UpdateManager.CurrentVersion;
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

#if WINDOWS
                WebView2RuntimeService.LogRuntimeVersion();
#endif

                // VS Code sets SEGRA_VSCODE=1 via launch.json; Visual Studio does not.
                // In VS Code the Vite dev server runs separately, so PhotinoServer is not needed
                // and its RunAsync() would otherwise open a spurious browser tab.
                bool IsVSCodeDebug = Environment.GetEnvironmentVariable("SEGRA_VSCODE") == "1";
                bool IsDebugMode = Debugger.IsAttached;

                string baseUrl = string.Empty;
                if (!IsVSCodeDebug)
                {
                    PhotinoServer
                        .CreateStaticFileServer(args, startPort: 44040, portRange: 100, webRootFolder: "wwwroot", out baseUrl)
                        .RunAsync();
                }

                // Version + assembly timestamp: Photino's static server sends no cache headers,
                // and WebView2 keys its disk cache on the URL. Assembly version stays 1.0.0 on
                // local rebuilds, so ?v=1.0.0 kept serving a stale index.html (and the old JS
                // hash it pointed at). A new exe timestamp busts that cache every publish.
                string? appVersion = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                string cacheBust = GetFrontendCacheBustToken(appVersion);
                appUrl = IsDebugMode ? "http://localhost:2882" : $"{baseUrl}/index.html?v={Uri.EscapeDataString(cacheBust)}";

                if (IsDebugMode)
                {
                    Task.Run(() =>
                    {
                        var startInfo = new ProcessStartInfo
                        {
#if WINDOWS
                            FileName = "cmd.exe",
                            Arguments = "/c npm run dev",
#else
                            FileName = "npm",
                            Arguments = "run dev",
#endif
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

                Log.Information("Serving React app at {AppUrl}", appUrl);

                Task.Run(() =>
                {
                    ContentServer.StartServer(ContentServer.Prefix);
                });

                IsFirstRun = !SettingsService.LoadSettings();
                hasLoadedInitialSettings = true;
                AppState.Instance.Initialize();
                SettingsService.SaveSettings();
                if (IsFirstRun)
                {
                    _ = SettingsService.LoadContentFromFolderIntoState(true);
                    PlatformServices.Startup.SetStartupStatus(true);
                    Settings.Instance.DisableWindowsGameMode = true;
                    AppState.Instance.GpuVendor = GeneralUtils.DetectGpuVendor();
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
#if WINDOWS
                // Native in-app audio playback (Windows only; makes Discord/OBS app-audio
                // capture of the Segra window work). The frontend falls back to webview-rendered
                // audio when this endpoint is absent (e.g. on Linux).
                Task.Run(AudioStreamServer.StartAsync);
#endif
                Task.Run(StorageService.EnsureStorageBelowLimit);

                // Check for updates
                Task.Run(() => UpdateService.UpdateAppIfNecessary(forceCheck: true));

                // Check if application was launched from startup. Only minimize to tray when the
                // user has chosen the Minimized startup window mode; otherwise open normally.
                bool startMinimized = IsLaunchedFromStartup() &&
                    Settings.Instance.StartupWindowMode == StartupWindowMode.Minimized;
                Log.Information($"Starting application{(startMinimized ? " minimized from startup" : "")}");

                // Tray icon (WinForms NotifyIcon on Windows; no-op on Linux)
                PlatformServices.Tray.Initialize(
                    onOpen: () => _ = ShowApplicationWindow(),
                    onExit: () => { Shutdown(); Environment.Exit(0); });

#if WINDOWS
                // Start monitoring system power state changes (sleep/wake)
                Task.Run(PowerModeMonitor.StartMonitoring);

                // Ensure Windows Game Mode is off when the user has opted in (no-op otherwise)
                Task.Run(GameModeService.EnforceDisabledIfEnabled);

                // Run the OBS Initializer in a separate thread and application to make sure someting on the main thread doesn't block
                // (KeybindCaptureService.Start() is called from OBSService.InitializeAsync once OBS is
                // ready, since hotkeys register through OBS's own hotkey system.)
                // OBSWindow hosts the Win32 message pump the graphics-hook game_capture needs.
                Task.Run(() => Application.Run(new OBSWindow()));
#else
                // Linux libobs runs headless (no message pump needed); initialize OBS directly.
                Task.Run(() => OBSService.InitializeAsync());
#endif

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

#if WINDOWS
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
#endif

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
#if WINDOWS
                    try
                    {
                        var hwnd = Window.WindowHandle;
                        if (hwnd != IntPtr.Zero)
                            ShowWindow(hwnd, SW_RESTORE);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Win32 restore before fullscreen failed.");
                    }
#endif
                    // If still maximized, Win32 fullscreen can behave like bounded maximize (taskbar stays visible).
                    if (Window.Maximized)
                        Window.SetMaximized(false);
                    // Native OS fullscreen fills the monitor; maximize alone only fills the work area.
                    Window.SetFullScreen(true);
#if WINDOWS
                    ApplyTrueFullscreenBounds(Window);
                    // Photino may ignore the first size/style change while the frame is still maximized.
                    Window.SetFullScreen(true);
                    ApplyTrueFullscreenBounds(Window);
#endif
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

#if WINDOWS
        private static void ApplyTrueFullscreenBounds(PhotinoWindow window)
        {
            try
            {
                var hwnd = window.WindowHandle;
                if (hwnd == IntPtr.Zero) return;

                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return;

                var info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf<MONITORINFO>();
                if (!GetMonitorInfo(monitor, ref info)) return;

                int width = info.rcMonitor.Right - info.rcMonitor.Left;
                int height = info.rcMonitor.Bottom - info.rcMonitor.Top;
                if (width <= 0 || height <= 0) return;

                SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    width,
                    height,
                    SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ApplyTrueFullscreenBounds failed.");
            }
        }
#endif

        private static string GetFrontendCacheBustToken(string? appVersion)
        {
            string version = string.IsNullOrEmpty(appVersion) ? "0" : appVersion;
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    return $"{version}.{new FileInfo(location).LastWriteTimeUtc.Ticks}";
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not read assembly timestamp for frontend cache bust");
            }

            return version;
        }

        private static void Shutdown()
        {
            Log.Information("Application shutting down.");

            SaveWindowState();

            // Stop any active recording first so OBS finalizes the file cleanly. Task.Run + block keeps
            // the awaits off the tray thread, whose WinForms SynchronizationContext would otherwise deadlock.
            if (AppState.Instance.Recording != null || AppState.Instance.PreRecording != null)
            {
                Log.Information("Active recording detected during shutdown; stopping it before exit.");
                try
                {
                    Task.Run(() => OBSService.StopRecording()).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error stopping recording during shutdown");
                }
            }

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

        private static async Task BringWindowToForegroundAsync()
        {
            if (Window == null)
                return;

            Window.Invoke(() =>
            {
                Window.SetMinimized(false);
                Window.SetTopMost(true);
            });
            await Task.Delay(200);
            Window.Invoke(() => Window.SetTopMost(false));
            FocusApplicationWindow();
            Log.Information("Application window brought to foreground");
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
                    await BringWindowToForegroundAsync();
                });

                LoadFrontend();
            }
            else
            {
                Log.Information("Bringing application window to foreground. Window is not null");
                await BringWindowToForegroundAsync();
            }
        }

        public static void BringWindowToFront() => _ = ShowApplicationWindow();

        // SetForegroundWindow only works for the process owning the foreground, so borrow its input queue.
        private static void FocusApplicationWindow()
        {
#if WINDOWS
            try
            {
                IntPtr hWnd = GetMainWindowHandle();
                if (hWnd == IntPtr.Zero)
                    return;

                IntPtr foreground = GetForegroundWindow();
                if (foreground == hWnd)
                    return;

                ShowWindow(hWnd, SW_RESTORE);

                uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
                uint currentThread = GetCurrentThreadId();
                bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                    AttachThreadInput(currentThread, foregroundThread, true);

                SetForegroundWindow(hWnd);

                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);

                Log.Information("Application window focused");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not focus the application window");
            }
#endif
        }

        private static IntPtr GetMainWindowHandle()
        {
            try
            {
                return Window?.WindowHandle ?? IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static void HideApplicationWindow()
        {
#if WINDOWS
            // Photino SetMinimized on the parent can also minimize the owned PiP.
            IntPtr hWnd = GetMainWindowHandle();
            if (hWnd != IntPtr.Zero)
                ShowWindow(hWnd, SW_HIDE);

            PresentMonitoringWindow(activate: false);

            if (TryGetMonitoringWindowHandle(out IntPtr pip) && !IsWindowVisible(pip))
            {
                DetachMonitoringWindowOwner();
                PresentMonitoringWindow(activate: false);
            }
#else
            Window?.SetMinimized(true);
#endif

            Log.Information("Application window hidden");
        }

        private static void LoadFrontend()
        {
            Log.Information("Loading frontend, app url is " + appUrl);

#if WINDOWS
            // Photino sizes windows in physical pixels, so scale the default size by the
            // OS display scale (e.g. 150% on 4K monitors) and clamp it to the usable screen area
            double displayScale = GetDpiForSystem() / 96.0;
            var windowSize = new Size(
                Math.Min((int)(1280 * displayScale), GetSystemMetrics(SM_CXFULLSCREEN)),
                Math.Min((int)(720 * displayScale), GetSystemMetrics(SM_CYFULLSCREEN)));
            string iconFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
#else
            // WebKitGTK handles DPI scaling itself; use a sensible default size.
            var windowSize = new Size(1280, 720);
            string iconFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png");
#endif

            bool hasRestoredLocation = TryGetRestoredWindowLocation(out Point restoredLocation);

            // Initialize the PhotinoWindow
            var windowBuilder = new PhotinoWindow();
#if WINDOWS
            // Chromium/WebView2-only flag; WebKitGTK on Linux parses this natively and crashes on the
            // leading "--", so it must only be set on Windows.
            windowBuilder = windowBuilder.SetBrowserControlInitParameters("--enable-blink-features=AudioVideoTracks --disable-http-cache");
#endif
            windowBuilder = windowBuilder
                .SetNotificationsEnabled(false) // Disabled due to it creating a second start menu entry with incorrect start path. See https://github.com/tryphotino/photino.NET/issues/85
                .SetUseOsDefaultSize(false)
                .SetIconFile(iconFile)
                .SetSize(windowSize)
                .SetResizable(true);

            // Restore the window to the monitor it was last on instead of always centering on the primary display.
            // UseOsDefaultLocation defaults to true, so it must be explicitly disabled or the native window
            // ignores SetLocation and falls back to the OS default position (same reasoning as SetUseOsDefaultSize above).
            windowBuilder = hasRestoredLocation
                ? windowBuilder.SetUseOsDefaultLocation(false).SetLocation(restoredLocation)
                : windowBuilder.Center();

            Window = windowBuilder
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
                if (Settings.Instance.CloseButtonAction == CloseButtonAction.Exit)
                {
                    Shutdown();
                    Environment.Exit(0);
                    return false;
                }

                SaveWindowState();
                HideApplicationWindow();
                return true;
            });

            Window.WaitForClose();
        }

        // Validates the saved location still lands on a currently connected monitor
        // (e.g. the second monitor wasn't unplugged), falling back to centering otherwise.
        private static bool TryGetRestoredWindowLocation(out Point location)
        {
            var saved = Settings.Instance.LastWindowState;
            if (saved != null)
            {
                var savedLocation = new Point(saved.X, saved.Y);
#if WINDOWS
                // Only restore if the saved location still lands on a connected monitor.
                if (Screen.AllScreens.Any(screen => screen.Bounds.Contains(savedLocation)))
                {
                    location = savedLocation;
                    return true;
                }
#else
                // No cross-platform multi-monitor bounds query; trust the saved location.
                location = savedLocation;
                return true;
#endif
            }

            location = default;
            return false;
        }

        private static void SaveWindowState()
        {
            if (Window == null || Window.Minimized) return;

            try
            {
                Settings.Instance.LastWindowState = new WindowState
                {
                    X = Window.Location.X,
                    Y = Window.Location.Y
                };

                SettingsService.SaveSettings();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving window state");
            }
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
                                        _ = ShowApplicationWindow();
                                        Log.Information("ShowApplicationWindow from pipe server");
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
