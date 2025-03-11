using Segra.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Serilog;
using Segra.Backend.Utils;

namespace Segra.Backend.Services
{
    internal class AiService
    {
        // TODO (os): add a cancel button
        public static async Task AnalyzeVideo(string fileName)
        {
            try
            {
                Log.Information("Starting to analyze video: " + fileName);
                Content content = Settings.Instance.State.Content
                    .Where(x => x.FileName == fileName)
                    .FirstOrDefault();

                if (content == null)
                {
                    Log.Information("No content found matching fileName: " + fileName);
                    return;
                }

                List<Bookmark> bookmarks = content.Bookmarks;

                if (bookmarks.Count == 0)
                {
                    Log.Information("No bookmarks found for video: " + fileName);
                    return;
                }

                var payload = new
                {
                    filename = content.FileName,
                    game = content.Game
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                using var jsonContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, "https://processing.segra.tv/ai/new")
                {
                    Content = jsonContent
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AuthService.GetJwtAsync());

                using var client = new HttpClient();
                HttpResponseMessage response;
                try
                {
                    response = client.Send(request);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to send the request to the AI service.");
                    return;
                }

                string responseBody;
                try
                {
                    responseBody = response.Content.ReadAsStringAsync().Result.Trim();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to read the AI service response content.");
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning($"AI service returned non-success status code: {response.StatusCode} - Body: {responseBody}");
                    return;
                }

                string analysisId;
                try
                {
                    var responseJson = JsonDocument.Parse(responseBody);
                    analysisId = responseJson.RootElement.GetProperty("analysis_id").GetString();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to parse 'analysis_id' from the AI service response.");
                    return;
                }

                Log.Information("Analysis id: " + analysisId);

                MessageUtils.SendFrontendMessage("AiProgress", new { id = analysisId, progress = 0, status = "processing", message = "Finding clips to analyze" });

                Dictionary<Bookmark, string> clipPaths = new Dictionary<Bookmark, string>();

                foreach (var bookmark in bookmarks)
                {
                    Log.Information($"Generating clip for bookmark {bookmark.Id}...");
                    string clipPath = string.Empty;
                    try
                    {
                        clipPath = await ClipUtils.CreateAiClipToAnalyzeFromBookmark(bookmark, content);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to generate clip for bookmark {bookmark.Id}");
                    }

                    if (!string.IsNullOrEmpty(clipPath))
                    {
                        clipPaths.Add(bookmark, clipPath);
                        Log.Information($"Generated clip to analyze for bookmark {bookmark.Id}: {Path.GetFileName(clipPath)}");
                    }
                    else
                    {
                        Log.Information($"Failed to generate clip to analyze for bookmark {bookmark.Id}");
                    }

                    // Delay a bit before generating the next clip
                    await Task.Delay(1000);
                }

                Log.Information($"All {clipPaths.Count} clips have been generated. Starting parallel upload to AI service...");

                // Process all uploads asynchronously in parallel
                var uploadTasks = new List<Task>();

                foreach (var item in clipPaths)
                {
                    Bookmark bookmark = item.Key;
                    string clipPath = item.Value;

                    uploadTasks.Add(AnalyzeAiClipToAnalyzeAsync(content, bookmark, analysisId, clipPath));
                }

                try
                {
                    await Task.WhenAll(uploadTasks);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "One or more uploads failed during parallel processing.");
                }

                Log.Information($"All {clipPaths.Count} clips have been uploaded or attempted.");
                await ProcessAnalysisAsync(analysisId, content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error in AnalyzeVideo");
            }
        }

        private static async Task ProcessAnalysisAsync(string analysisId, Content content)
        {
            List<Bookmark> highRatedBookmarks = new List<Bookmark>();

            if (!string.IsNullOrEmpty(analysisId))
            {
                try
                {
                    string finalStatusResponse = await WaitForAnalysisCompletionAsync(analysisId, 20);

                    if (!string.IsNullOrEmpty(finalStatusResponse))
                    {
                        Log.Information($"Final analysis status received: {finalStatusResponse}");
                        // Process the analysis results
                        highRatedBookmarks = ProcessAnalysisResultsAndGetHighRatedBookmarks(finalStatusResponse, content);
                    }
                    else
                    {
                        Log.Information("Analysis timed out or failed to complete");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error during analysis monitoring (analysisId: {analysisId})");
                }
            }
            else
            {
                Log.Information("Cannot check analysis status: missing analysis_id");
            }

            if (highRatedBookmarks.Any())
            {
                Log.Information($"Creating clips for {highRatedBookmarks.Count} high-rated bookmarks");
                try
                {
                    await ClipUtils.CreateAiClipFromBookmarks(highRatedBookmarks, content);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to create final AI clips from high-rated bookmarks.");
                }
            }
            else
            {
                Log.Information("No high-rated bookmarks found, no clips will be created");
            }

            MessageUtils.SendFrontendMessage("AiProgress", new { id = analysisId, progress = 100, status = "done", message = "Done" });
        }

        private static async Task<string> WaitForAnalysisCompletionAsync(string analysisId, int maxWaitMinutes = 20)
        {
            Log.Information($"Starting to poll analysis status for analysis ID: {analysisId}");

            DateTime timeoutTime = DateTime.Now.AddMinutes(maxWaitMinutes);

            await Task.Delay(5000); // 5 seconds initial delay

            int attemptCount = 0;

            while (DateTime.Now < timeoutTime)
            {
                attemptCount++;
                Log.Information($"Checking analysis status (attempt {attemptCount})...");

                try
                {
                    var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"https://processing.segra.tv/ai/status/{analysisId}");
                    statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AuthService.GetJwtAsync());

                    using var statusClient = new HttpClient();
                    var statusResponse = await statusClient.SendAsync(statusRequest);
                    var statusResponseBody = (await statusResponse.Content.ReadAsStringAsync()).Trim();

                    if (!statusResponse.IsSuccessStatusCode)
                    {
                        Log.Information($"Failed to check analysis status. Status: {statusResponse.StatusCode}, Response: {statusResponseBody}");
                        await Task.Delay(15000); // 15 seconds between failed attempts
                        continue;
                    }

                    var statusJson = JsonDocument.Parse(statusResponseBody);
                    var status = statusJson.RootElement.GetProperty("status").GetString();

                    Log.Information($"Current analysis status: {status}");

                    if (status == "completed")
                    {
                        Log.Information("Analysis completed successfully");
                        return statusResponseBody;
                    }
                    else if (status == "error")
                    {
                        Log.Information("Analysis failed with an error");
                        return statusResponseBody;
                    }

                    Log.Information($"Waiting 10 seconds before next status check...");
                    await Task.Delay(10000);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error checking analysis status.");
                    await Task.Delay(15000);
                }
            }

