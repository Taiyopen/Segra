using Serilog;
using Photino.NET;
using System.Runtime.InteropServices;

namespace Segra.Backend.App
{
    // Custom title bar for the main window: the frontend draws it, so the native caption is removed while the
    // resize borders, shadow and snapping stay native. Window buttons send the same system commands as the native ones.
    partial class Program
    {
#if WINDOWS
        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

        [DllImport("user32.dll", EntryPoint = "PostMessageW")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int GWLP_WNDPROC = -4;
        private const uint WM_SIZE = 0x0005;
        private const uint WM_NCCALCSIZE = 0x0083;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;
        private const int SC_MAXIMIZE = 0xF030;
        private const int SC_RESTORE = 0xF120;
        private const int SC_CLOSE = 0xF060;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int SM_CYFRAME = 33;
        private const int SM_CXPADDEDBORDER = 92;
        private const int SIZE_RESTORED = 0;
        private const int SIZE_MAXIMIZED = 2;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;

        // Held in a field so the GC never frees the delegate native code calls into
        private static WndProc? _mainWindowProc;
        private static IntPtr _mainWindowPrevProc;
        private static bool _mainWindowMaximized;

        /// <summary>Removes the native caption from the main window. Call once its HWND exists.</summary>
        private static void ApplyCustomTitleBar(PhotinoWindow window)
        {
            try
            {
                IntPtr hwnd = window.WindowHandle;
                if (hwnd == IntPtr.Zero || _mainWindowProc != null) return;

                _mainWindowProc = MainWindowProc;
                _mainWindowPrevProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_mainWindowProc));
                _mainWindowMaximized = IsZoomed(hwnd);

                // Recompute the frame so the caption disappears right away
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply the custom title bar");
            }
        }

        private static IntPtr MainWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
            {
                // Let Windows lay out the side and bottom borders, then hand the caption area to the page
                int originalTop = Marshal.PtrToStructure<RECT>(lParam).Top;
                IntPtr result = CallWindowProc(_mainWindowPrevProc, hWnd, msg, wParam, lParam);
                var client = Marshal.PtrToStructure<RECT>(lParam);
                client.Top = originalTop;

                // A maximized window hangs its frame past the screen edge; keep the page below that
                if (IsZoomed(hWnd))
                {
                    uint dpi = GetDpiForWindow(hWnd);
                    client.Top += GetSystemMetricsForDpi(SM_CYFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
                }

                Marshal.StructureToPtr(client, lParam, false);
                return result;
            }

            if (msg == WM_SIZE)
            {
                int kind = (int)wParam;
                if (kind == SIZE_MAXIMIZED || kind == SIZE_RESTORED)
                {
                    bool maximized = kind == SIZE_MAXIMIZED;
                    if (maximized != _mainWindowMaximized)
                    {
                        _mainWindowMaximized = maximized;
                        _ = SendMainWindowState();
                    }
                }
            }

            return CallWindowProc(_mainWindowPrevProc, hWnd, msg, wParam, lParam);
        }
#endif

        private static Task SendMainWindowState()
        {
#if WINDOWS
            // customTitleBar tells the page to draw its own bar only once the native caption is really gone
            return MessageService.SendFrontendMessage("MainWindowState", new
            {
                customTitleBar = _mainWindowProc != null,
                maximized = _mainWindowMaximized,
            });
#else
            return Task.CompletedTask;
#endif
        }

        /// <summary>Title bar actions from the frontend: drag, resize from the top edge, window buttons, state sync.</summary>
        public static void HandleMainWindowCommand(string action)
        {
#if WINDOWS
            if (Window == null) return;

            void Run()
            {
                IntPtr hwnd = GetMainWindowHandle();
                if (hwnd == IntPtr.Zero) return;

                switch (action)
                {
                    case "drag":
                        ReleaseCapture();
                        SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                        break;
                    case "resizeTop":
                    case "resizeTopLeft":
                    case "resizeTopRight":
                        int hit = action == "resizeTop" ? HTTOP : action == "resizeTopLeft" ? HTTOPLEFT : HTTOPRIGHT;
                        ReleaseCapture();
                        SendMessage(hwnd, WM_NCLBUTTONDOWN, hit, 0);
                        break;
                    case "minimize":
                        PostMessage(hwnd, WM_SYSCOMMAND, SC_MINIMIZE, IntPtr.Zero);
                        break;
                    case "toggleMaximize":
                        PostMessage(hwnd, WM_SYSCOMMAND, IsZoomed(hwnd) ? SC_RESTORE : SC_MAXIMIZE, IntPtr.Zero);
                        break;
                    case "close":
                        PostMessage(hwnd, WM_SYSCOMMAND, SC_CLOSE, IntPtr.Zero);
                        break;
                    case "sync":
                        _ = SendMainWindowState();
                        break;
                    default:
                        Log.Warning("Unknown main window command: {Action}", action);
                        break;
                }
            }

            try
            {
                Window.Invoke(Run);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Main window command {Action} failed", action);
            }
#endif
        }
    }
}
