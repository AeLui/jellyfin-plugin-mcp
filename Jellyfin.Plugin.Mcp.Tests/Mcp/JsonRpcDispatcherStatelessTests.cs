using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class JsonRpcDispatcherStatelessTests
{
    private const string Version = "2026-07-28";
    private const string Meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";

    private const string Instructions = "These tools act on Jellyfin as the user that owns the access token, limited to that user's permissions and the token's scopes. Item ids come from other results: find items with jellyfin_search, jellyfin_browse or jellyfin_list_libraries before passing an id. Before jellyfin_play or jellyfin_playback_control, call jellyfin_list_sessions to get the sessionId of the target device. List results are paged: use startIndex with totalCount to fetch more. For recommendations use jellyfin_similar_items or jellyfin_movie_recommendations; open a collection from jellyfin_list_collections with jellyfin_browse. Some tools may be missing when the token lacks their scope.";

    private static readonly McpPrincipal Principal = new(Guid.NewGuid(), new HashSet<string> { McpScopes.Library });

    private static JsonRpcDispatcher Dispatcher()
        => new(new ToolRegistry(new IMcpTool[] { new FakeTool("jellyfin_t", McpScopes.Library) }), new McpServerInfo("jellyfin-mcp", "1.2.3"));

    private static string Request(string method, string? extraParams = null, int id = 1)
        => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":{" + (extraParams is null ? string.Empty : extraParams + ",") + Meta + "}}";

    private static Task<DispatchResult> Send(string body, string? method, string? name = null, string? version = Version)
        => Dispatcher().DispatchAsync(Encoding.UTF8.GetBytes(body), Principal, new McpRequestHeaders(version, method, name), CancellationToken.None);

    private static JsonElement Parse(DispatchResult result)
    {
        Assert.NotNull(result.Body);
        return JsonDocument.Parse(result.Body!).RootElement.Clone();
    }

    private static void AssertServerInfo(JsonElement result)
        => Assert.Equal("jellyfin-mcp", result.GetProperty("_meta").GetProperty("io.modelcontextprotocol/serverInfo").GetProperty("name").GetString());

    [Fact]
    public async Task Stateless_Discover_ReturnsCapabilitiesAndCacheHints()
    {
        var response = await Send(Request("server/discover"), "server/discover");

        Assert.Equal(200, response.StatusCode);
        var result = Parse(response).GetProperty("result");
        Assert.Equal("complete", result.GetProperty("resultType").GetString());
        Assert.Equal(new[] { "2026-07-28", "2025-11-25", "2025-06-18", "2025-03-26" }, result.GetProperty("supportedVersions").EnumerateArray().Select(v => v.GetString()));
        var tools = result.GetProperty("capabilities").GetProperty("tools");
        Assert.Equal(JsonValueKind.Object, tools.ValueKind);
        Assert.Empty(tools.EnumerateObject());
        Assert.Equal(Instructions, result.GetProperty("instructions").GetString());
        Assert.Equal(300000, result.GetProperty("ttlMs").GetInt32());
        Assert.Equal("private", result.GetProperty("cacheScope").GetString());
        AssertServerInfo(result);
    }

    [Fact]
    public async Task Stateless_ToolsList_HasResultTypeAndCacheHints()
    {
        var response = await Send(Request("tools/list"), "tools/list");

        var result = Parse(response).GetProperty("result");
        Assert.Equal("complete", result.GetProperty("resultType").GetString());
        Assert.Equal("jellyfin_t", Assert.Single(result.GetProperty("tools").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(300000, result.GetProperty("ttlMs").GetInt32());
        Assert.Equal("private", result.GetProperty("cacheScope").GetString());
        AssertServerInfo(result);
    }

    [Fact]
    public async Task Stateless_ToolsCall_HasResultTypeAndServerInfo()
    {
        var response = await Send(Request("tools/call", "\"name\":\"jellyfin_t\""), "tools/call", "jellyfin_t");

        Assert.Equal(200, response.StatusCode);
        var result = Parse(response).GetProperty("result");
        Assert.Equal("complete", result.GetProperty("resultType").GetString());
        Assert.True(result.GetProperty("structuredContent").GetProperty("ok").GetBoolean());
        Assert.False(result.GetProperty("isError").GetBoolean());
        AssertServerInfo(result);
    }

    [Fact]
    public async Task Stateless_UnknownTool_ReturnsInvalidParamsWith200()
    {
        var response = await Send(Request("tools/call", "\"name\":\"jellyfin_missing\""), "tools/call", "jellyfin_missing");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(-32602, Parse(response).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Theory]
    [InlineData("initialize")]
    [InlineData("ping")]
    [InlineData("subscriptions/listen")]
    public async Task Stateless_UnsupportedMethods_Return404MethodNotFound(string method)
    {
        var response = await Send(Request(method), method);

        Assert.Equal(404, response.StatusCode);
        Assert.Equal(-32601, Parse(response).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Stateless_Batch_Returns400InvalidRequest()
    {
        var response = await Send("[" + Request("tools/list") + "]", "tools/list");

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(-32600, Parse(response).GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Stateless_Notification_Returns202WithoutHeaders()
    {
        var response = await Send("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{" + Meta + "}}", null);

        Assert.Equal(202, response.StatusCode);
        Assert.Null(response.Body);
    }

    [Fact]
    public async Task Stateless_ValidationError_Returns400()
    {
        var response = await Send(Request("tools/list"), "tools/call");

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(-32020, Parse(response).GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(1, Parse(response).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task UnknownProtocolVersionHeader_Returns400WithSupportedList()
    {
        var response = await Send(Request("tools/list"), "tools/list", version: "1999-01-01");

        Assert.Equal(400, response.StatusCode);
        var error = Parse(response).GetProperty("error");
        Assert.Equal(-32022, error.GetProperty("code").GetInt32());
        Assert.Equal(new[] { "2026-07-28", "2025-11-25", "2025-06-18", "2025-03-26" }, error.GetProperty("data").GetProperty("supported").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("1999-01-01", error.GetProperty("data").GetProperty("requested").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2025-06-18")]
    public async Task Legacy_WithAndWithoutHeader_Unchanged(string? version)
    {
        foreach (var body in new[]
        {
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"jellyfin_t\"}}",
            "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"ping\"}",
        })
        {
            var response = await Send(body, null, version: version);

            Assert.Equal(200, response.StatusCode);
            var result = Parse(response).GetProperty("result");
            Assert.False(result.TryGetProperty("resultType", out _));
            Assert.False(result.TryGetProperty("_meta", out _));
        }
    }

    [Fact]
    public async Task Initialize_ReturnsNewInstructions()
    {
        var response = await Send("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}", null, version: null);

        Assert.Equal(Instructions, Parse(response).GetProperty("result").GetProperty("instructions").GetString());
    }
}
