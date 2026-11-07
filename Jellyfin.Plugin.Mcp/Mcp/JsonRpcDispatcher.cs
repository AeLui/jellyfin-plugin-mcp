using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Parses JSON-RPC 2.0 messages and dispatches them to MCP method handlers.
/// </summary>
public sealed partial class JsonRpcDispatcher
{
    private const string Instructions = "These tools act on Jellyfin as the user that owns the access token, limited to that user's permissions and the token's scopes. "
        + "Item ids come from other results: find items with jellyfin_search, jellyfin_browse or jellyfin_list_libraries before passing an id. "
        + "Before jellyfin_play or jellyfin_playback_control, call jellyfin_list_sessions to get the sessionId of the target device. "
        + "To choose audio or subtitles, get track indexes with jellyfin_get_media_tracks. "
        + "List results are paged: use startIndex with totalCount to fetch more. "
        + "For recommendations use jellyfin_similar_items or jellyfin_movie_recommendations; open a collection from jellyfin_list_collections with jellyfin_browse. "
        + "Some tools may be missing when the token lacks their scope.";

    private readonly ToolRegistry _registry;
    private readonly PromptRegistry _prompts;
    private readonly McpServerInfo _info;
    private readonly Action<string, Exception>? _onToolFailure;
    private readonly Action<ToolCallLog>? _onToolCall;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcDispatcher"/> class.
    /// </summary>
    /// <param name="registry">The tool registry.</param>
    /// <param name="prompts">The prompt registry.</param>
    /// <param name="info">The server identity.</param>
    /// <param name="onToolFailure">Optional hook invoked with the tool name and the exception when a tool throws an unexpected exception.</param>
    /// <param name="onToolCall">Optional hook invoked after every <c>tools/call</c> that reached a tool; it never receives arguments.</param>
    public JsonRpcDispatcher(
        ToolRegistry registry,
        PromptRegistry prompts,
        McpServerInfo info,
        Action<string, Exception>? onToolFailure = null,
        Action<ToolCallLog>? onToolCall = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(info);
        _registry = registry;
        _prompts = prompts;
        _info = info;
        _onToolFailure = onToolFailure;
        _onToolCall = onToolCall;
    }

    /// <summary>
    /// Dispatches one HTTP request body (a single message or a batch).
    /// </summary>
    /// <param name="body">The raw request body.</param>
    /// <param name="principal">The authenticated caller.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The status code and optional response body.</returns>
    public Task<DispatchResult> DispatchAsync(ReadOnlyMemory<byte> body, McpPrincipal principal, CancellationToken ct)
        => DispatchAsync(body, principal, McpRequestHeaders.None, ct);

