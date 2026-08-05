using Segra.Backend.Core.Models;

using Segra.Backend.Games;

using Segra.Backend.Games.CounterStrike2;

using Segra.Backend.Games.Dota2;

using Segra.Backend.Games.GrandTheftAuto;

using Segra.Backend.Games.LeagueOfLegends;

using Segra.Backend.Games.Minecraft;

using Segra.Backend.Games.Pubg;

using Segra.Backend.Games.RocketLeague;

using Segra.Backend.Games.RunescapeDragonwilds;

using Segra.Backend.Games.Rust;

using Segra.Backend.Games.VrChat;

using Segra.Backend.Games.WarThunder;

using Serilog;



namespace Segra.Backend.Services

{

    public static class GameIntegrationService

    {

        private const int PUBG_IGDB_ID = 27789;

        private const int LOL_IGDB_ID = 115;

        private const int CS2_IGDB_ID = 242408;

        private const int ROCKET_LEAGUE_IGDB_ID = 11198;

        private const int DOTA2_IGDB_ID = 2963;

        private const int RUST_IGDB_ID = 3277;

        private const int MINECRAFT_IGDB_ID = 135400;

        private const int RUNESCAPE_DRAGONWILDS_IGDB_ID = 337712;

        private const int WAR_THUNDER_IGDB_ID = 2165;



        private const int GTA_V_IGDB_ID = 1020;

        private const int FIVEM_IGDB_ID = 146553;

        private const int RAGE_MP_IGDB_ID = 212734;



        private static readonly Dictionary<int, Integration> _integrationsBySlot = new();

        private static readonly SemaphoreSlim _lock = new(1, 1);



        public static async Task Start(int? igdbId, string? gameName = null, string? exePath = null, int slot = 0)

        {

            await _lock.WaitAsync();

            try

            {

                if (_integrationsBySlot.TryGetValue(slot, out var existing))

                {

                    Log.Information("Active game integration already exists for slot {Slot}! Shutting down before starting", slot);

                    await existing.Shutdown();

                    _integrationsBySlot.Remove(slot);

                }



                var integrations = Settings.Instance.GameIntegrations;

                Integration? gameIntegration = null;



                if ((igdbId == PUBG_IGDB_ID || gameName?.Contains("PUBG:", StringComparison.OrdinalIgnoreCase) == true || gameName?.Contains("PLAYERUNKNOWN'S BATTLEGROUNDS", StringComparison.OrdinalIgnoreCase) == true) && integrations.Pubg.Enabled)

                    gameIntegration = new PubgIntegration();

                else if ((igdbId == LOL_IGDB_ID || gameName?.Equals("League of Legends", StringComparison.OrdinalIgnoreCase) == true) && integrations.LeagueOfLegends.Enabled)

                    gameIntegration = new LeagueOfLegendsIntegration();

                else if ((igdbId == CS2_IGDB_ID || gameName?.Equals("Counter-Strike 2", StringComparison.OrdinalIgnoreCase) == true) && integrations.CounterStrike2.Enabled)

                    gameIntegration = new CounterStrike2Integration();

                else if ((igdbId == ROCKET_LEAGUE_IGDB_ID || gameName?.Equals("Rocket League", StringComparison.OrdinalIgnoreCase) == true) && integrations.RocketLeague.Enabled)

                    gameIntegration = new RocketLeagueIntegration();

                else if ((igdbId == DOTA2_IGDB_ID || gameName?.Equals("Dota 2", StringComparison.OrdinalIgnoreCase) == true) && integrations.Dota2.Enabled)

                    gameIntegration = new Dota2Integration();

                else if ((igdbId == RUST_IGDB_ID || gameName?.Equals("Rust", StringComparison.OrdinalIgnoreCase) == true) && integrations.Rust.Enabled)

                    gameIntegration = new RustIntegration();

                else if ((igdbId == MINECRAFT_IGDB_ID || gameName?.Equals("Minecraft", StringComparison.OrdinalIgnoreCase) == true) && integrations.Minecraft.Enabled)

                    gameIntegration = new MinecraftIntegration();

                else if ((igdbId == RUNESCAPE_DRAGONWILDS_IGDB_ID || gameName?.Contains("Dragonwilds", StringComparison.OrdinalIgnoreCase) == true) && integrations.RunescapeDragonwilds.Enabled)

                    gameIntegration = new RunescapeDragonwildsIntegration();

                else if ((igdbId == WAR_THUNDER_IGDB_ID || gameName?.Equals("War Thunder", StringComparison.OrdinalIgnoreCase) == true) && integrations.WarThunder.Enabled)

                    gameIntegration = new WarThunderIntegration();

                else if ((igdbId == GTA_V_IGDB_ID || igdbId == FIVEM_IGDB_ID || igdbId == RAGE_MP_IGDB_ID

                          || gameName?.Contains("Grand Theft Auto", StringComparison.OrdinalIgnoreCase) == true

                          || gameName?.Contains("FiveM", StringComparison.OrdinalIgnoreCase) == true

                          || gameName?.Contains("Rage Multiplayer", StringComparison.OrdinalIgnoreCase) == true) && integrations.Gta.Enabled)

                    gameIntegration = new GtaIntegration();

                else if (integrations.VrChat.Enabled &&

                         !string.IsNullOrEmpty(exePath) &&

                         exePath.EndsWith("VRChat.exe", StringComparison.OrdinalIgnoreCase))

                    gameIntegration = new VrChatVvmwIntegration();



                if (gameIntegration == null)

                    return;



                gameIntegration.RecordingSlot = slot;

                gameIntegration.ExePath = exePath;

                Log.Information($"Starting game integration for slot {slot}, IGDB ID: {igdbId}, Game: {gameName}");

                _integrationsBySlot[slot] = gameIntegration;

                _ = gameIntegration.Start();

            }

            finally

            {

                _lock.Release();

            }

        }



        public static async Task Shutdown(int slot)

        {

            await _lock.WaitAsync();

            try

            {

                if (!_integrationsBySlot.TryGetValue(slot, out var integration))

                {

                    return;

                }



                Log.Information("Shutting down game integration for slot {Slot}", slot);

                await integration.Shutdown();

                _integrationsBySlot.Remove(slot);

            }

            finally

            {

                _lock.Release();

            }

        }



        public static async Task ShutdownAll()

        {

            await _lock.WaitAsync();

            try

            {

                if (_integrationsBySlot.Count == 0)

                {

                    return;

                }



                Log.Information("Shutting down all game integrations");

                foreach (var kvp in _integrationsBySlot.ToList())

                {

                    await kvp.Value.Shutdown();

                }

                _integrationsBySlot.Clear();

            }

            finally

            {

                _lock.Release();

            }

        }



        public static Task Shutdown() => ShutdownAll();

    }

}

