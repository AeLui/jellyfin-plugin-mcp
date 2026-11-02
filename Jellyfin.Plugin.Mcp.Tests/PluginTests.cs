using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tests.Auth;
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
        using var fixture = new PluginFixture();
        var plugin = fixture.Plugin;

        Assert.Equal("MCP Server", plugin.Name);
        Assert.Equal(Guid.Parse("08dd6fbf-c026-48c9-9405-824c3906df09"), plugin.Id);
        Assert.Same(plugin, Plugin.Instance);
        var page = plugin.GetPages().First(p => p.Name == "McpServer");
        Assert.Equal("Jellyfin.Plugin.Mcp.Configuration.configPage.html", page.EmbeddedResourcePath);
    }

    [Fact]
    public void GetPages_EmbeddedResourcesExist()
    {
        using var fixture = new PluginFixture();

        var pages = fixture.Plugin.GetPages().ToList();

        Assert.Equal(new[] { "McpServer", "McpServerJs" }, pages.Select(p => p.Name));
        Assert.Equal("Jellyfin.Plugin.Mcp.Configuration.configPage.js", pages.Single(p => p.Name == "McpServerJs").EmbeddedResourcePath);
        foreach (var page in pages)
        {
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(page.EmbeddedResourcePath);
            Assert.NotNull(stream);
            Assert.True(stream.Length > 0);
        }
    }

    [Fact]
    public void UpdateConfiguration_IgnoresIncomingTokens()
    {
        using var fixture = new PluginFixture();
        var plugin = fixture.Plugin;
        plugin.Configuration.Tokens.Add(new McpToken { Id = Guid.NewGuid(), Name = "keep" });

        var stale = new PluginConfiguration { MaxPageSize = 50, LogToolCalls = true };
        stale.Tokens.Add(new McpToken { Id = Guid.NewGuid(), Name = "resurrected" });
        plugin.UpdateConfiguration(stale);

        Assert.Equal(50, plugin.Configuration.MaxPageSize);
        Assert.True(plugin.Configuration.LogToolCalls);
        Assert.Equal("keep", Assert.Single(plugin.Configuration.Tokens).Name);

        plugin.UpdateConfiguration(new PluginConfiguration());
        Assert.Equal("keep", Assert.Single(plugin.Configuration.Tokens).Name);
    }

    [Fact]
    public void TokenCreate_ThenSettingsUpdate_KeepsNewToken()
    {
        using var fixture = new PluginFixture();
        var plugin = fixture.Plugin;
        var store = new TokenStore(() => plugin.Configuration, plugin.SaveConfigurationWithTokens, new FakeClock(), Plugin.ConfigurationLock);

        var (token, _) = store.Create("new", Guid.NewGuid(), new[] { McpScopes.Library }, null);
        plugin.UpdateConfiguration(new PluginConfiguration { MaxPageSize = 10 });

        Assert.Equal(10, plugin.Configuration.MaxPageSize);
        Assert.Equal(token.Id, Assert.Single(store.GetAll()).Id);
        Assert.True(store.Delete(token.Id));
        plugin.UpdateConfiguration(new PluginConfiguration());
        Assert.Empty(store.GetAll());
    }

    private sealed class PluginFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "jfmcp-tests-" + Guid.NewGuid().ToString("N"));

        public PluginFixture()
        {
            Directory.CreateDirectory(_root);
            var paths = new Mock<IApplicationPaths>();
            paths.SetupGet(p => p.PluginConfigurationsPath).Returns(_root);
            paths.SetupGet(p => p.PluginsPath).Returns(_root);
            var xml = new Mock<IXmlSerializer>();
            xml.Setup(x => x.DeserializeFromFile(It.IsAny<Type>(), It.IsAny<string>())).Returns(() => new PluginConfiguration());
            Plugin = new Plugin(paths.Object, xml.Object);
        }

        public Plugin Plugin { get; }

        public void Dispose() => Directory.Delete(_root, true);
    }
}
