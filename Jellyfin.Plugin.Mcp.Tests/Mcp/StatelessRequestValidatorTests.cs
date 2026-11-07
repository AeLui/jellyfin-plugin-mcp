using System;
using System.Text.Json;
using Jellyfin.Plugin.Mcp.Mcp;
using Xunit;

namespace Jellyfin.Plugin.Mcp.Tests.Mcp;

public class StatelessRequestValidatorTests
{
    private const string Meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";

    private static JsonElement Message(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Call(string name, string meta = Meta, string arguments = "{}")
        => Message("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"" + name + "\",\"arguments\":" + arguments + "," + meta + "}}");

    private static McpRequestHeaders Headers(string? method = "tools/call", string? name = "jellyfin_search", string? version = "2026-07-28")
        => new(version, method, name);

    [Fact]
    public void ValidToolsCall_HasNoError()
    {
        Assert.Null(StatelessRequestValidator.Validate(Call("jellyfin_search"), Headers()));
    }

    [Fact]
    public void ValidToolsList_NeedsNoNameHeader()
    {
        var message = Message("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{" + Meta + "}}");

        Assert.Null(StatelessRequestValidator.Validate(message, Headers("tools/list", null)));
    }

    [Fact]
    public void MissingMetaProtocolVersion_IsInvalidParams()
    {
        var error = StatelessRequestValidator.Validate(Call("jellyfin_search", "\"_meta\":{\"io.modelcontextprotocol/clientCapabilities\":{}}"), Headers());

        Assert.Equal(-32602, error!.Code);
        Assert.Contains("io.modelcontextprotocol/protocolVersion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingParams_IsInvalidParams()
    {
        var error = StatelessRequestValidator.Validate(Message("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"), Headers("tools/list", null));

        Assert.Equal(-32602, error!.Code);
    }

    [Fact]
    public void ClientCapabilitiesNotAnObject_IsInvalidParams()
    {
        var meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":true}";

        var error = StatelessRequestValidator.Validate(Call("jellyfin_search", meta), Headers());

        Assert.Equal(-32602, error!.Code);
        Assert.Contains("io.modelcontextprotocol/clientCapabilities", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MetaVersionDiffersFromHeader_IsHeaderMismatch()
    {
        var meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2025-11-25\",\"io.modelcontextprotocol/clientCapabilities\":{}}";

        Assert.Equal(-32020, StatelessRequestValidator.Validate(Call("jellyfin_search", meta), Headers())!.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tools/list")]
    public void MethodHeaderMissingOrDifferent_IsHeaderMismatch(string? method)
    {
        var error = StatelessRequestValidator.Validate(Call("jellyfin_search"), Headers(method));

        Assert.Equal(-32020, error!.Code);
        Assert.Contains("Mcp-Method", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other")]
    public void NameHeaderMissingOrDifferent_IsHeaderMismatch(string? name)
    {
        var error = StatelessRequestValidator.Validate(Call("jellyfin_search"), Headers(name: name));

        Assert.Equal(-32020, error!.Code);
        Assert.Contains("Mcp-Name", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Base64EncodedNameHeader_IsDecoded()
    {
        Assert.Null(StatelessRequestValidator.Validate(Call("jellyfin_search"), Headers(name: "=?base64?amVsbHlmaW5fc2VhcmNo?=")));
    }

    [Fact]
    public void MalformedBase64Header_IsMismatchNotException()
    {
        Assert.Equal(-32020, StatelessRequestValidator.Validate(Call("jellyfin_search"), Headers(name: "=?base64?!!!?="))!.Code);
        Assert.Null(StatelessRequestValidator.DecodeHeader("=?base64?!!!?="));
    }

    [Fact]
    public void Errors_NeverEchoArgumentValues()
    {
        var error = StatelessRequestValidator.Validate(Call("jellyfin_search", arguments: "{\"secret\":\"s3cr3t\"}"), Headers(name: "other"));

        Assert.DoesNotContain("s3cr3t", error!.Message, StringComparison.Ordinal);
    }

    private static JsonElement GetPrompt(string name)
        => Message("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"prompts/get\",\"params\":{\"name\":\"" + name + "\"," + Meta + "}}");

    [Fact]
    public void PromptsGet_MissingMcpName()
    {
        var error = StatelessRequestValidator.Validate(GetPrompt("movie_night"), Headers("prompts/get", null));

        Assert.Equal(-32020, error!.Code);
        Assert.Equal("Header mismatch: missing Mcp-Name header", error.Message);
    }

    [Fact]
    public void PromptsGet_MismatchedMcpName()
    {
        var error = StatelessRequestValidator.Validate(GetPrompt("movie_night"), Headers("prompts/get", "whats_new"));

        Assert.Equal(-32020, error!.Code);
        Assert.Equal("Header mismatch: Mcp-Name header does not match the request body", error.Message);
    }

    [Fact]
    public void PromptsGet_MatchingMcpName_IsValid()
    {
        Assert.Null(StatelessRequestValidator.Validate(GetPrompt("movie_night"), Headers("prompts/get", "movie_night")));
    }

    [Fact]
    public void PromptsGet_Base64McpName_Matches()
    {
        var header = "=?base64?" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("película")) + "?=";

        Assert.Null(StatelessRequestValidator.Validate(GetPrompt("película"), Headers("prompts/get", header)));
    }

    [Fact]
    public void PromptsList_NeedsNoMcpName()
    {
        var message = Message("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"prompts/list\",\"params\":{" + Meta + "}}");

        Assert.Null(StatelessRequestValidator.Validate(message, Headers("prompts/list", null)));
    }
}
