using System;
using Jellyfin.Plugin.Mcp.Auth;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Auth;

public class AuthFailureLimiterTests
{
    [Fact]
    public void BlocksAfterMaxFailures_PerIp_AndUnblocksAfterWindow()
    {
        var clock = new FakeClock();
        var limiter = new AuthFailureLimiter(clock);

        for (var i = 0; i < 9; i++)
        {
            limiter.RecordFailure("1.1.1.1");
        }

        Assert.False(limiter.IsBlocked("1.1.1.1"));
        limiter.RecordFailure("1.1.1.1");
        Assert.True(limiter.IsBlocked("1.1.1.1"));
        Assert.False(limiter.IsBlocked("2.2.2.2"));

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(limiter.IsBlocked("1.1.1.1"));
    }

    [Fact]
    public void WindowSlides_OldFailuresExpireIndividually()
    {
        var clock = new FakeClock();
        var limiter = new AuthFailureLimiter(clock, 2);

        limiter.RecordFailure("ip");
        clock.Advance(TimeSpan.FromSeconds(40));
        limiter.RecordFailure("ip");
        Assert.True(limiter.IsBlocked("ip"));

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(limiter.IsBlocked("ip"));
    }
}
