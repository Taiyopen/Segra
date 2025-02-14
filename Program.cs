using Photino.NET;
using Photino.NET.Server;
using Segra.Backend.ContentServer;
using Segra.Backend.Services;
using Segra.Backend.Utils;
using Segra.Models;
using Serilog;
using System.Diagnostics;
using System.Net;
using Velopack;

namespace Segra
{
    class Program
    {
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

            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.Debug()
                .WriteTo.File(LogFilePath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            VelopackApp.Build().Run();
            
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

                // Initialize the PhotinoWindow
                window = new PhotinoWindow()
                    .SetUseOsDefaultSize(false)
                    .SetIconFile("icon.ico")
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
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Run(() => OBSUtils.InitializeAsync());
                        Log.Information("OBSUtils initialized successfully.");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to initialize OBSUtils.");
                    }
                });

                // intentional space after name because of https://github.com/tryphotino/photino.NET/issues/106
                window.SetTitle("Segra ");

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
                Icon = new Icon("icon.ico"),
                Visible = true,
                Text = "Segra"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (sender, e) =>
            {
                notifyIcon.Visible = false;
                window.Minimized = false;
            });
            contextMenu.Items.Add("Exit", null, (sender, e) =>
            {
                notifyIcon.Visible = false;
                Environment.Exit(0);
            });

            notifyIcon.ContextMenuStrip = contextMenu;

            notifyIcon.MouseDoubleClick += (sender, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    notifyIcon.Visible = false;
                    window.Minimized = false;
                }
            };

            window.RegisterWindowClosingHandler((sender, eventArgs) =>
            {
                // TODO (os) Hide instead of minimize
                window.Minimized = true;
                notifyIcon.Visible = true;

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