            Log.Information($"Timed out waiting for analysis to complete after {maxWaitMinutes} minutes");
            return null;
        }

        private static List<Bookmark> ProcessAnalysisResultsAndGetHighRatedBookmarks(string statusResponseBody, Content content)
        {
            List<Bookmark> highRatedBookmarks = new List<Bookmark>();

            try
            {
                var statusJson = JsonDocument.Parse(statusResponseBody);
                var status = statusJson.RootElement.GetProperty("status").GetString();

                if (status == "completed")
                {
                    if (statusJson.RootElement.TryGetProperty("clips", out var clipsElement) &&
                        clipsElement.ValueKind == JsonValueKind.Array)
                    {
                        var clips = clipsElement.EnumerateArray().ToList();
                        Log.Information($"Received {clips.Count} analyzed clips");

                        foreach (var clip in clips)
                        {
                            string bookmarkId = clip.GetProperty("bookmark_id").GetString();
                            string clipStatus = clip.GetProperty("status").GetString();

                            if (clipStatus == "completed")
                            {
                                int rating = clip.GetProperty("rating").GetInt32();
                                string summary = clip.GetProperty("summary").GetString();

                                Log.Information($"Clip {bookmarkId}: Rating={rating}, Summary={summary}");

                                var bookmark = content.Bookmarks.FirstOrDefault(b => b.Id.ToString() == bookmarkId);
                                if (bookmark != null)
                                {
                                    if (rating > 6)
                                    {
                                        Log.Information($"Adding bookmark {bookmark.Id} to high-rated list with rating {rating}");
                                        highRatedBookmarks.Add(bookmark);
                                    }
                                }
                                else
                                {
                                    Log.Information($"No matching bookmark found for bookmark id {bookmarkId}");
                                }
                            }
                            else if (clipStatus == "error")
                            {
                                string error = clip.GetProperty("error").GetString();
                                Log.Information($"Clip {bookmarkId} analysis failed: {error}");
                            }
                            else
                            {
                                Log.Information($"Clip {bookmarkId} has status: {clipStatus}");
                            }
                        }
                    }
                    else
                    {
                        Log.Information("No clips found in analysis response");
                    }
                }
                else
                {
                    Log.Information($"Analysis status: {status}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing analysis results.");
            }

            return highRatedBookmarks.OrderBy(b => b.Time).ToList();
        }

        private static async Task AnalyzeAiClipToAnalyzeAsync(Content content, Bookmark bookmark, string analysisId, string clipPath)
        {
            try
            {
                if (string.IsNullOrEmpty(clipPath) || !File.Exists(clipPath))
                {
                    Log.Information($"Clip file not found or invalid for bookmark {bookmark.Id}");
                    return;
                }

                Log.Information($"Processing ai clip to analyze: {Path.GetFileName(clipPath)} for {bookmark.Type} bookmark");

                using var client = new HttpClient();
                using var formContent = new MultipartFormDataContent();

                var fileBytes = await File.ReadAllBytesAsync(clipPath);
                var fileContent = new ByteArrayContent(fileBytes);
                formContent.Add(fileContent, "file", Path.GetFileName(clipPath));

                formContent.Add(new StringContent(analysisId), "analysis_id");
                formContent.Add(new StringContent(bookmark.Id.ToString()), "bookmark_id");

                var request = new HttpRequestMessage(HttpMethod.Post, "https://processing.segra.tv/ai/add")
                {
                    Content = formContent
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AuthService.GetJwtAsync());

                var response = await client.SendAsync(request);
                var uploadResponseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Log.Information($"Successfully uploaded clip for bookmark {bookmark.Id}. Response: {uploadResponseBody.Trim()}");
                }
                else
                {
                    Log.Information($"Failed to upload clip for bookmark {bookmark.Id}. Status: {response.StatusCode}, Response: {uploadResponseBody.Trim()}");
                }

                if (File.Exists(clipPath))
                {
                    File.Delete(clipPath);
                }
            }
            catch (Exception ex)
            {
                Log.Information($"Error processing clip for bookmark {bookmark.Id}: {ex.Message}");
                if (!string.IsNullOrEmpty(clipPath) && File.Exists(clipPath))
                {
                    File.Delete(clipPath);
                }
            }
        }
    }
}
