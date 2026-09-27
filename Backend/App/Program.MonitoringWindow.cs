using Serilog;
using Photino.NET;
using System.Drawing;
using System.Text.Json;
using System.Runtime.InteropServices;
using Segra.Backend.Recorder;
#if WINDOWS
using Segra.Backend.Windows.Display;
#endif

namespace Segra.Backend.App
{
    // The monitoring PiP window: a second Photino window that shows the live recording preview.
    partial class Program
    {
        /// <summary>WebView2 init args for the monitoring PiP window.</summary>
        private const string MonitoringBrowserInitParameters =
            "--enable-blink-features=AudioVideoTracks --disable-http-cache";
        public static PhotinoWindow? MonitoringWindow { get; private set; }
        public static bool IsMonitoringWindowOpen => MonitoringWindow != null;
        private static bool _monitoringWindowTopMost = true;
        private static Point? _monitoringWindowLocation;
        private static Size? _monitoringWindowSize;
        private static readonly string MonitoringWindowBoundsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Segra",
            "monitoring-window-bounds.json");

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

        /// <summary>
        /// True when enough of the window intersects a connected monitor to remain usable
        /// (avoids restoring onto an unplugged display).
        /// </summary>
        private static bool IsWindowBoundsOnScreen(Point location, Size size)
        {
#if WINDOWS
            try
            {
                int w = Math.Max(size.Width, 64);
                int h = Math.Max(size.Height, 32);
                var windowRect = new Rectangle(location.X, location.Y, w, h);

                foreach (var screen in Screen.AllScreens)
                {
                    var hit = Rectangle.Intersect(screen.WorkingArea, windowRect);
                    if (hit.Width >= 64 && hit.Height >= 32)
                        return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to check monitoring window screen bounds");
            }

            return false;
#else
            return true;
#endif
        }

        private static Point GetCenteredMonitoringLocation(Size size)
        {
#if WINDOWS
            try
            {
                var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
                if (screen != null)
                {
                    var wa = screen.WorkingArea;
                    return new Point(
                        wa.Left + Math.Max(0, (wa.Width - size.Width) / 2),
                        wa.Top + Math.Max(0, (wa.Height - size.Height) / 2));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to center monitoring window on primary screen");
            }
#endif
            if (Window != null)
            {
                try
                {
                    var main = Window.Location;
                    return new Point(main.X + 48, main.Y + 48);
                }
                catch
                {
                    // ignore
                }
            }

            return new Point(100, 100);
        }

        private static Size GetScaledMonitoringDefaultSize()
        {
            const int baseWidth = 336;
            const int baseHeight = 400;
            const int referenceHeight = 1440;
#if WINDOWS
            try
            {
                if (DisplayService.GetPrimaryMonitorPhysicalResolution(out uint _, out uint height) && height > 0)
                {
                    double scale = Math.Clamp((double)height / referenceHeight, 0.75, 2.0);
                    return new Size(
                        Math.Max(1, (int)Math.Round(baseWidth * scale)),
                        Math.Max(1, (int)Math.Round(baseHeight * scale)));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to scale monitoring window for display resolution.");
            }
#endif
            return new Size(baseWidth, baseHeight);
        }

        private static Size ResolveMonitoringWindowSize()
        {
            var defaultSize = GetScaledMonitoringDefaultSize();
            var legacyDefault = new Size(336, 400);

            if (!_monitoringWindowSize.HasValue)
                return defaultSize;

            var saved = _monitoringWindowSize.Value;
            if (saved.Width == legacyDefault.Width && saved.Height == legacyDefault.Height &&
                (defaultSize.Width != legacyDefault.Width || defaultSize.Height != legacyDefault.Height))
            {
                return defaultSize;
            }

            if (saved.Height < defaultSize.Height)
                return new Size(Math.Max(saved.Width, defaultSize.Width), defaultSize.Height);

            return saved;
        }

        private static void EnsureMonitoringWindowVisible(PhotinoWindow window)
        {
            var location = window.Location;
            var size = window.Size;
            if (size.Width <= 0 || size.Height <= 0)
                size = GetScaledMonitoringDefaultSize();

            if (IsWindowBoundsOnScreen(location, size))
                return;

            var safe = GetCenteredMonitoringLocation(size);
            Log.Information(
                "Monitoring window was off-screen at {X},{Y}; moving to {NewX},{NewY}",
                location.X, location.Y, safe.X, safe.Y);
            window.SetLocation(safe);
            _monitoringWindowLocation = safe;
            _monitoringWindowSize = size;
            SaveMonitoringWindowBounds(window);
        }

        private static void FocusMonitoringWindow()
        {
#if WINDOWS
            try
            {
                if (MonitoringWindow == null) return;
                IntPtr hWnd = MonitoringWindow.WindowHandle;
                if (hWnd == IntPtr.Zero) return;

                ShowWindow(hWnd, SW_RESTORE);

                IntPtr foreground = GetForegroundWindow();
                uint foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
                uint currentThread = GetCurrentThreadId();
                bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                    AttachThreadInput(currentThread, foregroundThread, true);

                SetForegroundWindow(hWnd);

                if (attached)
                    AttachThreadInput(currentThread, foregroundThread, false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not focus monitoring window");
            }
#endif
        }

        private static string BuildMonitoringWindowUrl(string appUrl)
        {
            // Use a dedicated monitoring.html entry. Photino/WebView2 has been observed to drop
            // query/hash from Load(url), which caused the PiP window to boot the full main app.
            // Release appUrl: http://localhost:44040/index.html?v=1.0.0
            // Debug appUrl:   http://localhost:2882
            if (Uri.TryCreate(appUrl, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Authority}/monitoring.html{uri.Query}";
            }

            string withoutHash = appUrl.Contains('#') ? appUrl.Split('#')[0] : appUrl;
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

            var size = ResolveMonitoringWindowSize();

            Point location = GetPipLocationOnMainMonitor(size);
            if (_monitoringWindowLocation.HasValue &&
                (_monitoringWindowLocation.Value.X != location.X || _monitoringWindowLocation.Value.Y != location.Y))
            {
                Log.Information(
                    "Monitoring window position {X},{Y} is not on the main window's screen; using {NewX},{NewY}",
                    _monitoringWindowLocation.Value.X, _monitoringWindowLocation.Value.Y, location.X, location.Y);
            }

            var windowBuilder = new PhotinoWindow(Window);
#if WINDOWS
            windowBuilder = windowBuilder.SetBrowserControlInitParameters(MonitoringBrowserInitParameters);
#endif
            windowBuilder = windowBuilder
                .SetNotificationsEnabled(false)
                // Chromeless on Windows requires explicit size AND location (not OS defaults).
                .SetUseOsDefaultSize(false)
                .SetUseOsDefaultLocation(false)
                .SetIconFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
#if WINDOWS
                    "icon.ico"
#else
                    "icon.png"
#endif
                    ))
                .SetSize(size)
                .SetLocation(location)
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
                        NotifyMonitoringWindowClosed();
                    }
                    return false;
                });

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
#if WINDOWS
                    PresentMonitoringWindow(activate: true);
#else
                    EnsureMonitoringWindowVisible(MonitoringWindow);
                    if (_monitoringWindowTopMost)
                        MonitoringWindow.SetTopMost(true);
                    FocusMonitoringWindow();
#endif
                    NotifyMonitoringWindowOpened();
                    return;
                }

                string monitoringUrl = BuildMonitoringWindowUrl(appUrl);
                Log.Information("Opening monitoring window at {MonitoringUrl}", monitoringUrl);

                try
                {
                    MonitoringWindow = CreateMonitoringPhotinoWindow(monitoringUrl, chromeless: true);
                    MonitoringWindow.SetTitle("Segra \u76e3\u63a7");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Chromeless monitoring window failed; falling back to framed window");
                    MonitoringWindow = CreateMonitoringPhotinoWindow(monitoringUrl, chromeless: false);
                    MonitoringWindow.SetTitle("Segra \u76e3\u63a7");
                }

                NotifyMonitoringWindowOpened();
