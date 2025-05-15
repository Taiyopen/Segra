using NuGet.Versioning;
using Photino.NET;
using Photino.NET.Server;
using Segra.Backend.ContentServer;
using Segra.Backend.Models;
using Segra.Backend.Services;
using Segra.Backend.Utils;
using Serilog;
using System.Diagnostics;
using System.IO.Pipes;
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
        private const string PipeName = "Segra_SingleInstance";
        private static Mutex singleInstanceMutex;
        private static Thread pipeServerThread;

        [STAThread]
        static void Main(string[] args)
        {
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
                .WithFirstRun((v) =>
                {
                    Log.Information($"First run of Segra {v}");
                    StartupUtils.SetStartupStatus(true);
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

                bool firstRun = !SettingsUtils.LoadSettings();
                hasLoadedInitialSettings = true;
                Settings.Instance.State.Initialize();
                SettingsUtils.SaveSettings();
                if (firstRun)
                {
                    SettingsUtils.LoadContentFromFolderIntoState(true);
                }


                // Try to login with stored credentials
                Task.Run(async () => await AuthService.TryAutoLogin());

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

                // Check if application was launched from startup
                bool startMinimized = IsLaunchedFromStartup();
                Log.Information($"Starting application{(startMinimized ? " minimized from startup" : "")}");

                // If started from startup, minimize the window and show in system tray
                if (startMinimized)
                {
                    window.Minimized = true;
                    // Add a small delay to ensure the window is created before hiding it
                    Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        HideApplicationWindow();
                    });
                }

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

                // Release the mutex when closing
                if (singleInstanceMutex != null)
                {
                    singleInstanceMutex.ReleaseMutex();
                    singleInstanceMutex.Dispose();
                }
            }
        }

        private static async Task ShowApplicationWindow()
        {
            if (notifyIcon != null)
                notifyIcon.Visible = false;

            if (window != null)
            {
                window.Minimized = false;

                window.SetTopMost(true);
                await Task.Delay(200);
                window.SetTopMost(false);

                Log.Information("Application window brought to foreground");
            }
        }

        private static void HideApplicationWindow()
        {
            if (window != null)
                window.Minimized = true; // Minimize first


            if (notifyIcon != null)
                notifyIcon.Visible = true;

            IntPtr hWnd = Process.GetCurrentProcess().MainWindowHandle;
            ShowWindow(hWnd, SW_HIDE); // Hides the window from the taskbar

            Log.Information("Application window hidden");
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
                                string message = reader.ReadLine();
                                if (message == "SHOW_WINDOW")
                                {
                                    Log.Information("Received message to show application window from another instance.");
                                    Task.Run(async () => await ShowApplicationWindow()).Wait();
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
            notifyIcon = new NotifyIcon
            {
                Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico")),
                Visible = true,
                Text = "Segra"
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open", null, async (sender, e) =>
            {
                await ShowApplicationWindow();
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
                    await ShowApplicationWindow();
                }
            };

            window.RegisterWindowClosingHandler((sender, eventArgs) =>
            {
                HideApplicationWindow();
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
