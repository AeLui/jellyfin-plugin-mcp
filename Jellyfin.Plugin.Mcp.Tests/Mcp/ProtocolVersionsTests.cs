using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class ProtocolVersionsTests
{
    [Fact]
    public void All_ListsEveryVersionNewestFirst()
    {
        Assert.Equal(new[] { "2026-07-28", "2025-11-25", "2025-06-18", "2025-03-26" }, ProtocolVersions.All);
    }

    [Fact]
    public void Eras_AreDistinguished()
    {
        Assert.True(ProtocolVersions.IsStateless("2026-07-28"));
        Assert.False(ProtocolVersions.IsHandshake("2026-07-28"));
        Assert.True(ProtocolVersions.IsHandshake("2025-06-18"));
        Assert.False(ProtocolVersions.IsStateless("2025-06-18"));
        Assert.False(ProtocolVersions.IsStateless("1999-01-01"));
        Assert.False(ProtocolVersions.IsHandshake("1999-01-01"));
    }

    [Fact]
    public void Negotiate_NeverPicksTheStatelessVersion()
    {
        Assert.Equal("2025-11-25", ProtocolVersions.Negotiate("2026-07-28"));
        Assert.Equal("2025-11-25", ProtocolVersions.Latest);
    }
}
