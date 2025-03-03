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
        public static async void AnalyzeVideo(string fileName)
        {
            Log.Information("Starting to analyze video: " + fileName);
            Content content = Settings.Instance.State.Content
                .Where(x => x.FileName == fileName)
                .First();

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
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthService.GetJwt());
            using var client = new HttpClient();
            var response = client.Send(request);
            var responseBody = response.Content.ReadAsStringAsync().Result.Trim();
            // Parse the analysis_id from the initial response
            var responseJson = JsonDocument.Parse(responseBody);
            string analysisId = responseJson.RootElement.GetProperty("analysis_id").GetString();
            Log.Information("Analysis id: " + analysisId);
            MessageUtils.SendFrontendMessage("AiProgress", new { id = analysisId, progress = 0, status = "processing", message = "Finding clips to analyze" });

            Dictionary<Bookmark, string> clipPaths = new Dictionary<Bookmark, string>();

            foreach (var bookmark in bookmarks)
            {
                Log.Information($"Generating clip for bookmark {bookmark.Id}...");
                string clipPath = ClipUtils.CreateAiClipToAnalyzeFromBookmark(bookmark, content).Result;

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

            // Wait for all uploads to complete
            await Task.WhenAll(uploadTasks);
            Log.Information($"All {clipPaths.Count} clips have been uploaded.");

            // The videos will be processed async so we need to have a scheduler that checks the status of the analysis until it's complete (maximum of 20 minutes)
            await ProcessAnalysisAsync(analysisId, content);
        }

        private static async Task ProcessAnalysisAsync(string analysisId, Content content)
        {
            List<Bookmark> highRatedBookmarks = new List<Bookmark>();

            // Wait for the analysis to complete (with a maximum wait time of 20 minutes)
            if (!string.IsNullOrEmpty(analysisId))
            {
                try
                {
                    // Use the scheduler to wait for analysis completion
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
                    Log.Information($"Error during analysis monitoring: {ex.Message}");
                }
            }
            else
            {
                Log.Information("Cannot check analysis status: missing analysis_id");
            }

            if (highRatedBookmarks.Any())
            {
                Log.Information($"Creating clips for {highRatedBookmarks.Count} high-rated bookmarks");
                await ClipUtils.CreateAiClipFromBookmarks(highRatedBookmarks, content);
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

            // Calculate the timeout time
            DateTime timeoutTime = DateTime.Now.AddMinutes(maxWaitMinutes);

            // Initial delay before first check
            await Task.Delay(5000); // 5 seconds initial delay

            int attemptCount = 0;

            while (DateTime.Now < timeoutTime)
            {
                attemptCount++;
                Log.Information($"Checking analysis status (attempt {attemptCount})...");

                try
                {
                    // Create a request to check the status
                    var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"https://processing.segra.tv/ai/status/{analysisId}");
                    statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthService.GetJwt());

                    // Send the status request
                    using var statusClient = new HttpClient();
                    var statusResponse = await statusClient.SendAsync(statusRequest);
                    var statusResponseBody = await statusResponse.Content.ReadAsStringAsync();
                    statusResponseBody = statusResponseBody.Trim();

                    if (!statusResponse.IsSuccessStatusCode)
                    {
                        Log.Information($"Failed to check analysis status. Status: {statusResponse.StatusCode}, Response: {statusResponseBody}");
                        await Task.Delay(15000); // 15 seconds between failed attempts
                        continue;
                    }

                    // Parse the status response
                    var statusJson = JsonDocument.Parse(statusResponseBody);
                    var status = statusJson.RootElement.GetProperty("status").GetString();

                    Log.Information($"Current analysis status: {status}");

                    // If the analysis is complete, return the response body
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
                    await Task.Delay(10 * 1000);
                }
                catch (Exception ex)
                {
                    Log.Information($"Error checking analysis status: {ex.Message}");
                    // Wait before next attempt
                    await Task.Delay(15000); // 15 seconds between error attempts
                }
            }

            // If we reach here, we've timed out
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
                    // Analysis is complete, process the clip information
                    if (statusJson.RootElement.TryGetProperty("clips", out var clipsElement) &&
                        clipsElement.ValueKind == JsonValueKind.Array)
                    {
                        var clips = clipsElement.EnumerateArray().ToList();
                        Log.Information($"Received {clips.Count} analyzed clips");

                        // Process each clip in the response
                        foreach (var clip in clips)
                        {
                            string bookmarkId = clip.GetProperty("bookmark_id").GetString();
                            string clipStatus = clip.GetProperty("status").GetString();

                            if (clipStatus == "completed")
                            {
                                int rating = clip.GetProperty("rating").GetInt32();
                                string summary = clip.GetProperty("summary").GetString();

                                Log.Information($"Clip {bookmarkId}: Rating={rating}, Summary={summary}");

                                // Find the bookmark that corresponds to this clip
                                var bookmark = content.Bookmarks.FirstOrDefault(b => b.Id.ToString() == bookmarkId);
                                if (bookmark != null)
                                {
                                    // Add to high-rated bookmarks if rating > 6
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
                Log.Information($"Error processing analysis results: {ex.Message}");
            }

            return highRatedBookmarks = highRatedBookmarks.OrderBy(b => b.Time).ToList();
        }

        private static async Task AnalyzeAiClipToAnalyzeAsync(Content content, Bookmark bookmark, string analysisId, string clipPath)
        {
            try
            {
                // Use the directly provided clip path instead of searching for it
                if (string.IsNullOrEmpty(clipPath) || !File.Exists(clipPath))
                {
                    Log.Information($"Clip file not found or invalid for bookmark {bookmark.Id}");
                    return;
                }

                Log.Information($"Processing ai clip to analyze: {Path.GetFileName(clipPath)} for {bookmark.Type} bookmark");

                // Upload the clip to the AI service
                using var client = new HttpClient();
                using var formContent = new MultipartFormDataContent();

                // Add the clip file
                var fileBytes = await File.ReadAllBytesAsync(clipPath);
                var fileContent = new ByteArrayContent(fileBytes);
                formContent.Add(fileContent, "file", Path.GetFileName(clipPath));

                // Add the analysis_id and bookmark_id (bookmark.Id)
                formContent.Add(new StringContent(analysisId), "analysis_id");
                formContent.Add(new StringContent(bookmark.Id.ToString()), "bookmark_id");

                // Set the authorization header
                var request = new HttpRequestMessage(HttpMethod.Post, "https://processing.segra.tv/ai/add")
                {
                    Content = formContent
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthService.GetJwt());

                // Send the request asynchronously
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
                File.Delete(clipPath);
            }
            catch (Exception ex)
            {
                Log.Information($"Error processing clip for bookmark {bookmark.Id}: {ex.Message}");
                File.Delete(clipPath);
            }
        }
    }
}
