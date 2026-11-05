using System.Collections.Generic;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Auth;

public class OriginPolicyTests
{
    private static OriginPolicy Policy(params string[] allowed)
    {
        var settings = new Mock<IPluginSettings>();
        settings.SetupGet(s => s.AllowedOrigins).Returns(allowed);
        return new OriginPolicy(settings.Object);
    }

    [Fact]
    public void NoOrigin_IsAllowed()
    {
        Assert.True(Policy().IsAllowed(null, "jellyfin.example.com"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not a uri")]
    [InlineData("ftp://jellyfin.example.com")]
    [InlineData("")]
    public void NullOrMalformedOrigin_IsRejected(string origin)
    {
        Assert.False(Policy().IsAllowed(origin, "jellyfin.example.com"));
    }

    [Theory]
    [InlineData("https://jellyfin.example.com:8920", "jellyfin.example.com")]
    [InlineData("https://jellyfin.example.com", "JELLYFIN.example.com")]
    [InlineData("http://192.168.1.10:8096", "192.168.1.10")]
    [InlineData("http://[::1]:8096", "::1")]
    [InlineData("http://[::1]:8096", "[::1]")]
    public void SameHostOrigin_IsAllowedWhateverSchemeAndPort(string origin, string host)
    {
        Assert.True(Policy().IsAllowed(origin, host));
    }

    [Fact]
    public void ForeignOrigin_IsRejected()
    {
        Assert.False(Policy().IsAllowed("https://evil.example", "jellyfin.example.com"));
    }

    [Fact]
    public void ProxyHostDiffers_OnlyAllowedWhenListed()
    {
        Assert.False(Policy().IsAllowed("https://jellyfin.example.com", "127.0.0.1"));
        Assert.True(Policy("https://jellyfin.example.com").IsAllowed("https://jellyfin.example.com", "127.0.0.1"));
    }

    [Fact]
    public void ListedOrigin_IsMatchedAfterNormalization()
    {
        Assert.True(Policy("HTTPS://Evil.example/").IsAllowed("https://evil.example", "jellyfin.example.com"));
        Assert.True(Policy("https://evil.example:443").IsAllowed("https://evil.example", "jellyfin.example.com"));
        Assert.False(Policy("https://evil.example").IsAllowed("http://evil.example", "jellyfin.example.com"));
        Assert.False(Policy("https://evil.example").IsAllowed("https://evil.example:8443", "jellyfin.example.com"));
    }

    [Fact]
    public void MissingOrMalformedAllowList_IsTreatedAsEmpty()
    {
        var settings = new Mock<IPluginSettings>();
        Assert.False(new OriginPolicy(settings.Object).IsAllowed("https://evil.example", "jellyfin.example.com"));
        Assert.False(Policy("not a uri", " ").IsAllowed("https://evil.example", "jellyfin.example.com"));
    }

    [Theory]
    [InlineData("HTTPS://Evil.example/", "https://evil.example")]
    [InlineData("http://host:8096", "http://host:8096")]
    [InlineData("https://host:443", "https://host")]
    [InlineData("null", null)]
    [InlineData("https://host/path", null)]
    public void Normalize_CanonicalizesOrRejects(string origin, string? expected)
    {
        Assert.Equal(expected, OriginPolicy.Normalize(origin));
    }
}
