using Segra.Backend.Api;
using Xunit;

namespace Segra.Tests;

public class ControlApiTests
{
    [Fact]
    public void Allows_a_local_tool_that_sends_the_control_header()
    {
        Assert.True(ControlApi.IsAllowed(controlHeader: "1", origin: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Rejects_requests_without_the_control_header(string? controlHeader)
    {
        Assert.False(ControlApi.IsAllowed(controlHeader, origin: null));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:44040")]
    [InlineData("null")]
    public void Rejects_requests_from_a_browser_page(string origin)
    {
        Assert.False(ControlApi.IsAllowed(controlHeader: "1", origin));
    }
}
