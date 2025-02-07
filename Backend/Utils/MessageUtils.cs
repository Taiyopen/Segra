using System.Net.WebSockets;
using System.Net;
using System.Text;
using System.Text.Json;

using Segra.Models;
using Serilog;
using System.Net.Http.Headers;
using System.Diagnostics;

namespace Segra.Backend.Utils
{
    public static class MessageUtils
    {
        private static WebSocket activeWebSocket;
        private static readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };


        public static async Task HandleMessage(string message)
        {
            Log.Information("WEBSOCKET: " + message);
            if (string.IsNullOrEmpty(message))
            {
                Log.Information("Received empty message.");
                return;
            }

            try
            {
                var jsonDoc = JsonDocument.Parse(message);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("Method", out JsonElement methodElement))
                {
                    string method = methodElement.GetString();

                    switch (method)
                    {
                        case "CreateClip":
                            root.TryGetProperty("Parameters", out JsonElement clipParameterElement);
                            await HandleCreateClip(clipParameterElement);
                            break;
                        case "DeleteContent":
                            root.TryGetProperty("Parameters", out JsonElement deleteContentParameterElement);
                            await HandleDeleteContent(deleteContentParameterElement);
                            break;
                        case "UploadContent":
                            root.TryGetProperty("Parameters", out JsonElement uploadContentParameterElement);
                            await HandleUploadContent(uploadContentParameterElement);
                            break;
                        case "OpenFileLocation":
                            root.TryGetProperty("Parameters", out JsonElement openFileLocationParameterElement);
                            openFileLocationParameterElement.TryGetProperty("FilePath", out JsonElement filePathElement);
                            Process.Start("explorer.exe", $"/select,\"{filePathElement.ToString().Replace("/", "\\")}\"");
                            break;
                        case "StartRecording":
                            await Task.Run(() =>
                            {
                                OBSUtils.StartRecording();
                                Log.Information("StartRecording command received.");
                            });
                            break;
                        case "StopRecording":
                            await Task.Run(() =>
                            {
                                OBSUtils.StopRecording();
                                Log.Information("StopRecording command received.");
                            });
                            break;
                        case "GetSettings":
                            Log.Information("GetSettings command received.");
                            await SendSettingsToFrontend();
                            break;
                        case "SetVideoLocation":
                            await SetVideoLocationAsync();
                            Log.Information("SetVideoLocation command received.");
                            break;
                        case "UpdateSettings":
                            root.TryGetProperty("Parameters", out JsonElement settingsParameterElement);
                            SettingsUtils.HandleUpdateSettings(settingsParameterElement);
                            Log.Information("UpdateSettings command received.");
                            break;
                        // Handle other methods if needed
                        default:
                            Log.Information($"Unknown method: {method}");
                            break;
                    }
                }
                else
                {
                    Log.Information("Method property not found in message.");
                }
            }
            catch (JsonException ex)
            {
                Log.Error($"Failed to parse message as JSON: {ex.Message}");
            }
        }

        private static async Task HandleCreateClip(JsonElement message)
        {
            Log.Information($"{message}");

            if (message.TryGetProperty("Selections", out JsonElement selectionsElement))
            {
                var selections = new List<Selection>();
                foreach (var selectionElement in selectionsElement.EnumerateArray())
                {
                    if (selectionElement.TryGetProperty("startTime", out JsonElement startTimeElement) &&
                        selectionElement.TryGetProperty("endTime", out JsonElement endTimeElement) &&
                        selectionElement.TryGetProperty("fileName", out JsonElement fileNameElement) &&
                        selectionElement.TryGetProperty("type", out JsonElement videoTypeElement))
                    {
                        double startTime = startTimeElement.GetDouble();
                        double endTime = endTimeElement.GetDouble();
                        string fileName = fileNameElement.GetString();
                        string type = videoTypeElement.GetString();

                        string? game = null;
                        if (selectionElement.TryGetProperty("game", out JsonElement gameElement))
                        {
                            game = gameElement.GetString();
                        }

                        // Create a new Selection instance with all required properties.
                        selections.Add(new Selection
                        {
                            Type = type,
                            StartTime = startTime,
                            EndTime = endTime,
                            FileName = fileName,
                            Game = game
                        });
                    }
                }

                await ClipUtils.CreateClips(selections);
            }
            else
            {
                Log.Information("Selections property not found in CreateClip message.");
            }
        }

        private static async Task HandleUploadContent(JsonElement message)
        {
            try
            {
                string filePath = message.GetProperty("FilePath").GetString();
                string jwt = message.GetProperty("JWT").GetString();
                string fileName = Path.GetFileName(filePath);

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                using var httpClient = new HttpClient();
                using var formData = new MultipartFormDataContent();

                void ProgressHandler(long sent, long total)
                {
                    int progress = (int)((sent / (double)total) * 100);
                    _ = SendFrontendMessage("UploadProgress", new
                    {
                        fileName,
                        progress,
                        status = "uploading",
                        message = $"Uploading... {progress}%"
                    });
                }

                var fileContent = new ProgressableStreamContent(fileBytes, "application/octet-stream", ProgressHandler);
                formData.Add(fileContent, "file", fileName);

                AddOptionalContent(formData, message, "Game");
                AddOptionalContent(formData, message, "Title");
                AddOptionalContent(formData, message, "Description");

                await SendFrontendMessage("UploadProgress", new
                {
                    fileName,
                    progress = 0,
                    status = "uploading",
                    message = "Starting upload..."
                });

                var request = new HttpRequestMessage(HttpMethod.Post, "https://upload.segra.tv")
                {
                    Content = formData
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                await SendFrontendMessage("UploadProgress", new
                {
                    fileName,
                    progress = 100,
                    status = "done",
                    message = "Upload completed successfully"
                });

                Log.Information($"Upload success: {await response.Content.ReadAsStringAsync()}");
            }
            catch (Exception ex)
            {
                Log.Error($"Upload failed: {ex.Message}");
                await SendFrontendMessage("UploadProgress", new
                {
                    fileName = message.GetProperty("filePath").GetString(),
                    progress = 0,
                    status = "error",
                    message = ex.Message
                });
            }
        }

        private static void AddOptionalContent(MultipartFormDataContent formData, JsonElement message, string field)
        {
            if (message.TryGetProperty(field, out JsonElement element))
            {
                formData.Add(new StringContent(element.GetString()), field.ToLower());
            }
        }

        private static async Task HandleDeleteContent(JsonElement message)
        {
            Log.Information($"Handling DeleteContent with message: {message}");

            // Extract FileName and ContentType
            if (message.TryGetProperty("FileName", out JsonElement fileNameElement) &&
                message.TryGetProperty("ContentType", out JsonElement contentTypeElement))
            {
                string fileName = fileNameElement.GetString();
                string contentTypeStr = contentTypeElement.GetString();

                if (Enum.TryParse(contentTypeStr, true, out Content.ContentType contentType))
                {
                    // Construct the full file path based on the file name and content type
                    string videoFolder = Settings.Instance.ContentFolder;
                    string contentTypeFolder = Path.Combine(videoFolder, contentType.ToString().ToLower() + "s");
                    string filePath = Path.Combine(contentTypeFolder, $"{fileName}.mp4"); // Assuming .mp4 extension

                    // Invoke the deletion asynchronously
                    await Task.Run(() => ContentUtils.DeleteContent(filePath, contentType));
                }
                else
                {
                    Log.Error($"Invalid ContentType provided: {contentTypeStr}");
                }
            }
            else
            {
                Log.Information("FileName or ContentType property not found in DeleteContent message.");
            }
        }

        // Existing methods...

        private static async Task SetVideoLocationAsync()
        {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                // Set an initial description or instruction for the dialog
                fbd.Description = "Select a folder to set as the video location.";

                // Optionally, set the root folder for the dialog (e.g., My Computer or Desktop)
                fbd.RootFolder = Environment.SpecialFolder.Desktop;

                // Show the dialog and check if the user selected a folder
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    // Get the selected folder path
                    string selectedPath = fbd.SelectedPath;
                    Log.Information($"Selected Folder: {selectedPath}");

                    // Update settings with the selected folder path
                    Settings.Instance.ContentFolder = selectedPath;
                }
                else
                {
                    Log.Information("Folder selection was canceled.");
                }
            }
        }

        public static async Task StartWebsocket()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5000/");
            listener.Start();
            Log.Information("WebSocket server started at ws://localhost:5000/");

            try
            {
                while (true)
                {
                    HttpListenerContext context = await listener.GetContextAsync();

                    if (context.Request.IsWebSocketRequest)
                    {
                        Log.Information("Received WebSocket connection request");

                        // Close the current WebSocket if already active
                        if (activeWebSocket != null && activeWebSocket.State == WebSocketState.Open)
                        {
                            await activeWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "New connection", CancellationToken.None);
                            Log.Information("Closed previous WebSocket connection.");
                        }

                        HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
                        activeWebSocket = wsContext.WebSocket;

                        Log.Information("WebSocket connection established");
                        await HandleWebSocketAsync(activeWebSocket);
                    }
                    else
                    {
                        Log.Information("Invalid request: Not a WebSocket request");
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"Exception in StartWebsocket: {ex.Message}");
                Log.Information(ex.StackTrace);
            }
        }

        private static async Task HandleWebSocketAsync(WebSocket webSocket)
        {
            byte[] buffer = new byte[4096];
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log.Information("Client initiated WebSocket closure.");
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client initiated closure", CancellationToken.None);
                    }
                    else
                    {
                        string receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        Log.Information($"Received message: {receivedMessage}");
                        await HandleMessage(receivedMessage);
                    }
                }
            }
            catch (WebSocketException wsEx)
            {
                Log.Information($"WebSocketException in HandleWebSocketAsync: {wsEx.Message}");
                Log.Information($"WebSocket state at exception: {webSocket.State}");
                if (wsEx.InnerException != null)
                {
                    Log.Information($"Inner exception: {wsEx.InnerException.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Information($"General exception in HandleWebSocketAsync: {ex.Message}");
            }
            finally
            {
                if (webSocket.State != WebSocketState.Closed && webSocket.State != WebSocketState.Aborted)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Server-side error", CancellationToken.None);
                }
                Log.Information("WebSocket connection closed.");
            }
        }

        public static async Task SendFrontendMessage(string method, object content)
        {
            await sendLock.WaitAsync();
            try
            {
                // Wait for up to 10 seconds for the websocket to be open
                int maxWaitTimeMs = 10000;
                int waitIntervalMs = 100;
                int elapsedTime = 0;

                while ((activeWebSocket == null || activeWebSocket.State != WebSocketState.Open)
                    && elapsedTime < maxWaitTimeMs)
                {
                    await Task.Delay(waitIntervalMs);
                    elapsedTime += waitIntervalMs;
                }

                if (activeWebSocket?.State == WebSocketState.Open)
                {
                    var message = new { method, content };
                    byte[] buffer = JsonSerializer.SerializeToUtf8Bytes(message, jsonOptions);
                    await activeWebSocket.SendAsync(
                        buffer,
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        cancellationToken: CancellationToken.None
                    );
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending message: {ex.Message}");
            }
            finally
            {
                sendLock.Release();
            }
        }

        public static async Task SendSettingsToFrontend()
        {
            if (!Program.hasLoadedInitialSettings)
                return;

            Log.Information("Sending state to frontend");
            await SendFrontendMessage("Settings", Settings.Instance);
        }
    }

    // Define the Selection class
    public class Selection
    {
        public string Type { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public string FileName { get; set; }
        public string? Game { get; set; }
    }
}

