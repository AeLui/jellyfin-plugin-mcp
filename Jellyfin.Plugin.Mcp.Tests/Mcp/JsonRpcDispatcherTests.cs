using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class JsonRpcDispatcherTests
{
    private static readonly McpPrincipal Principal = new(Guid.NewGuid(), new System.Collections.Generic.HashSet<string>());

    private static JsonRpcDispatcher CreateDispatcher() => new(new ToolRegistry(), new McpServerInfo("jellyfin-mcp", "1.2.3"));

    private static async Task<DispatchResult> SendAsync(string json)
    {
        return await CreateDispatcher().DispatchAsync(Encoding.UTF8.GetBytes(json), Principal, CancellationToken.None);
    }

    private static JsonElement Parse(DispatchResult result)
    {
        Assert.NotNull(result.Body);
        return JsonDocument.Parse(result.Body!).RootElement.Clone();
    }

    [Fact]
    public async Task Initialize_EchoesSupportedVersion()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}""");

        Assert.Equal(200, result.StatusCode);
        var root = Parse(result);
        Assert.Equal("2025-06-18", root.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
    }

    [Fact]
    public async Task Initialize_UnknownVersion_ReturnsLatest()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"1999-01-01"}}""");

        Assert.Equal(ProtocolVersions.Latest, Parse(result).GetProperty("result").GetProperty("protocolVersion").GetString());
    }

    [Fact]
    public async Task Initialize_AdvertisesToolsOnly()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","id":"a","method":"initialize","params":{}}""");

        var payload = Parse(result).GetProperty("result");
        var capabilities = payload.GetProperty("capabilities");
        Assert.Single(capabilities.EnumerateObject());
        Assert.False(capabilities.GetProperty("tools").GetProperty("listChanged").GetBoolean());
        Assert.Equal("jellyfin-mcp", payload.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal("1.2.3", payload.GetProperty("serverInfo").GetProperty("version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("instructions").GetString()));
    }

    [Fact]
    public async Task Ping_ReturnsEmptyObject()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","id":7,"method":"ping"}""");

        var root = Parse(result);
        Assert.Equal(JsonValueKind.Object, root.GetProperty("result").ValueKind);
        Assert.Empty(root.GetProperty("result").EnumerateObject());
    }

    [Fact]
    public async Task Notification_Returns202WithNoBody()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        Assert.Equal(202, result.StatusCode);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task InvalidJson_ReturnsParseError()
    {
        var result = await SendAsync("{not json");

        var root = Parse(result);
        Assert.Equal(-32700, root.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task WrongJsonRpcVersion_ReturnsInvalidRequest()
    {
        var result = await SendAsync("""{"jsonrpc":"1.0","id":3,"method":"ping"}""");

        var root = Parse(result);
        Assert.Equal(-32600, root.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(3, root.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task MissingMethod_ReturnsInvalidRequest()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","id":3}""");

        Assert.Equal(-32600, Parse(result).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound_WithStringId()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","id":"abc","method":"nope"}""");

        var root = Parse(result);
        Assert.Equal(-32601, root.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("abc", root.GetProperty("id").GetString());
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound_WithNumberId()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","id":42,"method":"nope"}""");

        var root = Parse(result);
        Assert.Equal(-32601, root.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("id").ValueKind);
        Assert.Equal(42, root.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task ToolsMethods_AreNotYetImplemented()
    {
        var result = await SendAsync("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        Assert.Equal(-32601, Parse(result).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Batch_ReturnsArrayWithoutNotifications()
    {
        var result = await SendAsync("""
            [
              {"jsonrpc":"2.0","id":1,"method":"ping"},
              {"jsonrpc":"2.0","method":"notifications/initialized"},
              {"jsonrpc":"2.0","id":"x","method":"nope"}
            ]
            """);

        Assert.Equal(200, result.StatusCode);
        var root = Parse(result);
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(2, root.GetArrayLength());
        Assert.Equal(1, root[0].GetProperty("id").GetInt32());
        Assert.Equal(-32601, root[1].GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Batch_OnlyNotifications_Returns202()
    {
        var result = await SendAsync("""[{"jsonrpc":"2.0","method":"notifications/initialized"}]""");

        Assert.Equal(202, result.StatusCode);
        Assert.Null(result.Body);
    }

    [Fact]
    public async Task EmptyBatch_ReturnsInvalidRequest()
    {
        var result = await SendAsync("[]");

        var root = Parse(result);
        Assert.Equal(-32600, root.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("id").ValueKind);
    }
}
