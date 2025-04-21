using System.Text.Json.Serialization;

namespace Segra.Backend.Models
{
    public class GameInfo
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }
        
        [JsonPropertyName("exe")]
        public required string Exe { get; set; }
    }
}
