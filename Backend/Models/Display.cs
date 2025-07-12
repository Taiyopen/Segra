using System.Text.Json.Serialization;

namespace Segra.Backend.Models
{
    public class Display
    {
        [JsonPropertyName("deviceName")]
        public required string DeviceName { get; set; }
        
        [JsonPropertyName("deviceId")]
        public required string DeviceId { get; set; }

        [JsonPropertyName("isPrimary")]
        public required bool IsPrimary { get; set; }
    }
}
