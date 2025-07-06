using Segra.Backend.Models;
using Segra.Backend.Utils;
using Serilog;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Segra.Backend.GameIntegration
{
    internal class CounterStrike2Integration : Integration
    {
        private readonly HttpListener _listener = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private const string Prefix = "http://127.0.0.1:1340/";
        
        // Game state classes to deserialize the CS2 JSON payload
        private class GameState
        {
            [System.Text.Json.Serialization.JsonPropertyName("player")]
            public Player? Player { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("provider")]
            public Provider? Provider { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("map")]
            public Map? Map { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("previously")]
            public Previously? Previously { get; set; }
        }

        private class Player
        {
            [System.Text.Json.Serialization.JsonPropertyName("steamid")]
            public string? SteamId { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("match_stats")]
            public MatchStats? MatchStats { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("state")]
            public PlayerState? State { get; set; }
        }

        private class Provider
        {
            [System.Text.Json.Serialization.JsonPropertyName("steamid")]
            public string? SteamId { get; set; }
        }

        private class Map
        {
            [System.Text.Json.Serialization.JsonPropertyName("phase")]
            public string? Phase { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        private class MatchStats
        {
            [System.Text.Json.Serialization.JsonPropertyName("kills")]
            public int? Kills { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("deaths")]
            public int? Deaths { get; set; }
        }

        private class PlayerState
        {
            [System.Text.Json.Serialization.JsonPropertyName("health")]
            public int Health { get; set; }
        }

        private class Previously
        {
            [System.Text.Json.Serialization.JsonPropertyName("player")]
            public PreviousPlayer? Player { get; set; }
        }

        private class PreviousPlayer
        {
            [System.Text.Json.Serialization.JsonPropertyName("steamid")]
            public string? SteamId { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("match_stats")]
            public MatchStats? MatchStats { get; set; }
            
            [System.Text.Json.Serialization.JsonPropertyName("state")]
            public PlayerState? State { get; set; }
        }

        public override async Task Start()
        {
            try
            {
                EnsureCfgExists();
                InitializeListener();
                Log.Information($"Counter Strike 2 integration listening on {Prefix}");
                while (_listener.IsListening)
                {
                    try
                    {
                        HttpListenerContext context = await _listener.GetContextAsync();
                    
                        // Use semaphore to ensure only one request is processed at a time
                        await _semaphore.WaitAsync();
                        
                        try
                        {
                            await HandleRequest(context);
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException or HttpListenerException)
                    {
                        Log.Information("CS2 integration listener stopped");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"CS2 integration error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start CS2 integration: {ex.Message}");
            }
        }

        public override Task Shutdown()
        {
            Log.Information("Shutting down CS2 integration");
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (Exception ex)
            {
                Log.Warning($"Error shutting down CS2 integration: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        private void InitializeListener()
        {
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                if (context.Request.HttpMethod == "POST")
                {
                    string body = await ReadRequestBodyAsync(context.Request);
                    Log.Debug($"CS2 integration received payload: {body}");
                    
                    GameState gameState = DeserializeState(body);
                    ProcessGameState(gameState);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Error handling CS2 request: {ex.Message}");
            }
            finally
            {
                await WriteResponseAsync(context.Response);
                context.Response.Close();
            }
        }

        private async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private async Task WriteResponseAsync(HttpListenerResponse response)
        {
            byte[] buffer = Encoding.UTF8.GetBytes("");
            response.ContentLength64 = buffer.Length;
            using (Stream output = response.OutputStream)
            {
                await output.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        private GameState DeserializeState(string body)
        {
            try
            {
                return JsonSerializer.Deserialize<GameState>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new GameState();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to deserialize CS2 state\n{Body}", body);
                return new GameState();
            }
        }

        private void ProcessGameState(GameState gameState)
        {
            try
            {
                if (!IsValidState(gameState))
                {
                    Log.Debug($"Skipping invalid state - Phase: {gameState.Map?.Phase}, SteamId match: {gameState.Player?.SteamId == gameState.Provider?.SteamId}");
                    return;
                }

                // Process changes using the "previously" block
                ProcessStatChanges(gameState);
            }
            catch (Exception ex)
            {
                Log.Warning($"Error processing game state: {ex.Message}");
            }
        }

        private void ProcessStatChanges(GameState gameState)
        {
            if (gameState.Previously?.Player == null)
            {
                Log.Debug("No previous state data, skipping stat tracking");
                return;
            }

            var previousStats = gameState.Previously.Player.MatchStats;

            if (previousStats == null)
            {
                Log.Debug("No previous stats included in state, skipping stat tracking");
                return;
            }

            // Check for kill - if kills field exists in previously, it means kills changed
            if (previousStats.Kills.HasValue)
            {
                var currentKills = gameState.Player?.MatchStats?.Kills ?? 0;
                int killsGained = currentKills - previousStats.Kills.Value;
                Log.Information($"Kill detected: {previousStats.Kills.Value} -> {currentKills} (+{killsGained})");
                
                if(killsGained > 1)
                {
                    Log.Debug($"Warning: More than one kill detected: {killsGained}. Bug?");
                }

                // Add bookmarks for each kill gained
                for (int i = 0; i < killsGained; i++)
                {
                    AddBookmark(BookmarkType.Kill);
                }
            }

            // Check for death - if deaths field exists in previously, it means deaths changed
            if (previousStats.Deaths.HasValue)
            {
                var currentDeaths = gameState.Player?.MatchStats?.Deaths ?? 0;
                int deathsGained = currentDeaths - previousStats.Deaths.Value;
                Log.Information($"Death detected: {previousStats.Deaths.Value} -> {currentDeaths} (+{deathsGained})");
                
                if(deathsGained > 1)
                {
                    Log.Debug($"Warning: More than one death detected: {deathsGained}. Bug?");
                }
                
                // Add bookmarks for each death gained
                for (int i = 0; i < deathsGained; i++)
                {
                    AddBookmark(BookmarkType.Death);
                }
            }
        }

        private static bool IsValidState(GameState gameState)
        {
            // Only process stats during live gameplay
            if (gameState.Map?.Phase != "live" && gameState.Map?.Phase != "gameover")
            {
                Log.Debug($"Skipping invalid state - Phase: {gameState.Map?.Phase}");
                return false;
            }

            // Ensure we have valid player data
            if (gameState.Player?.MatchStats == null)
            {
                Log.Debug($"Skipping invalid state - No player match stats");
                return false;
            }

            // Ensure the player data matches the provider (same Steam ID)
            if (gameState.Player.SteamId != gameState.Provider?.SteamId)
            {
                Log.Debug($"Skipping invalid state - Player Steam ID: {gameState.Player.SteamId}, Provider Steam ID: {gameState.Provider?.SteamId}");
                return false;
            }

            // Ensure the player data matches the previously player (same Steam ID), eg if the player takes over a bot
            if (gameState.Previously?.Player?.SteamId != null && gameState.Previously?.Player?.SteamId != gameState.Provider?.SteamId)
            {
                Log.Debug($"Skipping invalid state - Previously player Steam ID: {gameState.Previously?.Player?.SteamId}, Provider Steam ID: {gameState.Provider?.SteamId}");
                return false;
            }

            return true;
        }

        private static void AddBookmark(BookmarkType type)
        {
            if (Settings.Instance.State.Recording == null)
            {
                Log.Debug($"No recording active, skipping {type} bookmark");
                return;
            }

            var bookmark = new Bookmark
            {
                Type = type,
                Time = DateTime.Now - Settings.Instance.State.Recording.StartTime
            };
            Settings.Instance.State.Recording.Bookmarks.Add(bookmark);
            Log.Information($"Added {type} bookmark at {bookmark.Time}");
        }

        private void EnsureCfgExists()
        {
            try
            {
                string steam = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string cfgPath = Path.Combine(steam,
                    "Steam",
                    "steamapps",
                    "common",
                    "Counter-Strike Global Offensive",
                    "game",
                    "csgo",
                    "cfg",
                    "gamestate_integration_segra.cfg");

                if (File.Exists(cfgPath))
                {
                    string existingContent = File.ReadAllText(cfgPath);
                    string expectedContent = GenerateCfg();
                    if (existingContent.Equals(expectedContent, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
                File.WriteAllText(cfgPath, GenerateCfg());
                Log.Information($"Created CS2 gamestate integration config at {cfgPath}");
                _ = MessageUtils.ShowModal("Game integration", $"There has been an update to the CS2 integration. Please restart the game to apply the changes.", "warning");
            }
            catch (Exception ex)
            {
                Log.Warning($"Could not ensure CS2 cfg exists: {ex.Message}");
            }
        }

        private string GenerateCfg()
        {
            return "\"Segra\" {\n" +
                "    \"uri\" \"http://localhost:1340/\"\n" +
                "    \"timeout\" \"5.0\"\n" +
                "    \"buffer\" \"0.1\"\n" +
                "    \"throttle\" \"0.1\"\n" +
                "    \"heartbeat\" \"60.0\"\n" +
                "    \"data\" {\n" +
                "        \"player_id\" \"1\"\n" +
                "        \"provider\" \"1\"\n" +
                "        \"map\" \"1\"\n" +
                "        \"player_state\" \"1\"\n" +
                "        \"player_match_stats\" \"1\"\n" +
                "    }\n" +
                "}";
        }
    }
}