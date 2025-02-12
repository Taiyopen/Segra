using System.Text.Json.Serialization;

namespace Segra.Models
{
    public class Bookmark
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BookmarkType Type { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BookmarkSubtype Subtype { get; set; }
        public TimeSpan Time { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BookmarkType
    {
        Kill,
        Assist,
        Death
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BookmarkSubtype
    {
        Kill,
        Headshot,
        Assist,
        Death
    }
}
