using System.Text.Json.Serialization;

namespace Segra.Models
{
    public class Keybind(List<int> keys, KeybindAction action, bool enabled = true)
    {
        [JsonPropertyName("action")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public KeybindAction Action { get; set; } = action;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = enabled;

        [JsonPropertyName("keys")]
        public List<int> Keys { get; set; } = keys;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum KeybindAction
    {
        CreateBookmark
    }
}
