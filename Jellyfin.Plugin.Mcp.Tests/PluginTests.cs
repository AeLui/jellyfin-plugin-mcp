using System;
using System.IO;
using System.Xml.Serialization;
using Jellyfin.Plugin.Mcp.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests;

public class PluginTests
{
    [Fact]
    public void PluginConfigurationDefaults()
    {
        var config = new PluginConfiguration();

        Assert.True(config.Enabled);
        Assert.Equal(100, config.MaxPageSize);
        Assert.False(config.LogToolCalls);
        Assert.Empty(config.Tokens);
    }

    [Fact]
    public void McpTokenRoundTripsThroughXmlSerializer()
    {
        var token = new McpToken
        {
            Id = Guid.NewGuid(),
            Name = "Claude Desktop",
            UserId = Guid.NewGuid(),
            Scopes = new() { "library", "playback" },
            TokenHash = "abc123",
            Prefix = "AbCdEfGh",
            CreatedAt = new DateTime(2026, 11, 2, 10, 30, 0, DateTimeKind.Utc),
            LastUsedAt = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc),
            ExpiresAt = null,
            Enabled = true
        };
        var config = new PluginConfiguration();
        config.Tokens.Add(token);

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, config);
        using var reader = new StringReader(writer.ToString());
        var result = Assert.IsType<PluginConfiguration>(serializer.Deserialize(reader));

        var copy = Assert.Single(result.Tokens);
        Assert.Equal(token.Id, copy.Id);
        Assert.Equal(token.Name, copy.Name);
        Assert.Equal(token.UserId, copy.UserId);
        Assert.Equal(new[] { "library", "playback" }, copy.Scopes);
        Assert.Equal(token.TokenHash, copy.TokenHash);
        Assert.Equal(token.Prefix, copy.Prefix);
        Assert.Equal(token.CreatedAt, copy.CreatedAt);
        Assert.Equal(token.LastUsedAt, copy.LastUsedAt);
        Assert.Null(copy.ExpiresAt);
        Assert.True(copy.Enabled);
    }

    [Fact]
    public void PluginIdentity()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.SetupGet(p => p.PluginConfigurationsPath).Returns(Path.GetTempPath());
        paths.SetupGet(p => p.PluginsPath).Returns(Path.GetTempPath());
        var xml = new Mock<IXmlSerializer>();

        var plugin = new Plugin(paths.Object, xml.Object);

        Assert.Equal("MCP Server", plugin.Name);
        Assert.Equal(Guid.Parse("08dd6fbf-c026-48c9-9405-824c3906df09"), plugin.Id);
        Assert.Same(plugin, Plugin.Instance);
        var page = Assert.Single(plugin.GetPages());
        Assert.Equal("McpServer", page.Name);
        Assert.Equal("Jellyfin.Plugin.Mcp.Configuration.configPage.html", page.EmbeddedResourcePath);
        Assert.NotNull(typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath));
    }
}
