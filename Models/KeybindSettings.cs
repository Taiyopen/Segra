using System.Text.Json.Serialization;

namespace Segra.Models
{
    public class Keybind : IEquatable<Keybind>
    {
        [JsonPropertyName("action")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public KeybindAction Action { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("keys")]
        public List<int> Keys { get; set; }

        public Keybind(List<int> keys, KeybindAction action, bool enabled = true)
        {
            Keys = keys;
            Action = action;
            Enabled = enabled;
        }

        public bool Equals(Keybind? other)
        {
            if (other == null) return false;
            
            return Action == other.Action && 
                   Enabled == other.Enabled && 
                   Keys.SequenceEqual(other.Keys);
        }

        public override bool Equals(object? obj)
        {
            if (obj is Keybind keybind)
            {
                return Equals(keybind);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Action,
                Enabled,
                Keys.Aggregate(0, (hash, key) => hash ^ key.GetHashCode())
            );
        }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum KeybindAction
    {
        CreateBookmark,
        SaveReplayBuffer
    }
}
