using Segra.Backend.Core.Models;
using Segra.Backend.Games;
using Segra.Backend.Games.CounterStrike2;
using Segra.Backend.Games.LeagueOfLegends;
using Segra.Backend.Games.Pubg;
using Segra.Backend.Games.RocketLeague;
using Segra.Backend.Games.VrChat;
using Serilog;

namespace Segra.Backend.Services
{
    public static class GameIntegrationService
    {
        private const int PUBG_IGDB_ID = 27789;
        private const int LOL_IGDB_ID = 115;
        private const int CS2_IGDB_ID = 242408;
        private const int ROCKET_LEAGUE_IGDB_ID = 11198;
        private static Integration? _gameIntegration;
        public static Integration? GameIntegration => _gameIntegration;

        public static async Task Start(int? igdbId, string? exePath = null)
        {
            if (_gameIntegration != null)
            {
                Log.Information("Active game integration already exists! Shutting down before starting");
                await _gameIntegration.Shutdown();
            }

            _gameIntegration = igdbId switch
            {
                PUBG_IGDB_ID => Settings.Instance.GameIntegrations.Pubg.Enabled ? new PubgIntegration() : null,
                LOL_IGDB_ID => Settings.Instance.GameIntegrations.LeagueOfLegends.Enabled ? new LeagueOfLegendsIntegration() : null,
                CS2_IGDB_ID => Settings.Instance.GameIntegrations.CounterStrike2.Enabled ? new CounterStrike2Integration() : null,
                ROCKET_LEAGUE_IGDB_ID => Settings.Instance.GameIntegrations.RocketLeague.Enabled ? new RocketLeagueIntegration() : null,
                _ => null,
            };

            if (_gameIntegration == null &&
                Settings.Instance.GameIntegrations.VrChat.Enabled &&
                !string.IsNullOrEmpty(exePath) &&
                exePath.EndsWith("VRChat.exe", StringComparison.OrdinalIgnoreCase))
            {
                _gameIntegration = new VrChatVvmwIntegration();
            }

            if (_gameIntegration == null)
            {
                return;
            }

            Log.Information($"Starting game integration (IGDB hint: {igdbId?.ToString() ?? "none"})");
            _ = _gameIntegration.Start();
        }

        public static async Task Shutdown()
        {
            if (_gameIntegration == null)
            {
                return;
            }

            Log.Information("Shutting down game integration");
            await _gameIntegration.Shutdown();
            _gameIntegration = null;
        }
    }
}
