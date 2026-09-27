using Segra.Backend.Recorder;
using Xunit;

namespace Segra.Tests;

public class AlwaysOnBufferPolicyTests
{
    [Fact]
    public void Runs_when_enabled_and_nothing_records()
    {
        Assert.True(AlwaysOnBufferPolicy.ShouldRun(enabled: true, obsInitialized: true, isExiting: false, anyRecording: false, anyPreRecording: false));
    }

    [Theory]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, false, false, true)]
    public void Does_not_run_when_disabled_uninitialized_exiting_or_a_slot_is_busy(
        bool enabled, bool obsInitialized, bool isExiting, bool anyRecording, bool anyPreRecording)
    {
        Assert.False(AlwaysOnBufferPolicy.ShouldRun(enabled, obsInitialized, isExiting, anyRecording, anyPreRecording));
    }

    [Fact]
    public void Starts_when_wanted_and_not_running()
    {
        Assert.Equal(AlwaysOnBufferAction.Start, AlwaysOnBufferPolicy.Decide(shouldRun: true, isRunning: false, "a", lastKey: null));
    }

    [Fact]
    public void Leaves_a_running_buffer_alone_while_its_configuration_is_unchanged()
    {
        Assert.Equal(AlwaysOnBufferAction.None, AlwaysOnBufferPolicy.Decide(shouldRun: true, isRunning: true, "a", lastKey: "a"));
    }

    [Fact]
    public void Restarts_when_the_configuration_changes()
    {
        Assert.Equal(AlwaysOnBufferAction.Restart, AlwaysOnBufferPolicy.Decide(shouldRun: true, isRunning: true, "b", lastKey: "a"));
    }

    [Fact]
    public void Stops_when_no_longer_wanted()
    {
        Assert.Equal(AlwaysOnBufferAction.Stop, AlwaysOnBufferPolicy.Decide(shouldRun: false, isRunning: true, "a", lastKey: "a"));
    }

    [Fact]
    public void A_failed_start_is_not_retried_with_the_same_configuration()
    {
        Assert.Equal(AlwaysOnBufferAction.None, AlwaysOnBufferPolicy.Decide(shouldRun: true, isRunning: false, "a", lastKey: "a"));
    }

    [Fact]
    public void A_failed_start_is_retried_once_the_configuration_changes()
    {
        Assert.Equal(AlwaysOnBufferAction.Start, AlwaysOnBufferPolicy.Decide(shouldRun: true, isRunning: false, "b", lastKey: "a"));
    }

    [Fact]
    public void Only_a_buffer_that_ran_for_a_while_is_retried_after_failing()
    {
        Assert.False(AlwaysOnBufferPolicy.ShouldRetryAfterFailure(TimeSpan.FromMinutes(1)));
        Assert.True(AlwaysOnBufferPolicy.ShouldRetryAfterFailure(TimeSpan.FromMinutes(5)));
    }
}
