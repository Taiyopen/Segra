using Segra.Backend.App;
using Segra.Backend.Core;
using Segra.Backend.Core.Models;
using Segra.Backend.Recorder;
using Segra.Backend.Windows.Input;
using Serilog;
using System.Net;
using System.Text.Json;

namespace Segra.Backend.Api
{
    /// <summary>
    /// Local control endpoint for the Stream Deck plugin: <c>GET /api/control/status</c> and
    /// <c>POST /api/control/{action}</c>. Runs the same code as Segra's own buttons, no simulated keys.
    /// </summary>
    internal static class ControlApi
    {
        internal const string PathPrefix = "/api/control";
        internal const string ControlHeader = "X-Segra-Control";

        public static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            if (!IsAllowed(request.Headers[ControlHeader], request.Headers["Origin"]))
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            string action = (request.Url?.AbsolutePath ?? "")[PathPrefix.Length..].Trim('/');

            if (request.HttpMethod == "GET" && action == "status")
            {
                await WriteJson(response, GetStatus());
                return;
            }

            if (request.HttpMethod != "POST")
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            bool? done = action switch
            {
                "toggle-recording" => ToggleRecording(),
                "save-replay" => RecordingHotkeyActions.TrySaveReplayBuffer(),
                "bookmark" => await CreateBookmark(),
                "toggle-always-on-buffer" => await ToggleAlwaysOnBuffer(),
                _ => null
            };

            if (done == null)
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            Log.Information("Control API: {Action} (done={Done})", action, done);
            await WriteJson(response, new { done });
        }

        // Browsers must preflight a request with a custom header, and we never answer preflights, so a web page
        // can't reach this. Browsers also attach Origin to cross-site POSTs; the plugin (Node.js) sends none.
        internal static bool IsAllowed(string? controlHeader, string? origin)
        {
            return !string.IsNullOrEmpty(controlHeader) && origin == null;
        }

        private static object GetStatus()
        {
            return new
            {
                recording = AppState.Instance.Recording != null || AppState.Instance.PreRecording != null,
                alwaysOnBuffer = Settings.Instance.AlwaysOnReplayBuffer,
            };
        }

        private static bool ToggleRecording()
        {
            if (AppState.Instance.Recording != null || AppState.Instance.PreRecording != null)
            {
                _ = Task.Run(OBSService.StopRecording);
                return true;
            }

            if (AppState.Instance.GetFreeSlot() == null)
                return false;

            _ = Task.Run(() => OBSService.StartRecording(startManually: true));
            return true;
        }

        private static async Task<bool> CreateBookmark()
        {
            if (!RecordingHotkeyActions.TryCreateBookmark())
                return false;

            await MessageService.SendFrontendMessage("BookmarkCreated", new { });
            return true;
        }

        private static async Task<bool> ToggleAlwaysOnBuffer()
        {
            Settings.Instance.AlwaysOnReplayBuffer = !Settings.Instance.AlwaysOnReplayBuffer;
            SettingsService.SaveSettings();
            await MessageService.SendSettingsToFrontend("Always-on replay buffer toggled from Stream Deck");
            return true;
        }

        private static async Task WriteJson(HttpListenerResponse response, object value)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");
            byte[] body = JsonSerializer.SerializeToUtf8Bytes(value);
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body);
        }
    }
}