    /// <summary>
    /// Dispatches one HTTP body, choosing the protocol era from the <c>MCP-Protocol-Version</c> header.
    /// </summary>
    /// <param name="body">The raw request body.</param>
    /// <param name="principal">The authenticated caller.</param>
    /// <param name="headers">The MCP request headers.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The status code and optional response body.</returns>
    public async Task<DispatchResult> DispatchAsync(ReadOnlyMemory<byte> body, McpPrincipal principal, McpRequestHeaders headers, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(headers);
        ct.ThrowIfCancellationRequested();

        if (headers.ProtocolVersion is { } version)
        {
            if (ProtocolVersions.IsStateless(version))
            {
                return await DispatchStatelessAsync(body, principal, headers, ct).ConfigureAwait(false);
            }

            if (!ProtocolVersions.IsHandshake(version))
            {
                return UnsupportedVersion(body, version);
            }
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Error(default, JsonRpcErrorCodes.ParseError, "Parse error");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return await DispatchBatchAsync(root, principal, ct).ConfigureAwait(false);
            }

            var response = await HandleMessageAsync(root, principal, ct).ConfigureAwait(false);
            return response is null
                ? new DispatchResult(202, null)
                : new DispatchResult(200, Encoding.UTF8.GetString(response));
        }
    }

    private static DispatchResult Error(JsonElement id, int code, string message)
        => new(200, Encoding.UTF8.GetString(JsonRpcMessages.Error(id, code, message)));

    private async Task<DispatchResult> DispatchBatchAsync(JsonElement batch, McpPrincipal principal, CancellationToken ct)
    {
        if (batch.GetArrayLength() == 0)
        {
            return Error(default, JsonRpcErrorCodes.InvalidRequest, "Empty batch");
        }

        var responses = new List<string>();
        foreach (var message in batch.EnumerateArray())
        {
            var response = await HandleMessageAsync(message, principal, ct).ConfigureAwait(false);
            if (response is not null)
            {
                responses.Add(Encoding.UTF8.GetString(response));
            }
        }

        return responses.Count == 0
            ? new DispatchResult(202, null)
            : new DispatchResult(200, "[" + string.Join(',', responses) + "]");
    }

    /// <summary>
    /// Handles one message; returns null when no response is due (notification).
    /// </summary>
    private async Task<byte[]?> HandleMessageAsync(JsonElement message, McpPrincipal principal, CancellationToken ct)
    {
        var envelope = ReadEnvelope(message);
        if (envelope.Error is not null)
        {
            return envelope.Error;
        }

        if (!envelope.HasId)
        {
            // Notifications never get a response.
            return null;
        }

        var responseId = envelope.Id;
        var method = envelope.Method!;
        message.TryGetProperty("params", out var parameters);
        try
        {
            return method switch
            {
                "initialize" => JsonRpcMessages.Success(responseId, writer => WriteInitializeResult(writer, parameters)),
                "ping" => JsonRpcMessages.Success(responseId, writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                }),
                "tools/list" => JsonRpcMessages.Success(responseId, writer => WriteToolsList(writer, principal, false)),
                "tools/call" => await HandleToolsCallAsync(responseId, parameters, principal, false, ct).ConfigureAwait(false),
                "prompts/list" => JsonRpcMessages.Success(responseId, writer => WritePromptsList(writer, principal, false)),
                "prompts/get" => HandlePromptsGet(responseId, parameters, principal, false),
                _ => JsonRpcMessages.Error(responseId, JsonRpcErrorCodes.MethodNotFound, "Method not found: " + method),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // A handler failure must become a JSON-RPC error, not an HTTP 500.
        catch (Exception)
#pragma warning restore CA1031
        {
            return JsonRpcMessages.Error(responseId, JsonRpcErrorCodes.InternalError, "Internal error");
        }
    }

    /// <summary>
    /// Runs an observer hook; a failing hook must never change the outcome of the call.
    /// </summary>
    private static void SafeInvoke(Action hook)
    {
        try
        {
            hook();
        }
#pragma warning disable CA1031 // Observer failures are deliberately swallowed.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void WriteToolsList(Utf8JsonWriter writer, McpPrincipal principal, bool stateless)
    {
        writer.WriteStartObject();
        if (stateless)
        {
            writer.WriteString("resultType", "complete");
        }

        writer.WriteStartArray("tools");
        foreach (var tool in _registry.ListFor(principal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteString("title", tool.Title);
            writer.WriteString("description", tool.Description);
            writer.WritePropertyName("inputSchema");
            tool.InputSchema.WriteTo(writer);
            if (tool.OutputSchema is { } outputSchema)
            {
                writer.WritePropertyName("outputSchema");
                outputSchema.WriteTo(writer);
            }

            writer.WritePropertyName("annotations");
            JsonSerializer.Serialize(writer, tool.Annotations with { Title = tool.Title });
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (stateless)
        {
            WriteCacheHints(writer);
            WriteServerInfoMeta(writer);
        }

        writer.WriteEndObject();
    }

    private async Task<byte[]> HandleToolsCallAsync(JsonElement id, JsonElement parameters, McpPrincipal principal, bool stateless, CancellationToken ct)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
        {
            return JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidParams, "Invalid params: 'name' is required");
        }

        var name = nameElement.GetString()!;
        var tool = _registry.Find(name, principal);
        if (tool is null)
        {
            return JsonRpcMessages.Error(id, JsonRpcErrorCodes.InvalidParams, "Unknown tool: " + name);
        }

        parameters.TryGetProperty("arguments", out var argumentsElement);
        ToolResult result;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            result = await tool.ExecuteAsync(new ToolArguments(argumentsElement), principal, ct).ConfigureAwait(false);
        }
        catch (ToolException ex)
        {
            result = ToolResult.Error(ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // An unexpected tool failure must become an isError result, without leaking details.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SafeInvoke(() => _onToolFailure?.Invoke(name, ex));
            result = ToolResult.Error("Internal error while running " + name);
        }

        stopwatch.Stop();
        SafeInvoke(() => _onToolCall?.Invoke(new ToolCallLog(name, principal.UserId, stopwatch.Elapsed, result.IsError)));

        return JsonRpcMessages.Success(id, writer =>
        {
            writer.WriteStartObject();
            if (stateless)
            {
                writer.WriteString("resultType", "complete");
            }

            writer.WriteStartArray("content");
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", result.Text);
            writer.WriteEndObject();
            writer.WriteEndArray();
            if (result.Structured is { } structured)
            {
                writer.WritePropertyName("structuredContent");
                structured.WriteTo(writer);
            }

            writer.WriteBoolean("isError", result.IsError);
            if (stateless)
            {
                WriteServerInfoMeta(writer);
            }

            writer.WriteEndObject();
        });
    }

    private void WriteInitializeResult(Utf8JsonWriter writer, JsonElement parameters)
    {
        string? requested = null;
        if (parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("protocolVersion", out var versionElement)
            && versionElement.ValueKind == JsonValueKind.String)
        {
            requested = versionElement.GetString();
        }

        writer.WriteStartObject();
        writer.WriteString("protocolVersion", ProtocolVersions.Negotiate(requested));
        writer.WriteStartObject("capabilities");
        writer.WriteStartObject("tools");
        writer.WriteBoolean("listChanged", false);
        writer.WriteEndObject();
        writer.WriteStartObject("prompts");
        writer.WriteBoolean("listChanged", false);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartObject("serverInfo");
        writer.WriteString("name", _info.Name);
        writer.WriteString("version", _info.Version);
        writer.WriteEndObject();
        writer.WriteString("instructions", Instructions);
        writer.WriteEndObject();
    }
}
