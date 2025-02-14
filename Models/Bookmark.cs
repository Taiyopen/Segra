using System.Text.Json.Serialization;

namespace Segra.Models
{
    public class Bookmark
    {
        public int Id { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BookmarkType Type { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BookmarkSubtype Subtype { get; set; }
        public TimeSpan Time { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BookmarkType
    {
        Manual,
        Kill,
        Assist,
        Death
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BookmarkSubtype
    {
        Manual,
        Kill,
        Headshot,
        Assist,
        Death
    }
}
