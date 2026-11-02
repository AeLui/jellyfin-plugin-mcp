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

    private static readonly McpPrincipal LibraryPrincipal = new(Guid.NewGuid(), new System.Collections.Generic.HashSet<string> { McpScopes.Library });

    private static JsonRpcDispatcher CreateDispatcher(
        System.Collections.Generic.IEnumerable<IMcpTool>? tools = null,
        Action<string, Exception>? onToolFailure = null)
        => new(new ToolRegistry(tools ?? Array.Empty<IMcpTool>()), new McpServerInfo("jellyfin-mcp", "1.2.3"), onToolFailure);

    private static async Task<DispatchResult> SendAsync(string json)
    {
        return await CreateDispatcher().DispatchAsync(Encoding.UTF8.GetBytes(json), Principal, CancellationToken.None);
    }

    private static async Task<DispatchResult> CallAsync(
        JsonRpcDispatcher dispatcher,
        string json,
        CancellationToken ct = default)
    {
        return await dispatcher.DispatchAsync(Encoding.UTF8.GetBytes(json), LibraryPrincipal, ct);
    }

    private static JsonRpcDispatcher WithTool(FakeTool tool, Action<string, Exception>? onToolFailure = null)
        => CreateDispatcher(new IMcpTool[] { tool }, onToolFailure);

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
    public async Task ToolsList_IncludesAnnotationsAndSchema()
    {
        var dispatcher = CreateDispatcher(new IMcpTool[]
        {
            new FakeTool("jellyfin_b", McpScopes.Library),
            new FakeTool("jellyfin_a", McpScopes.Library),
            new FakeTool("jellyfin_admin", McpScopes.Admin),
        });

        var result = await CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");

        var tools = Parse(result).GetProperty("result").GetProperty("tools");
        Assert.Equal(2, tools.GetArrayLength());
        var first = tools[0];
        Assert.Equal("jellyfin_a", first.GetProperty("name").GetString());
        Assert.Equal("Fake jellyfin_a", first.GetProperty("description").GetString());
        Assert.Equal("object", first.GetProperty("inputSchema").GetProperty("type").GetString());
        var annotations = first.GetProperty("annotations");
        Assert.True(annotations.GetProperty("readOnlyHint").GetBoolean());
        Assert.False(annotations.GetProperty("destructiveHint").GetBoolean());
        Assert.True(annotations.GetProperty("idempotentHint").GetBoolean());
        Assert.Equal("jellyfin_b", tools[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ToolsCall_Success_ReturnsTextContent()
    {
        var dispatcher = WithTool(new FakeTool("jellyfin_t", McpScopes.Library, (args, _) =>
            Task.FromResult(ToolResult.Json(new { Echo = args.GetString("q") }))));

        var result = await CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"jellyfin_t","arguments":{"q":"hi"}}}""");

        var payload = Parse(result).GetProperty("result");
        Assert.False(payload.GetProperty("isError").GetBoolean());
        var content = Assert.Single(payload.GetProperty("content").EnumerateArray());
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Equal("""{"echo":"hi"}""", content.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ToolsCall_MissingArguments_PassesEmptyArguments()
    {
        var dispatcher = WithTool(new FakeTool("jellyfin_t", McpScopes.Library, (args, _) =>
            Task.FromResult(ToolResult.Json(new { Has = args.GetString("q") is not null }))));

        var result = await CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"jellyfin_t"}}""");

        Assert.False(Parse(result).GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task ToolsCall_OutOfScope_ReturnsUnknownToolError()
    {
        var dispatcher = CreateDispatcher(new IMcpTool[] { new FakeTool("jellyfin_admin", McpScopes.Admin) });

        var outOfScope = await CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"jellyfin_admin"}}""");
        var missing = await CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"jellyfin_admin"}}""".Replace("jellyfin_admin", "jellyfin_missing", StringComparison.Ordinal));

        var error = Parse(outOfScope).GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Equal("Unknown tool: jellyfin_admin", error.GetProperty("message").GetString());
        Assert.Equal(
            "Unknown tool: jellyfin_missing",
            Parse(missing).GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task ToolsCall_MissingName_ReturnsInvalidParams()
    {
        var result = await CallAsync(CreateDispatcher(), """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{}}""");

        Assert.Equal(-32602, Parse(result).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task ToolsCall_ToolException_ReturnsIsError()
    {
        var dispatcher = WithTool(new FakeTool("jellyfin_t", McpScopes.Library, (_, _) => throw new ToolException("Item not found")));

        var result = await CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"jellyfin_t"}}""");

        var payload = Parse(result).GetProperty("result");
        Assert.True(payload.GetProperty("isError").GetBoolean());
        Assert.Equal("Item not found", payload.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ToolsCall_InvalidArgument_ReturnsIsErrorNamingArgument()
    {
        var dispatcher = WithTool(new FakeTool("jellyfin_t", McpScopes.Library, (args, _) =>
            Task.FromResult(ToolResult.Json(new { Id = args.GetRequiredGuid("itemId") }))));

        var result = await CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"jellyfin_t","arguments":{"itemId":"x"}}}""");

        var payload = Parse(result).GetProperty("result");
        Assert.True(payload.GetProperty("isError").GetBoolean());
        Assert.Contains("'itemId'", payload.GetProperty("content")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolsCall_UnexpectedException_ReturnsGenericIsError()
    {
        Exception? logged = null;
        string? loggedTool = null;
        var dispatcher = WithTool(
            new FakeTool("jellyfin_t", McpScopes.Library, (_, _) => throw new InvalidOperationException("secret db path C:\\data")),
            (name, ex) =>
            {
                loggedTool = name;
                logged = ex;
            });

        var result = await CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"jellyfin_t"}}""");

        var payload = Parse(result).GetProperty("result");
        Assert.True(payload.GetProperty("isError").GetBoolean());
        var text = payload.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Equal("Internal error while running jellyfin_t", text);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
        Assert.Equal("jellyfin_t", loggedTool);
        Assert.IsType<InvalidOperationException>(logged);
    }

    [Fact]
    public async Task ToolsCall_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var dispatcher = WithTool(new FakeTool("jellyfin_t", McpScopes.Library, async (_, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return ToolResult.Error("unreachable");
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CallAsync(dispatcher, """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"jellyfin_t"}}""", cts.Token));
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
