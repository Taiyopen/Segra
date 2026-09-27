using System.Reflection;
using System.Text.Json.Serialization;
using Segra.Backend.App;
using Segra.Backend.Core.Models;
using Xunit;

namespace Segra.Tests;

public class RecordingStartSettingsTests
{
    private static readonly HashSet<string> SettingNames = typeof(Settings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
        .OfType<string>()
        .ToHashSet();

    [Fact]
    public void Every_snapshot_key_is_a_setting()
    {
        // A typo here would silently drop that setting from the "applies to the next recording" check
        var unknown = MessageService.RecordingStartSettingKeys.Where(k => !SettingNames.Contains(k)).ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Selected_display_is_not_snapshotted_because_it_applies_immediately()
    {
        Assert.DoesNotContain("selectedDisplay", MessageService.RecordingStartSettingKeys);
    }
}
