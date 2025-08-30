using Segra.Backend.Models;
using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Segra.Backend.Utils
{
    public static class GameUtils
    {
        private static List<GameInfo> _games = new List<GameInfo>();
        private static HashSet<string> _gameExePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _exeToGameName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _isInitialized = false;

        public static async Task InitializeAsync()
        {
            if (_isInitialized) return;

            await DownloadGamesJsonIfNeededAsync();
            LoadGamesFromJson();
            _isInitialized = true;
        }

        public static bool IsGameExePath(string exePath)
        {
            if (!_isInitialized || string.IsNullOrEmpty(exePath))
                return false;

            // Convert backslashes to forward slashes for consistent comparison
            string normalizedPath = exePath.Replace("\\", "/");

            // Check if any game exe path is contained within the given exePath
            foreach (var gamePath in _gameExePaths)
            {
                if (normalizedPath.Contains(gamePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string? GetGameNameFromExePath(string exePath)
        {
            if (!_isInitialized || string.IsNullOrEmpty(exePath))
                return null;

            string normalizedPath = exePath.Replace("\\", "/");

            foreach (var entry in _exeToGameName)
            {
                if (normalizedPath.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        private static void LoadGamesFromJson()
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra");
            string jsonPath = Path.Combine(appDataDir, "games.json");

            if (!File.Exists(jsonPath))
            {
                Log.Warning("games.json file not found. Game detection from JSON will be disabled.");
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                _games = JsonSerializer.Deserialize<List<GameInfo>>(jsonContent) ?? new List<GameInfo>();

                // Build lookup collections for fast access
                _gameExePaths.Clear();
                _exeToGameName.Clear();

                foreach (var game in _games)
                {
                    if (!string.IsNullOrEmpty(game.Exe))
                    {
                        // Normalize path for consistent comparison
                        string normalizedExe = game.Exe.Replace("\\", "/");
                        _gameExePaths.Add(normalizedExe);
                        _exeToGameName[normalizedExe] = game.Name;
                    }
                }

                Log.Information($"Loaded {_games.Count} games from games.json");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading games.json");
            }
        }

        private static async Task DownloadGamesJsonIfNeededAsync()
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Segra");
            Directory.CreateDirectory(appDataDir); // Ensure directory exists

            string jsonPath = Path.Combine(appDataDir, "games.json");
            string apiUrl = "https://api.github.com/repos/Segergren/Segra/contents/games.json?ref=main";
            string localHashPath = Path.Combine(appDataDir, "games.hash");
            bool needsDownload = true;

            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Segra");
                httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.json");

                Log.Information("Fetching games.json metadata...");

                try
                {
                    var response = await httpClient.GetAsync(apiUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error($"Failed to fetch metadata from {apiUrl}. Status: {response.StatusCode}");
                        return;
                    }

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var metadata = JsonSerializer.Deserialize<GitHubFileMetadata>(jsonResponse);

                    if (metadata?.DownloadUrl == null)
                    {
                        Log.Error("Download URL not found in the API response.");
                        return;
                    }

                    string remoteHash = metadata.Sha;

                    // Check if we already have the file with the correct hash
                    if (File.Exists(jsonPath) && File.Exists(localHashPath))
                    {
                        string localHash = await File.ReadAllTextAsync(localHashPath);
                        if (localHash == remoteHash)
                        {
                            Log.Information("Found existing games.json with matching hash. Skipping download.");
                            needsDownload = false;
                        }
                        else
                        {
                            Log.Information("Found existing games.json but hash doesn't match. Downloading new version.");
                        }
                    }

                    if (needsDownload)
                    {
                        Log.Information("Downloading games.json...");

                        httpClient.DefaultRequestHeaders.Clear();
                        var jsonBytes = await httpClient.GetByteArrayAsync(metadata.DownloadUrl);
                        await File.WriteAllBytesAsync(jsonPath, jsonBytes);

                        // Save the hash for future reference
                        await File.WriteAllTextAsync(localHashPath, remoteHash);

                        Log.Information("Download complete");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error downloading games.json");
                }
            }
        }

        private class GitHubFileMetadata
        {
            [JsonPropertyName("sha")]
            public required string Sha { get; set; }

            [JsonPropertyName("download_url")]
            public required string DownloadUrl { get; set; }
        }
    }
}
