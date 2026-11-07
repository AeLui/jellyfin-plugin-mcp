using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Auth;

public class TokenStoreTests
{
    [Fact]
    public void Create_ReturnsPlaintextOnceAndStoresHashOnly()
    {
        var config = new PluginConfiguration();
        var store = new TokenStore(() => config, _ => { }, new FakeClock());

        var (token, plaintext) = store.Create("laptop", Guid.NewGuid(), new[] { McpScopes.Library }, null);

        var stored = Assert.Single(config.Tokens);
        Assert.StartsWith("jmcp_", plaintext, StringComparison.Ordinal);
        Assert.Equal(TokenGenerator.Hash(plaintext), stored.TokenHash);
        Assert.Equal(TokenGenerator.DisplayPrefix(plaintext), stored.Prefix);
        Assert.True(stored.Enabled);
        Assert.Equal(token.Id, stored.Id);
        Assert.DoesNotContain(plaintext, stored.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(plaintext, stored.TokenHash, StringComparison.Ordinal);
        Assert.DoesNotContain(plaintext, stored.Prefix, StringComparison.Ordinal);
        Assert.NotNull(store.FindByHash(TokenGenerator.Hash(plaintext)));
    }

    [Fact]
    public void Create_StoresLibraryIds()
    {
        var config = new PluginConfiguration();
        var store = new TokenStore(() => config, _ => { }, new FakeClock());
        var library = Guid.NewGuid();

        var (token, _) = store.Create("kids", Guid.NewGuid(), new[] { McpScopes.Library }, null, new[] { library, library });

        Assert.Equal(new[] { library }, Assert.Single(config.Tokens).LibraryIds);
        Assert.Equal(new[] { library }, token.LibraryIds);
        Assert.Equal(new[] { library }, store.GetAll().Single().LibraryIds);
    }

    [Fact]
    public void Create_WithoutLibraryIds_IsUnrestricted()
    {
        var config = new PluginConfiguration();
        var store = new TokenStore(() => config, _ => { }, new FakeClock());

        store.Create("all", Guid.NewGuid(), new[] { McpScopes.Library }, null);

        Assert.Empty(Assert.Single(config.Tokens).LibraryIds);
    }

    [Fact]
    public void XmlConfig_FromBeforeLibraryIds_DeserializesUnrestricted()
    {
        var id = Guid.NewGuid();
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <Enabled>true</Enabled>
              <MaxPageSize>100</MaxPageSize>
              <Tokens>
                <McpToken>
                  <Id>{id}</Id>
                  <Name>old</Name>
                  <UserId>{Guid.NewGuid()}</UserId>
                  <Scopes><string>library</string></Scopes>
                  <TokenHash>abc</TokenHash>
                  <Prefix>jmcp_ab</Prefix>
                  <CreatedAt>2026-01-01T00:00:00Z</CreatedAt>
                  <Enabled>true</Enabled>
                </McpToken>
              </Tokens>
            </PluginConfiguration>
            """;

        using var reader = new StringReader(xml);
        var config = (PluginConfiguration)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(reader)!;

        var token = Assert.Single(config.Tokens);
        Assert.Equal(id, token.Id);
        Assert.NotNull(token.LibraryIds);
        Assert.Empty(token.LibraryIds);
    }

    [Fact]
    public void XmlConfig_RoundTripsLibraryIds()
    {
        var library = Guid.NewGuid();
        var config = new PluginConfiguration();
        config.Tokens.Add(new McpToken { Id = Guid.NewGuid(), LibraryIds = new Collection<Guid> { library } });
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var writer = new StringWriter();
        serializer.Serialize(writer, config);
        using var reader = new StringReader(writer.ToString());

        var copy = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.Equal(new[] { library }, Assert.Single(copy.Tokens).LibraryIds);
    }

    [Fact]
    public void Delete_RemovesToken()
    {
        var config = new PluginConfiguration();
        var store = new TokenStore(() => config, _ => { }, new FakeClock());
        var (token, _) = store.Create("a", Guid.NewGuid(), Array.Empty<string>(), null);

        Assert.True(store.Delete(token.Id));
        Assert.False(store.Delete(token.Id));
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void TouchLastUsed_PersistsAtMostOncePerMinute()
    {
        var config = new PluginConfiguration();
        var saves = 0;
        var clock = new FakeClock();
        var store = new TokenStore(() => config, _ => saves++, clock);
        var (token, _) = store.Create("a", Guid.NewGuid(), Array.Empty<string>(), null);
        saves = 0;

        var t0 = clock.UtcNow;
        store.TouchLastUsed(token.Id, t0);
        store.TouchLastUsed(token.Id, t0.AddSeconds(30));
        store.TouchLastUsed(token.Id, t0.AddSeconds(59));
        Assert.Equal(1, saves);

        store.TouchLastUsed(token.Id, t0.AddSeconds(61));
        Assert.Equal(2, saves);
        Assert.Equal(t0.AddSeconds(61), store.GetAll().Single().LastUsedAt);
    }

    [Fact]
    public void ConcurrentCreateAndTouch_LosesNoWrites()
    {
        var config = new PluginConfiguration();
        var serializer = new XmlSerializer(typeof(PluginConfiguration));

        // Realistic save: serialise a snapshot and swap in the deserialised copy as the live configuration.
        void Save(PluginConfiguration c)
        {
            using var writer = new StringWriter();
            serializer.Serialize(writer, c);
            using var reader = new StringReader(writer.ToString());
            config = (PluginConfiguration)serializer.Deserialize(reader)!;
        }

        var store = new TokenStore(() => config, Save, new FakeClock());
        var seed = store.Create("seed", Guid.NewGuid(), Array.Empty<string>(), null).Token;
        var touchedAt = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Parallel.For(0, 100, i =>
        {
            if (i == 1)
            {
                store.TouchLastUsed(seed.Id, touchedAt);
            }
            else if (i % 2 == 0)
            {
                store.Create("t" + i, Guid.NewGuid(), Array.Empty<string>(), null);
            }
            else
            {
                store.TouchLastUsed(seed.Id, touchedAt);
            }
        });

        Assert.Equal(51, config.Tokens.Count);
        Assert.Equal(51, config.Tokens.Select(t => t.Id).Distinct().Count());
        Assert.Equal(touchedAt, config.Tokens.Single(t => t.Id == seed.Id).LastUsedAt);
    }
}
