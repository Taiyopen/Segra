using Segra.Backend.Core.Models;
using Segra.Backend.Recorder;
using Xunit;

namespace Segra.Tests;

public class AudioTrackLayoutTests
{
    private static DeviceSetting Device(string id, uint mask) => new() { Id = id, Name = id, AudioTrackMask = mask };

    [Fact]
    public void Masks_from_every_source_are_combined()
    {
        uint mask = AudioTrackLayout.ComputeTracksMask(
            [Device("mic", 0b0001)],
            [Device("speakers", 0b0010)],
            gameMask: 0b0100,
            voiceMask: 0b1000);

        Assert.Equal(0b1111u, mask);
    }

    [Fact]
    public void Absent_game_and_voice_sources_add_no_tracks()
    {
        uint mask = AudioTrackLayout.ComputeTracksMask([Device("mic", 0b0010)], [], gameMask: null, voiceMask: null);

        Assert.Equal(0b0010u, mask);
    }

    [Fact]
    public void Devices_without_an_id_are_skipped()
    {
        uint mask = AudioTrackLayout.ComputeTracksMask([Device("", 0b0100), Device("mic", 0b0001)], [], null, null);

        Assert.Equal(0b0001u, mask);
    }

    [Fact]
    public void Empty_mask_falls_back_to_track_one()
    {
        uint mask = AudioTrackLayout.ComputeTracksMask([Device("mic", 0)], [], gameMask: 0, voiceMask: null);

        Assert.Equal(1u, mask);
    }

    [Fact]
    public void Bits_beyond_six_tracks_are_dropped()
    {
        uint mask = AudioTrackLayout.ComputeTracksMask([Device("mic", 0b1100_0001)], [], gameMask: 0x100, voiceMask: null);

        Assert.Equal(0b0001u, mask);
    }

    [Fact]
    public void Custom_names_are_trimmed_and_blanks_become_track_n()
    {
        var names = AudioTrackLayout.ResolveTrackNames([" Game ", "", "   ", "Voice"]);

        Assert.Equal(["Game", "Track 2", "Track 3", "Voice", "Track 5", "Track 6"], names);
    }

    [Fact]
    public void No_custom_names_gives_six_default_tracks()
    {
        var names = AudioTrackLayout.ResolveTrackNames(null);

        Assert.Equal(["Track 1", "Track 2", "Track 3", "Track 4", "Track 5", "Track 6"], names);
    }
}