#if WINDOWS
                SchedulePresentMonitoringWindow(activate: true);
#endif
                MonitoringWindow.WaitForClose();
            }
            catch (Exception ex)
            {
                MonitoringWindow = null;
                Log.Error(ex, "Error showing monitoring window");
            }
        }

        private static void NotifyMonitoringWindowOpened()
        {
            _ = MessageService.SendFrontendMessage("MonitoringWindowState", new { open = true });
            OBSService.SyncAlwaysOnPreview();
            _ = Task.Run(async () =>
            {
                await Task.Delay(400);
                await MessageService.SendSettingsToFrontend("Monitoring window opened");
                await MessageService.SendGameList();
            });
            Log.Information("Monitoring window opened");
        }

        private static void NotifyMonitoringWindowClosed()
        {
            _ = MessageService.SendFrontendMessage("MonitoringWindowState", new { open = false });
            OBSService.SyncAlwaysOnPreview();
        }

        /// <summary>
        /// Win32 title-bar drag for chromeless monitoring window (WebView2 CSS drag regions are unreliable in Photino).
        /// </summary>
        public static void BeginMonitoringWindowDrag()
        {
            if (MonitoringWindow == null || Window == null) return;

#if WINDOWS
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
#endif
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
                NotifyMonitoringWindowClosed();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error closing monitoring window");
            }
        }

        private static bool TryGetMonitoringWindowHandle(out IntPtr hwnd)
        {
            hwnd = IntPtr.Zero;
            try
            {
                if (MonitoringWindow == null) return false;
                hwnd = MonitoringWindow.WindowHandle;
                return hwnd != IntPtr.Zero;
            }
            catch (ApplicationException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetMainMonitorWorkArea(out Rectangle work)
        {
            work = default;
#if WINDOWS
            try
            {
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd == IntPtr.Zero) return false;

                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return false;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref info)) return false;

                work = Rectangle.FromLTRB(
                    info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
                return work.Width > 0 && work.Height > 0;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to get main window monitor work area");
            }
#endif
            return false;
        }

        private static bool IsLocationUsableOnWorkArea(Point location, Size size, Rectangle work)
        {
            int w = Math.Max(size.Width, 64);
            int h = Math.Max(size.Height, 32);
            var hit = Rectangle.Intersect(work, new Rectangle(location.X, location.Y, w, h));
            return hit.Width >= 64 && hit.Height >= 32;
        }

        private static Point GetPipLocationOnMainMonitor(Size size)
        {
#if WINDOWS
            if (TryGetMainMonitorWorkArea(out var work))
            {
                if (_monitoringWindowLocation.HasValue &&
                    IsLocationUsableOnWorkArea(_monitoringWindowLocation.Value, size, work))
                {
                    return _monitoringWindowLocation.Value;
                }

                int x = Math.Max(work.Left, work.Right - size.Width - 16);
                int y = Math.Max(work.Top, work.Bottom - size.Height - 16);
                return new Point(x, y);
            }
#endif
            return GetCenteredMonitoringLocation(size);
        }

        private static void PresentMonitoringWindow(bool activate)
        {
#if WINDOWS
            try
            {
                if (!TryGetMonitoringWindowHandle(out IntPtr hwnd))
                    return;

                var size = MonitoringWindow!.Size;
                if (size.Width <= 0 || size.Height <= 0)
                    size = GetScaledMonitoringDefaultSize();

                var location = GetPipLocationOnMainMonitor(size);

                ShowWindow(hwnd, SW_RESTORE);
                ShowWindow(hwnd, activate ? SW_SHOW : SW_SHOWNOACTIVATE);

                SetWindowPos(
                    hwnd,
                    _monitoringWindowTopMost ? HWND_TOPMOST : IntPtr.Zero,
                    location.X,
                    location.Y,
                    size.Width,
                    size.Height,
                    SWP_SHOWWINDOW | (activate ? 0u : SWP_NOACTIVATE));

                try
                {
                    MonitoringWindow.SetLocation(location);
                    MonitoringWindow.SetSize(size);
                    MonitoringWindow.SetMinimized(false);
                    if (_monitoringWindowTopMost)
                        MonitoringWindow.SetTopMost(true);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Photino bounds sync after present failed");
                }

                _monitoringWindowLocation = location;
                _monitoringWindowSize = size;

                if (activate)
                    FocusMonitoringWindow();

                Log.Information(
                    "Presented monitoring window at {X},{Y} {Width}x{Height}",
                    location.X, location.Y, size.Width, size.Height);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to present monitoring window");
            }
#endif
        }

        private static void SchedulePresentMonitoringWindow(bool activate)
        {
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 40; i++)
                {
                    await Task.Delay(50);
                    if (MonitoringWindow == null) return;
                    if (!TryGetMonitoringWindowHandle(out _)) continue;

                    try
                    {
                        Window?.Invoke(() => PresentMonitoringWindow(activate));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "PresentMonitoringWindow invoke failed");
                    }
                    return;
                }

                Log.Warning("Monitoring window HWND was not ready to present");
            });
        }

        /// <summary>
        /// Owned windows are hidden with their owner. Detach only if hide left the PiP invisible.
        /// </summary>
        private static void DetachMonitoringWindowOwner()
        {
#if WINDOWS
            try
            {
                if (!TryGetMonitoringWindowHandle(out IntPtr hwnd)) return;

                IntPtr owner = GetWindowLongPtr(hwnd, GWLP_HWNDPARENT);
                if (owner == IntPtr.Zero) return;

                SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, IntPtr.Zero);
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to detach monitoring window owner");
            }
#endif
        }
    }
}
