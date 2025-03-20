using NuGet.Versioning;
using Photino.NET;
using Photino.NET.Server;
using Segra.Backend.ContentServer;
using Segra.Backend.Services;
using Segra.Backend.Utils;
using Segra.Models;
using Serilog;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using Velopack;

namespace Segra
{
    class Program
    {
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;

        public static bool hasLoadedInitialSettings = false;
        public static PhotinoWindow window { get; private set; }
        private static readonly string LogFilePath =
          Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra", "logs.log");
        private static NotifyIcon notifyIcon;

        [STAThread]
        static void Main(string[] args)
        {
            var logDirectory = Path.GetDirectoryName(LogFilePath);
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.Debug()
                .WriteTo.File(
                    path: LogFilePath,
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB per file
                    rollOnFileSizeLimit: true,            // Roll to a new file when size is reached
                    retainedFileCountLimit: 1             // Keep only the latest log file
                )
                .CreateLogger();

            // Get the current version
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            VelopackApp.Build()
                .WithBeforeUpdateFastCallback((v) =>
                {
                    SemanticVersion currentVersion = UpdateUtils.UpdateManager.CurrentVersion;
                    Log.Information($"Updating from version {currentVersion} to {v}");
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "segra.tmp"), currentVersion.ToString());
                })
                .WithAfterUpdateFastCallback((v) =>
                {
                    string previousVersionPath = Path.Combine(Path.GetTempPath(), "segra.tmp");
                    if (File.Exists(previousVersionPath))
                    {
                        string previousVersion = File.ReadAllText(previousVersionPath);
                        Log.Information($"Updated from version {previousVersion} to {v}");
                        Task.Run(async () =>
                        {
                            await Task.Delay(5000);
                            MessageUtils.SendFrontendMessage("ShowReleaseNotes", previousVersion);
                        });
                        File.Delete(previousVersionPath);
                    }
                })
                .Run();

            Task.Run(() =>
            {
                UpdateUtils.UpdateAppIfNecessary();
            });

            try
            {
                Log.Information("Application starting up...");

                // Set up the PhotinoServer
                PhotinoServer
                    .CreateStaticFileServer(args, out string baseUrl)
                    .RunAsync();

                bool IsDebugMode = Debugger.IsAttached;
                string appUrl = IsDebugMode ? "http://localhost:2882" : $"{baseUrl}/index.html";

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
                        Process process = null;

                        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://localhost:2882/index.html");
                        request.AllowAutoRedirect = false;
                        request.Method = "HEAD";

                        try
                        {
                            request.GetResponse();
                        }
                        catch (WebException)
                        {
                            process ??= Process.Start(startInfo);
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

                SettingsUtils.LoadSettings();
                hasLoadedInitialSettings = true;
                Settings.Instance.State.Initialize();
                SettingsUtils.SaveSettings();

                // Start WebSocket and Load Settings
                Task.Run(MessageUtils.StartWebsocket);
                Task.Run(StorageUtils.EnsureStorageBelowLimit);
                // Initialize the PhotinoWindow
                window = new PhotinoWindow()
                    .SetNotificationsEnabled(false) // Disabled due to it creating a second start menu entry with incorrect start path. See https://github.com/tryphotino/photino.NET/issues/85
                    .SetUseOsDefaultSize(false)
                    .SetIconFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico"))
                    .SetSize(new Size(1280, 720))
                    .Center()
                    .SetResizable(true)
                    .RegisterWebMessageReceivedHandler((sender, message) =>
                    {
                        window = (PhotinoWindow)sender;
                        MessageUtils.HandleMessage(message);
                    })
                    .Load(appUrl);

                AddNotifyIcon();
                GameDetectionService.ForegroundHook.Start();

                // intentional space after name because of https://github.com/tryphotino/photino.NET/issues/106
                window.SetTitle("Segra ");

                // Run the OBS Initializer in a separate thread and application to make sure someting on the main thread doesn't block
                Task.Run(() => Application.Run(new OBSWindow()));

                window.WaitForClose();
                GameDetectionService.ForegroundHook.Stop();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly.");
            }
            finally
            {
                Log.Information("Application shutting down.");
                Log.CloseAndFlush(); // Ensure all logs are written before the application exits
            }
        }

        private static void AddNotifyIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico")),
                Visible = true,
                Text = "Segra"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open", null, async (sender, e) =>
            {
                notifyIcon.Visible = false;
                window.Minimized = false;

                window.SetTopMost(true);
                await Task.Delay(200);
                window.SetTopMost(false);
            });

            contextMenu.Items.Add("Exit", null, (sender, e) =>
            {
                notifyIcon.Visible = false;
                Environment.Exit(0);
            });

            notifyIcon.ContextMenuStrip = contextMenu;

            notifyIcon.MouseDoubleClick += async (sender, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    notifyIcon.Visible = false;
                    window.Minimized = false;

                    window.SetTopMost(true);
                    await Task.Delay(200);
                    window.SetTopMost(false);
                }
            };

            window.RegisterWindowClosingHandler((sender, eventArgs) =>
            {
                window.Minimized = true; // Minimize first
                notifyIcon.Visible = true;

                IntPtr hWnd = Process.GetCurrentProcess().MainWindowHandle;
                ShowWindow(hWnd, SW_HIDE); // Hides the window from the taskbar

                return true;
            });
        }

        private static string GetSolutionPath()
        {
            string currentDirectory = Directory.GetCurrentDirectory();

            string directory = currentDirectory;
            while (!string.IsNullOrEmpty(directory) && !Directory.GetFiles(directory, "*.sln").Any())
            {
                directory = Directory.GetParent(directory)?.FullName;
            }

            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Solution path could not be found. Ensure you are running this application within a solution directory.");
            }

            return directory;
        }
    }
}
