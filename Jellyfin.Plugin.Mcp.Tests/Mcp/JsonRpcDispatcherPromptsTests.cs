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

public class JsonRpcDispatcherPromptsTests
{
    private static readonly McpPrincipal LibraryPrincipal = new(Guid.NewGuid(), new HashSet<string> { McpScopes.Library });

    private static JsonRpcDispatcher Dispatcher(params IMcpPrompt[] extra)
    {
        var prompts = new List<IMcpPrompt>
        {
            new FakePrompt("p_lib", McpScopes.Library),
            new FakePrompt("p_admin", McpScopes.Admin),
            new FakePrompt("p_req", McpScopes.Library, new[] { new PromptArgument("who", "Who", "Who it is for", true) }),
        };
        prompts.AddRange(extra);
        return new JsonRpcDispatcher(new ToolRegistry(Array.Empty<IMcpTool>()), new PromptRegistry(prompts), new McpServerInfo("jellyfin-mcp", "1.2.3"));
    }

    private static async Task<JsonElement> Send(string json, JsonRpcDispatcher? dispatcher = null)
    {
        var result = await (dispatcher ?? Dispatcher()).DispatchAsync(Encoding.UTF8.GetBytes(json), LibraryPrincipal, CancellationToken.None);
        Assert.Equal(200, result.StatusCode);
        return JsonDocument.Parse(result.Body!).RootElement.Clone();
    }

    private static Task<JsonElement> Get(string paramsJson, JsonRpcDispatcher? dispatcher = null)
        => Send("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"prompts/get\",\"params\":" + paramsJson + "}", dispatcher);

    private static void AssertInvalidParams(JsonElement response, string message)
    {
        var error = response.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Equal(message, error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Initialize_AdvertisesPrompts()
    {
        var response = await Send("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25"}}""");

        Assert.False(response.GetProperty("result").GetProperty("capabilities").GetProperty("prompts").GetProperty("listChanged").GetBoolean());
    }

    [Fact]
    public async Task PromptsList_FiltersByScope()
    {
        var result = (await Send("""{"jsonrpc":"2.0","id":1,"method":"prompts/list"}""")).GetProperty("result");

        var prompts = result.GetProperty("prompts").EnumerateArray().ToList();
        Assert.Equal(new[] { "p_lib", "p_req" }, prompts.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("Fake p_lib", prompts[0].GetProperty("title").GetString());
        Assert.Equal("Fake prompt p_lib", prompts[0].GetProperty("description").GetString());
        Assert.Equal(0, prompts[0].GetProperty("arguments").GetArrayLength());
        var argument = Assert.Single(prompts[1].GetProperty("arguments").EnumerateArray());
        Assert.Equal("who", argument.GetProperty("name").GetString());
        Assert.Equal("Who", argument.GetProperty("title").GetString());
        Assert.Equal("Who it is for", argument.GetProperty("description").GetString());
        Assert.True(argument.GetProperty("required").GetBoolean());
        Assert.False(result.TryGetProperty("nextCursor", out _));
        Assert.False(result.TryGetProperty("resultType", out _));
    }

    [Fact]
    public async Task PromptsGet_ReturnsUserTextMessage()
    {
        var result = (await Get("""{"name":"p_lib","arguments":{}}""")).GetProperty("result");

        Assert.Equal("Fake p_lib", result.GetProperty("description").GetString());
        var message = Assert.Single(result.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("text", message.GetProperty("content").GetProperty("type").GetString());
        Assert.Equal("Text of p_lib", message.GetProperty("content").GetProperty("text").GetString());
        Assert.False(result.TryGetProperty("resultType", out _));
    }

    [Theory]
    [InlineData("""{"name":"p_lib"}""")]
    [InlineData("""{"name":"p_lib","arguments":null}""")]
    public async Task PromptsGet_WithoutArguments_Succeeds(string parameters)
    {
        var response = await Get(parameters);

        Assert.Equal("Text of p_lib", response.GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"name":3}""")]
    [InlineData("[]")]
    public async Task PromptsGet_MissingName(string parameters)
    {
        AssertInvalidParams(await Get(parameters), "Invalid params: 'name' is required");
    }

    [Fact]
    public async Task PromptsGet_UnknownPrompt()
    {
        AssertInvalidParams(await Get("""{"name":"nope"}"""), "Unknown prompt: nope");
    }

    [Fact]
    public async Task PromptsGet_OutOfScope_IsUnknownPrompt()
    {
        AssertInvalidParams(await Get("""{"name":"p_admin"}"""), "Unknown prompt: p_admin");
    }

    [Theory]
    [InlineData("""{"name":"p_req"}""")]
    [InlineData("""{"name":"p_req","arguments":{"who":""}}""")]
    public async Task PromptsGet_MissingRequiredArgument(string parameters)
    {
        AssertInvalidParams(await Get(parameters), "Missing required argument: who");
    }

    [Fact]
    public async Task PromptsGet_RequiredArgumentPresent_Renders()
    {
        var response = await Get("""{"name":"p_req","arguments":{"who":"me"}}""");

        Assert.True(response.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task PromptsGet_NonObjectArguments_IsInvalidParams()
    {
        AssertInvalidParams(await Get("""{"name":"p_lib","arguments":[]}"""), "Invalid params: 'arguments' must be an object");
    }

    [Fact]
    public async Task PromptsGet_PromptException_IsInvalidParams()
    {
        var failing = new FakePrompt("p_bad", McpScopes.Library, render: (_, _) => throw new PromptException("Invalid argument 'x': must be a string"));

        AssertInvalidParams(await Get("""{"name":"p_bad"}""", Dispatcher(failing)), "Invalid argument 'x': must be a string");
    }

    [Fact]
    public async Task PromptsGet_UnexpectedException_IsInternalError()
    {
        var failing = new FakePrompt("p_boom", McpScopes.Library, render: (_, _) => throw new InvalidOperationException("secret detail"));

        var error = (await Get("""{"name":"p_boom"}""", Dispatcher(failing))).GetProperty("error");

        Assert.Equal(-32603, error.GetProperty("code").GetInt32());
        Assert.Equal("Internal error", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PromptsGet_InBatch_Works()
    {
        var response = await Send("""[{"jsonrpc":"2.0","id":1,"method":"prompts/list"},{"jsonrpc":"2.0","id":2,"method":"prompts/get","params":{"name":"p_lib"}}]""");

        Assert.Equal(2, response.GetArrayLength());
        Assert.Equal("Text of p_lib", response[1].GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString());
    }
}
