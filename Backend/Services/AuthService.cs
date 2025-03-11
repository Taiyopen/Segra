using Serilog;
using Supabase.Gotrue;

namespace Segra.Backend.Services
{

    public static class AuthService
    {
        public static Session? Session { get; set; }
        private const string Url = "https://ponthqrnesnanivsatps.supabase.co";
        private const string PublicApiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InBvbnRocXJuZXNuYW5pdnNhdHBzIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Mzc2NzMzMjgsImV4cCI6MjA1MzI0OTMyOH0.k8pLDkDgKV0ZLjZjAZ6eUHa40rot5qWa7iJDQKWy1FA";
        private static Supabase.Client? _client;

        static AuthService()
        {
            _client = new Supabase.Client(Url, PublicApiKey);
        }

        // The frontend will call this method because they are the one who has the refresh token
        // TODO (os): Save the refresh token as soon as the user logs in and keep it in a file?
        public static async Task Login(string jwt, string refreshToken)
        {
            try
            {
                if (!IsAuthenticated() || Session == null || Session.Expired())
                {
                    Session = await _client.Auth.SetSession(jwt, refreshToken);
                    Log.Information($"Logged in as {Session.User.Id}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Login failed: {ex.Message}");
                Session = null;
            }
        }

        public static async Task Logout()
        {
            if (Session != null)
            {
                try
                {
                    await _client.Auth.SignOut();
                    Log.Information($"Logged out user: {Session.User.Id}");
                    Session = null;
                }
                catch (Exception ex)
                {
                    Log.Error($"Logout failed: {ex.Message}");
                }
            }
        }

        public static bool IsAuthenticated()
        {
            return Session != null && !string.IsNullOrEmpty(Session.AccessToken);
        }

        public static async Task<string> GetJwtAsync()
        {
            if (Session == null || Session.Expired() == true)
            {
                await _client.Auth.RefreshSession();
            }

            return Session?.AccessToken;
        }
    }
}
