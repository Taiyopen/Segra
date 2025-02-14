using RePlays.Integrations;
using Serilog;

namespace Segra.Backend.Services
{
    public static class GameIntegrationService
    {
        private const string PUBG = "PUBG: BATTLEGROUNDS";
        private static Integration? gameIntegration;
        public static Integration GameIntegration { get { return gameIntegration; } }
        public static async void Start(string gameName)
        {
            if (gameIntegration != null)
            {
                Log.Information("Active game integration already exists! Shutting down before starting");
                await GameIntegration.Shutdown();
            }
            switch (gameName)
            {
                case PUBG:
                    gameIntegration = new PubgIntegration();
                    break;
                default:
                    gameIntegration = null;
                    break;
            }

            if (GameIntegration == null) return;
            Log.Information("Starting game integration for: " + gameName);
            await GameIntegration.Start();
        }

        public static async void Shutdown()
        {
            if (gameIntegration == null) return;
            Log.Information("Shutting down game integration");
            await GameIntegration.Shutdown();
            gameIntegration = null;
        }
    }
}
