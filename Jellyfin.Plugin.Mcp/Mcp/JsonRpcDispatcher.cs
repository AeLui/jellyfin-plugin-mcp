using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Parses JSON-RPC 2.0 messages and dispatches them to MCP method handlers.
/// </summary>
public sealed class JsonRpcDispatcher
{
    private const string Instructions = "These tools act on Jellyfin as the user that owns the access token, limited to that user's permissions and the token's scopes.";

    private readonly ToolRegistry _registry;
    private readonly McpServerInfo _info;
    private readonly Action<string, Exception>? _onToolFailure;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcDispatcher"/> class.
    /// </summary>
    /// <param name="registry">The tool registry.</param>
    /// <param name="info">The server identity.</param>
    /// <param name="onToolFailure">Optional hook invoked with the tool name and the exception when a tool throws an unexpected exception.</param>
    public JsonRpcDispatcher(ToolRegistry registry, McpServerInfo info, Action<string, Exception>? onToolFailure = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(info);
        _registry = registry;
        _info = info;
        _onToolFailure = onToolFailure;
    }

    /// <summary>
    /// Dispatches one HTTP request body (a single message or a batch).
    /// </summary>
    /// <param name="body">The raw request body.</param>
    /// <param name="principal">The authenticated caller.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The status code and optional response body.</returns>
    public async Task<DispatchResult> DispatchAsync(ReadOnlyMemory<byte> body, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ct.ThrowIfCancellationRequested();

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
        if (message.ValueKind != JsonValueKind.Object)
        {
            return JsonRpcMessages.Error(default, JsonRpcErrorCodes.InvalidRequest, "Invalid request");
        }

        var hasId = message.TryGetProperty("id", out var id);
        var validId = hasId && id.ValueKind is JsonValueKind.String or JsonValueKind.Number;
        var responseId = validId ? id : default;

        if (hasId && !validId && id.ValueKind != JsonValueKind.Null)
        {
            return JsonRpcMessages.Error(default, JsonRpcErrorCodes.InvalidRequest, "Invalid request id");
        }

        if (!message.TryGetProperty("jsonrpc", out var version)
            || version.ValueKind != JsonValueKind.String
            || version.GetString() != "2.0"
            || !message.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String)
        {
            return JsonRpcMessages.Error(responseId, JsonRpcErrorCodes.InvalidRequest, "Invalid request");
        }

        if (!hasId)
        {
            // Notifications never get a response.
            return null;
        }

        var method = methodElement.GetString()!;
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
                "tools/list" => JsonRpcMessages.Success(responseId, writer => WriteToolsList(writer, principal)),
                "tools/call" => await HandleToolsCallAsync(responseId, parameters, principal, ct).ConfigureAwait(false),
                _ => JsonRpcMessages.Error(responseId, JsonRpcErrorCodes.MethodNotFound, "Method not found: " + method),
            };
        }
        catch (OperationCanceledException)
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

    private void WriteToolsList(Utf8JsonWriter writer, McpPrincipal principal)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("tools");
        foreach (var tool in _registry.ListFor(principal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteString("description", tool.Description);
            writer.WritePropertyName("inputSchema");
            tool.InputSchema.WriteTo(writer);
            writer.WritePropertyName("annotations");
            JsonSerializer.Serialize(writer, tool.Annotations);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private async Task<byte[]> HandleToolsCallAsync(JsonElement id, JsonElement parameters, McpPrincipal principal, CancellationToken ct)
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
        try
        {
            result = await tool.ExecuteAsync(new ToolArguments(argumentsElement), principal, ct).ConfigureAwait(false);
        }
        catch (ToolException ex)
        {
            result = ToolResult.Error(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // An unexpected tool failure must become an isError result, without leaking details.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _onToolFailure?.Invoke(name, ex);
            result = ToolResult.Error("Internal error while running " + name);
        }

        return JsonRpcMessages.Success(id, writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("content");
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", result.Text);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteBoolean("isError", result.IsError);
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
        writer.WriteEndObject();
        writer.WriteStartObject("serverInfo");
        writer.WriteString("name", _info.Name);
        writer.WriteString("version", _info.Version);
        writer.WriteEndObject();
        writer.WriteString("instructions", Instructions);
        writer.WriteEndObject();
    }
}
