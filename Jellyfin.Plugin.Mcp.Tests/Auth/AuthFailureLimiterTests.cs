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

    [Fact]
    public void GlobalSweep_RunsAtMostOncePerWindow()
    {
        var clock = new FakeClock();
        var limiter = new AuthFailureLimiter(clock);

        clock.Advance(TimeSpan.FromSeconds(51));
        limiter.RecordFailure("f");
        clock.Advance(TimeSpan.FromSeconds(10));
        limiter.IsBlocked("probe");

        // The sweep ran at t=61 and f (age 10 s) survived. At t=116 f is stale, but the next sweep is not due yet.
        clock.Advance(TimeSpan.FromSeconds(55));
        limiter.IsBlocked("other");
        Assert.Equal(1, limiter.TrackedAddressCount);

        clock.Advance(TimeSpan.FromSeconds(10));
        limiter.IsBlocked("other");
        Assert.Equal(0, limiter.TrackedAddressCount);
    }

    [Fact]
    public void RequestingIpIsPrunedInline_WithoutGlobalSweep()
    {
        var clock = new FakeClock();
        var limiter = new AuthFailureLimiter(clock, 1);
        limiter.RecordFailure("a");
        limiter.RecordFailure("b");
        clock.Advance(TimeSpan.FromSeconds(30));
        limiter.RecordFailure("c");
        clock.Advance(TimeSpan.FromSeconds(31));

        // Sweep is due here and removes a and b; c stays.
        Assert.False(limiter.IsBlocked("a"));
        Assert.Equal(1, limiter.TrackedAddressCount);
    }

    [Fact]
    public void TrackedAddressCap_Holds_AndFailsClosedForNewIps()
    {
        var clock = new FakeClock();
        var limiter = new AuthFailureLimiter(clock, 10, null, 3);

        limiter.RecordFailure("1");
        limiter.RecordFailure("2");
        limiter.RecordFailure("3");
        limiter.RecordFailure("4");

        Assert.Equal(3, limiter.TrackedAddressCount);
        Assert.True(limiter.IsBlocked("4"));
        Assert.False(limiter.IsBlocked("1"));

        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(limiter.IsBlocked("4"));
        Assert.Equal(0, limiter.TrackedAddressCount);
    }
}
