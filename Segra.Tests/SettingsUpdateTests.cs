using System.Text.Json;
using Segra.Backend.Core;
using Segra.Backend.Core.Models;
using Xunit;

namespace Segra.Tests;

public class SettingsUpdateTests
{
    private static Task<bool> Apply(Settings settings, string json)
    {
        using var document = JsonDocument.Parse(json);
        return SettingsService.ApplySettingsPayload(settings, document.RootElement.Clone());
    }

    [Theory]
    [InlineData("clipRateControl", "\"CBR\"", "CBR")]
    [InlineData("clipBitrate", "12", "12")]
    [InlineData("clipMinBitrate", "8", "8")]
    [InlineData("clipMaxBitrate", "20", "20")]
    [InlineData("recordingAudioBitrate", "\"320k\"", "320k")]
    [InlineData("excludeGameDiscordFromMasterMix", "true", "True")]
    [InlineData("gameAudioTrackMask", "3", "3")]
    [InlineData("discordAudioTrackMask", "5", "5")]
    [InlineData("recordingAudioTrackNames", "[\"Game\",\"Voice\"]", "Game,Voice")]
    public async Task Frontend_update_is_applied(string name, string jsonValue, string expected)
    {
        var settings = new Settings();

        bool changed = await Apply(settings, $"{{\"{name}\": {jsonValue}}}");

        Assert.True(changed);
        Assert.Equal(expected, ReadByJsonName(settings, name));
    }

    [Fact]
    public async Task Auth_from_frontend_is_ignored()
    {
        var settings = new Settings();

        bool changed = await Apply(settings, """{"auth": {"jwt": "x", "refreshToken": "y"}}""");

        Assert.False(changed);
        Assert.Equal(string.Empty, settings.Auth.Jwt);
    }

    [Fact]
    public async Task Fields_missing_from_payload_are_left_alone()
    {
        var settings = new Settings { ClipBitrate = 99, ShowGameBackground = false };

        await Apply(settings, """{"clipRateControl": "CBR"}""");

        Assert.Equal(99, settings.ClipBitrate);
        Assert.False(settings.ShowGameBackground);
    }

    [Fact]
    public async Task Identical_payload_reports_no_change()
    {
        var settings = new Settings();

        bool changed = await Apply(settings, $$"""{"clipRateControl": "{{settings.ClipRateControl}}", "clipBitrate": {{settings.ClipBitrate}}}""");

        Assert.False(changed);
    }

    [Fact]
    public async Task Echoing_the_full_settings_back_reports_no_change()
    {
        var settings = new Settings();
        // Same shape the backend pushes to the frontend, which the frontend then sends back on every update.
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        bool changed = await Apply(settings, json);

        Assert.False(changed);
    }

    [Fact]
    public async Task Null_clears_a_nullable_setting()
    {
        var settings = new Settings { LastWindowState = new WindowState { X = 10, Y = 20 } };

        bool changed = await Apply(settings, """{"lastWindowState": null}""");

        Assert.True(changed);
        Assert.Null(settings.LastWindowState);
    }

    [Fact]
    public async Task Null_is_ignored_for_a_non_nullable_setting()
    {
        var settings = new Settings { ClipRateControl = "CBR" };

        bool changed = await Apply(settings, """{"clipRateControl": null}""");

        Assert.False(changed);
        Assert.Equal("CBR", settings.ClipRateControl);
    }

    [Fact]
    public async Task Changing_clip_encoder_resets_clip_codec_to_h264()
    {
        var settings = new Settings { ClipEncoder = "cpu", ClipCodec = "hevc" };

        await Apply(settings, """{"clipEncoder": "gpu", "clipCodec": "hevc"}""");

        Assert.Equal("gpu", settings.ClipEncoder);
        Assert.Equal("h264", settings.ClipCodec);
    }

    private static string ReadByJsonName(Settings settings, string jsonName)
    {
        var property = typeof(Settings).GetProperties().Single(p =>
            p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
                .Any(a => a.Name == jsonName));
        object? value = property.GetValue(settings);
        return value is IEnumerable<string> list ? string.Join(",", list) : value?.ToString() ?? "";
    }
}
