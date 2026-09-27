using Serilog;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using NAudio.CoreAudioApi;
using Segra.Backend.Auth;
using Segra.Backend.Core.Models;
using Segra.Backend.Media;
using Segra.Backend.Recorder;
using Segra.Backend.Services;
using Segra.Backend.Shared;
using Segra.Backend.Windows.Audio;

namespace Segra.Backend.Api
{
    internal class ContentServer
    {
        internal const string Prefix = "http://localhost:2222/";

        private static readonly HttpListener _httpListener = new();
        private static CancellationTokenSource? _cancellationTokenSource;

        public static void StartServer(string prefix)
        {
            _httpListener.Prefixes.Add(prefix);
            _httpListener.Start();
            Log.Information("Server started at {Prefix}", prefix);

            _cancellationTokenSource = new();
            _ = Task.Run(() => AcceptRequestsAsync(_cancellationTokenSource.Token));
        }

        public static void StopServer()
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _httpListener.Stop();
                _httpListener.Close();
                Log.Information("ContentServer stopped");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping ContentServer");
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private static async Task AcceptRequestsAsync(CancellationToken cancellationToken)
        {
            Log.Information("ContentServer now accepting requests");

            while (!cancellationToken.IsCancellationRequested && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    _ = ProcessRequestAsync(context);
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                {
                    Log.Information("ContentServer listener stopped");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    Log.Information("ContentServer listener disposed");
                    break;
                }

                catch (Exception ex)
                {
                    Log.Error(ex, "Error accepting request");
                }
            }

            Log.Information("ContentServer stopped accepting requests");
        }

        private static async Task ProcessRequestAsync(HttpListenerContext context)
        {
            var response = context.Response;

            try
            {
                var rawUrl = context.Request.RawUrl ?? "";
                var path = context.Request.Url?.AbsolutePath ?? "";

                if (rawUrl.StartsWith("/api/thumbnail"))
                {
                    await HandleThumbnailRequest(context);
                }
                else if (rawUrl.StartsWith("/api/recording-preview"))
                {
                    await HandleRecordingPreviewRequest(context);
                }
                else if (rawUrl.StartsWith("/api/recording-audio-levels"))
                {
                    await HandleRecordingAudioLevelsRequest(context);
                }
                else if (rawUrl.StartsWith("/api/audio-mixer"))
                {
                    await HandleAudioMixerRequest(context);
                }
                else if (rawUrl.StartsWith("/api/content"))
                {
                    await HandleContentRequest(context);
                }
                else if (path.StartsWith(ControlApi.PathPrefix))
                {
                    await ControlApi.HandleRequest(context);
                }
                else if (DiscordLoginService.IsCallbackPath(path))
                {
                    await DiscordLoginService.HandleCallbackAsync(context);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.ContentType = "text/plain";
                    using (var writer = new StreamWriter(response.OutputStream))
                    {
                        await writer.WriteAsync("Invalid endpoint.");
                    }
                }
            }
            catch (HttpListenerException)
            {
            }
            catch (Exception ex)
            {
                // Path only: auth callback query strings carry session tokens.
                Log.Error(ex, "Error processing request for {Path}", context.Request.Url?.AbsolutePath);
                try
                {
                    if (!response.OutputStream.CanWrite)
                        return;

                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.ContentType = "text/plain";
                    using (var writer = new StreamWriter(response.OutputStream))
                    {
                        await writer.WriteAsync("Internal server error");
                    }
                }
                catch
                {
                }
            }
            finally
            {
                try
                {
                    response.Close();
                }
                catch
                {
                }
            }
        }

        private static async Task HandleRecordingPreviewRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            response.AddHeader("Access-Control-Allow-Origin", "*");

            if (request.HttpMethod == "OPTIONS")
            {
                response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            if (request.HttpMethod != "GET")
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            int slot = 0;
            var slotQuery = request.QueryString["slot"];
            if (!string.IsNullOrEmpty(slotQuery) && int.TryParse(slotQuery, out int parsedSlot))
                slot = parsedSlot;

            byte[]? jpeg = OBSService.TryGetRecordingPreviewJpeg(slot);
            if (jpeg == null || jpeg.Length == 0)
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.ContentType = "text/plain";
                using (var writer = new StreamWriter(response.OutputStream))
                {
                    await writer.WriteAsync("Preview not available.");
                }
                return;
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "image/jpeg";
            response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
            response.ContentLength64 = jpeg.Length;
            await response.OutputStream.WriteAsync(jpeg, 0, jpeg.Length);
        }

        private static async Task HandleRecordingAudioLevelsRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            response.AddHeader("Access-Control-Allow-Origin", "*");

            if (request.HttpMethod == "OPTIONS")
            {
                response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            if (request.HttpMethod != "GET")
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            var inputTracks = (Settings.Instance.InputDevices ?? [])
                .Where(d => !string.IsNullOrEmpty(d.Id))
                .Select((d, index) => new
                {
                    index,
                    id = d.Id,
                    name = d.Name,
                    peak = AudioDeviceService.GetEndpointPeak(d.Id, DataFlow.Capture),
                })
                .ToList();

            var outputTracks = (Settings.Instance.OutputDevices ?? [])
                .Where(d => !string.IsNullOrEmpty(d.Id))
                .Select((d, index) => new
                {
                    index,
                    id = d.Id,
                    name = d.Name,
                    peak = AudioDeviceService.GetEndpointPeak(d.Id, DataFlow.Render),
                })
                .ToList();

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { inputTracks, outputTracks });
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body, 0, body.Length);
        }

        /// <summary>Live level, mute and volume of every OBS audio source being recorded (PiP mixer).</summary>
        private static async Task HandleAudioMixerRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            response.AddHeader("Access-Control-Allow-Origin", "*");

            if (request.HttpMethod == "OPTIONS")
            {
                response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            if (request.HttpMethod != "GET")
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            var sources = AudioMixerService.GetSnapshot().Select(s => new
            {
                id = s.Id,
                name = s.Name,
                kind = s.Kind,
                peakDb = MathF.Round(s.PeakDb, 1),
                muted = s.Muted,
                volume = s.Volume,
            });

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { sources });
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body, 0, body.Length);
        }

        private static async Task HandleThumbnailRequest(HttpListenerContext context)
        {
            var query = HttpUtility.ParseQueryString(context.Request?.Url?.Query ?? "");
            string rawInput = query["input"] ?? "";
            string timeParam = query["time"] ?? "";
            var response = context.Response;

            response.AddHeader("Access-Control-Allow-Origin", "*");

            string? input = ValidateUserPath(rawInput);
            if (input == null || !File.Exists(input))
            {
                Log.Warning("Thumbnail request file not found or invalid: {Input}", rawInput);
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.ContentType = "text/plain";
                using (var writer = new StreamWriter(response.OutputStream))
                {
                    await writer.WriteAsync("File not found.");
                }
                return;
            }

            if (string.IsNullOrEmpty(timeParam))
            {
                response.ContentType = "image/jpeg";
                response.AddHeader("Cache-Control", "public, max-age=86400");
                response.AddHeader("Expires", DateTime.UtcNow.AddDays(7).ToString("R"));

                try
                {
                    var lastModified = File.GetLastWriteTimeUtc(input);
                    response.AddHeader("Last-Modified", lastModified.ToString("R"));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not get last modified time for {Input}", input);
                }

                using (var fs = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
                {
                    response.ContentLength64 = fs.Length;
                    await fs.CopyToAsync(response.OutputStream);
                }
            }
            else
            {
                if (!double.TryParse(timeParam, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out double timeSeconds))
                {
                    Log.Warning("Could not parse timeParam={TimeParam}, using 0.0", timeParam);
                    timeSeconds = 0.0;
                }

                if (!FFmpegService.FFmpegExists())
                {
                    Log.Error("FFmpeg executable not found");
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.ContentType = "text/plain";
                    using (var writer = new StreamWriter(response.OutputStream))
                    {
                        await writer.WriteAsync("FFmpeg not found on server.");
                    }
                    return;
                }

                byte[] jpegBytes = await FFmpegService.GenerateThumbnail(input, timeSeconds);

                if (jpegBytes != null && jpegBytes.Length > 0)
                {
                    response.ContentType = "image/jpeg";
                    response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
                    response.AddHeader("Pragma", "no-cache");
                    response.AddHeader("Expires", "0");
                    response.ContentLength64 = jpegBytes.Length;
                    await response.OutputStream.WriteAsync(jpegBytes, 0, jpegBytes.Length);
                }
                else
                {
                    Log.Error("No thumbnail data received from FFmpeg");
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.ContentType = "text/plain";
                    using (var writer = new StreamWriter(response.OutputStream))
                    {
                        await writer.WriteAsync("Failed to generate thumbnail.");
                    }
                }
            }
        }

        private static async Task HandleContentRequest(HttpListenerContext context)
        {
            var query = HttpUtility.ParseQueryString(context.Request?.Url?.Query ?? "");
            string rawInput = query["input"] ?? "";
            var response = context.Response;

            response.AddHeader("Access-Control-Allow-Origin", "*");

            string? fileName = ValidateUserPath(rawInput);
            if (fileName == null || !File.Exists(fileName))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.ContentType = "text/plain";
                using (var writer = new StreamWriter(response.OutputStream))
                {
                    await writer.WriteAsync("File not found.");
                }
                return;
            }

            string ext = Path.GetExtension(fileName);
            if (IsJsonExtension(ext))
            {
                await StreamJsonFile(fileName, response);
                return;
            }

            if (!IsVideoExtension(ext))
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                response.ContentType = "text/plain";
                using (var writer = new StreamWriter(response.OutputStream))
                {
                    await writer.WriteAsync("Unsupported file type.");
                }
                return;
            }

            try
            {
                string playablePath = await EnsureBrowserPlayableAsync(fileName);
                string mime = GetVideoMimeType(Path.GetExtension(playablePath));
                await StreamVideoFile(playablePath, context, mime);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to prepare video for playback: {Path}", fileName);
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                response.ContentType = "text/plain";
                using (var writer = new StreamWriter(response.OutputStream))
                {
                    await writer.WriteAsync("Failed to prepare video for playback.");
                }
            }
        }

        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RemuxLocks = new(StringComparer.OrdinalIgnoreCase);

        private static bool IsJsonExtension(string ext) =>
            ext.Equals(".json", StringComparison.OrdinalIgnoreCase);

        private static bool IsVideoExtension(string ext) =>
            ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".avi", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Formats the HTML5 video element can play directly in WebView2/Chromium.
        /// </summary>
        private static bool IsBrowserNativeVideo(string ext) =>
            ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase);

        private static string GetVideoMimeType(string ext)
        {
            if (ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
                return "video/webm";
            if (ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
                return "video/x-matroska";
            return "video/mp4";
        }

        /// <summary>
        /// Returns a path the browser can play. MOV/AVI are remuxed (or lightly
        /// re-encoded for audio) into a cached MP4 under the Segra cache folder.
        /// MKV is served as-is; waveform / multi-track features may fail.
        /// </summary>
        private static async Task<string> EnsureBrowserPlayableAsync(string sourcePath)
        {
            string ext = Path.GetExtension(sourcePath);
            if (IsBrowserNativeVideo(ext))
                return sourcePath;

            var info = new FileInfo(sourcePath);
            string key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();

            string remuxDir = Path.Combine(FolderNames.CacheFolder, "browse-remux");
            Directory.CreateDirectory(remuxDir);
            string outPath = Path.Combine(remuxDir, $"{hash}.mp4");

            if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                return outPath;

            var gate = RemuxLocks.GetOrAdd(hash, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
                    return outPath;

                string tempPath = outPath + ".partial";
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);

                    Log.Information("Remuxing browse video for playback: {Source} -> {Dest}", sourcePath, outPath);

                    try
                    {
                        // Fast path: container remux only (works for H.264/AAC in MKV, etc.)
                        await FFmpegService.RunSimple(
                        [
                            "-y",
                            "-i", sourcePath,
                            "-map", "0:v:0",
                            "-map", "0:a:0?",
                            "-c", "copy",
                            "-movflags", "+faststart",
                            tempPath
                        ]);
                    }
                    catch (Exception copyEx)
                    {
                        Log.Warning(copyEx, "Stream-copy remux failed; retrying with AAC audio for {Source}", sourcePath);
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);

                        // Fallback: keep video bitstream, re-encode audio to AAC for MP4
                        await FFmpegService.RunSimple(
                        [
                            "-y",
                            "-i", sourcePath,
                            "-map", "0:v:0",
                            "-map", "0:a:0?",
                            "-c:v", "copy",
                            "-c:a", "aac",
                            "-b:a", "192k",
                            "-movflags", "+faststart",
                            tempPath
                        ]);
                    }

                    File.Move(tempPath, outPath, overwrite: true);
                    Log.Information("Browse remux ready: {Dest}", outPath);
                    return outPath;
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
                    throw;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task StreamVideoFile(string fileName, HttpListenerContext context, string contentType = "video/mp4")
        {
            var response = context.Response;

            string rangeHeader = context.Request.Headers["Range"] ?? "";
            long start = 0;
            long end;

            using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 262144,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                long fileLength = fs.Length;
                end = fileLength - 1;

                if (!string.IsNullOrEmpty(rangeHeader) && rangeHeader.StartsWith("bytes="))
                {
                    string[] rangeParts = rangeHeader.Substring(6).Split('-');
                    if (rangeParts.Length > 0 && !string.IsNullOrEmpty(rangeParts[0]))
                    {
                        long.TryParse(rangeParts[0], out start);
                    }
                    if (rangeParts.Length > 1 && !string.IsNullOrEmpty(rangeParts[1]))
                    {
                        long.TryParse(rangeParts[1], out end);
                    }
                }

                if (start > end || start < 0 || end >= fileLength)
                {
                    response.StatusCode = (int)HttpStatusCode.RequestedRangeNotSatisfiable;
                    response.AddHeader("Content-Range", $"bytes */{fileLength}");
                    return;
                }

                long contentLength = end - start + 1;

                response.StatusCode = string.IsNullOrEmpty(rangeHeader) ? (int)HttpStatusCode.OK : (int)HttpStatusCode.PartialContent;
                response.ContentType = contentType;
                response.AddHeader("Accept-Ranges", "bytes");
                // Content-Range is not on the CORS response-header safelist, so the
                // browser hides it from fetch() unless we explicitly expose it.
                // The frontend reads it to determine the full file size from a small
                // probe request (useAudioTracks.ts).
                response.AddHeader("Access-Control-Expose-Headers", "Content-Range, Accept-Ranges");

                if (!string.IsNullOrEmpty(rangeHeader))
                {
                    response.AddHeader("Content-Range", $"bytes {start}-{end}/{fileLength}");
                }

                response.ContentLength64 = contentLength;

                if (start > 0)
                {
                    fs.Seek(start, SeekOrigin.Begin);
                }

                byte[] buffer = new byte[262144];
                long bytesRemaining = contentLength;

                while (bytesRemaining > 0)
                {
                    int bytesToRead = (int)Math.Min(buffer.Length, bytesRemaining);
                    int bytesRead = await fs.ReadAsync(buffer, 0, bytesToRead);

                    if (bytesRead == 0)
                        break;

                    await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                    bytesRemaining -= bytesRead;
                }
            }
        }

        private static async Task StreamJsonFile(string fileName, HttpListenerResponse response)
        {
            var fileInfo = new FileInfo(fileName);

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.AddHeader("Accept-Ranges", "bytes");
            response.ContentLength64 = fileInfo.Length;

            using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true))
            {
                await fs.CopyToAsync(response.OutputStream);
            }
        }

        private static string? ValidateUserPath(string userPath)
        {
            if (string.IsNullOrWhiteSpace(userPath))
                return null;

            string? canonical = TryGetFullPath(userPath);
            if (canonical == null)
                return null;

            var allowedRoots = new[]
            {
                Settings.Instance.ContentFolder,
                FolderNames.CacheFolder
            };

            foreach (var root in allowedRoots)
            {
                if (string.IsNullOrEmpty(root))
                    continue;

                string? rootCanonical = TryGetFullPath(root);
                if (rootCanonical == null)
                    continue;

                if (!rootCanonical.EndsWith(Path.DirectorySeparatorChar) &&
                    !rootCanonical.EndsWith(Path.AltDirectorySeparatorChar))
                {
                    rootCanonical += Path.DirectorySeparatorChar;
                }

                if (canonical.StartsWith(rootCanonical, StringComparison.OrdinalIgnoreCase))
                    return canonical;
            }

            // Allow videos under folders the user has opened in the file browser
            if (BrowseService.IsPathAuthorized(canonical))
                return canonical;

            // Recordings made before the recording path changed, and imported videos, sit
            // outside both roots but are still in the library. Allow those exact files so they
            // stay playable; matching the whole path rather than a prefix keeps this from
            // exposing the rest of the folder they happen to live in.
            if (IsTrackedContentFile(canonical))
                return canonical;

            return null;
        }

        private static bool IsTrackedContentFile(string canonical)
        {
            return AppState.Instance.Content.Any(c =>
                !string.IsNullOrEmpty(c.FilePath) &&
                string.Equals(TryGetFullPath(c.FilePath), canonical, StringComparison.OrdinalIgnoreCase));
        }

        private static string? TryGetFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
