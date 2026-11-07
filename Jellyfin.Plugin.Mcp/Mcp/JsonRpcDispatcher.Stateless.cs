using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Stateless MCP (2026-07-28 and later): no handshake, every request carries its version in a header and in
/// <c>_meta</c>, and errors map to HTTP status codes.
/// </summary>
public sealed partial class JsonRpcDispatcher
{
    /// <summary>How long clients may cache <c>server/discover</c> and <c>tools/list</c> results.</summary>
    private const int CacheTtlMs = 300_000;

    private const string ServerInfoKey = "io.modelcontextprotocol/serverInfo";

    private async Task<DispatchResult> DispatchStatelessAsync(ReadOnlyMemory<byte> body, McpPrincipal principal, McpRequestHeaders headers, CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Status(400, JsonRpcMessages.Error(default, JsonRpcErrorCodes.ParseError, "Parse error"));
        }

        using (document)
        {
            var message = document.RootElement;
            if (message.ValueKind == JsonValueKind.Array)
            {
                return Status(400, JsonRpcMessages.Error(default, JsonRpcErrorCodes.InvalidRequest, "Invalid request: batches are not supported"));
            }

            var envelope = ReadEnvelope(message);
            if (envelope.Error is not null)
            {
                return Status(400, envelope.Error);
            }

            if (!envelope.HasId)
            {
                return new DispatchResult(202, null);
            }

            var id = envelope.Id;
            if (StatelessRequestValidator.Validate(message, headers) is { } invalid)
            {
                return Status(400, JsonRpcMessages.Error(id, invalid.Code, invalid.Message));
            }

            message.TryGetProperty("params", out var parameters);
            try
            {
                return envelope.Method switch
                {
                    "server/discover" => Status(200, JsonRpcMessages.Success(id, WriteDiscoverResult)),
                    "tools/list" => Status(200, JsonRpcMessages.Success(id, writer => WriteToolsList(writer, principal, true))),
                    "tools/call" => Status(200, await HandleToolsCallAsync(id, parameters, principal, true, ct).ConfigureAwait(false)),
                    "prompts/list" => Status(200, JsonRpcMessages.Success(id, writer => WritePromptsList(writer, principal, true))),
                    "prompts/get" => Status(200, HandlePromptsGet(id, parameters, principal, true)),
                    _ => Status(404, JsonRpcMessages.Error(id, JsonRpcErrorCodes.MethodNotFound, "Method not found: " + envelope.Method)),
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // A handler failure must become a JSON-RPC error, not an unhandled exception.
            catch (Exception)
#pragma warning restore CA1031
            {
                return Status(500, JsonRpcMessages.Error(id, JsonRpcErrorCodes.InternalError, "Internal error"));
            }
        }
    }

    /// <summary>
    /// Answers a request whose <c>MCP-Protocol-Version</c> header names no supported version.
    /// </summary>
    private static DispatchResult UnsupportedVersion(ReadOnlyMemory<byte> body, string requested)
    {
        JsonElement id = default;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var envelope = ReadEnvelope(document.RootElement);
                id = envelope.Id.ValueKind == JsonValueKind.Undefined ? default : envelope.Id.Clone();
            }
        }
        catch (JsonException)
        {
            // The version error wins; an unreadable body just leaves the id null.
        }

        return Status(400, JsonRpcMessages.Error(id, JsonRpcErrorCodes.UnsupportedProtocolVersion, "Unsupported protocol version", writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("supported");
            foreach (var version in ProtocolVersions.All)
            {
                writer.WriteStringValue(version);
            }

            writer.WriteEndArray();
            writer.WriteString("requested", requested);
            writer.WriteEndObject();
        }));
    }

    /// <summary>
    /// Reads the JSON-RPC envelope of one message; <see cref="Envelope.Error"/> is set when it is not a valid request.
    /// </summary>
    private static Envelope ReadEnvelope(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return new Envelope(default, null, false, JsonRpcMessages.Error(default, JsonRpcErrorCodes.InvalidRequest, "Invalid request"));
        }

        var hasId = message.TryGetProperty("id", out var id);
        var validId = hasId && id.ValueKind is JsonValueKind.String or JsonValueKind.Number;
        var responseId = validId ? id : default;

        if (hasId && !validId && id.ValueKind != JsonValueKind.Null)
        {
            return new Envelope(default, null, hasId, JsonRpcMessages.Error(default, JsonRpcErrorCodes.InvalidRequest, "Invalid request id"));
        }

        if (!message.TryGetProperty("jsonrpc", out var version)
            || version.ValueKind != JsonValueKind.String
            || version.GetString() != "2.0"
            || !message.TryGetProperty("method", out var methodElement)
            || methodElement.ValueKind != JsonValueKind.String)
        {
            return new Envelope(responseId, null, hasId, JsonRpcMessages.Error(responseId, JsonRpcErrorCodes.InvalidRequest, "Invalid request"));
        }

        return new Envelope(responseId, methodElement.GetString(), hasId, null);
    }

    private static DispatchResult Status(int statusCode, byte[] response) => new(statusCode, Encoding.UTF8.GetString(response));

    private static void WriteCacheHints(Utf8JsonWriter writer)
    {
        writer.WriteNumber("ttlMs", CacheTtlMs);

        // tools/list depends on the token's scopes, so shared caches must not reuse it.
        writer.WriteString("cacheScope", "private");
    }

    private void WriteServerInfoMeta(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("_meta");
        writer.WriteStartObject(ServerInfoKey);
        writer.WriteString("name", _info.Name);
        writer.WriteString("version", _info.Version);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private void WriteDiscoverResult(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("resultType", "complete");
        writer.WriteStartArray("supportedVersions");
        foreach (var version in ProtocolVersions.All)
        {
            writer.WriteStringValue(version);
        }

        writer.WriteEndArray();
        writer.WriteStartObject("capabilities");
        writer.WriteStartObject("tools");
        writer.WriteEndObject();
        writer.WriteStartObject("prompts");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteString("instructions", Instructions);
        WriteCacheHints(writer);
        WriteServerInfoMeta(writer);
        writer.WriteEndObject();
    }

    /// <summary>
    /// The JSON-RPC envelope of one message.
    /// </summary>
    /// <param name="Id">The response id (default when absent or null).</param>
    /// <param name="Method">The method, when the envelope is valid.</param>
    /// <param name="HasId">Whether the message has an <c>id</c> member (otherwise it is a notification).</param>
    /// <param name="Error">The error response when the envelope is invalid.</param>
    private readonly record struct Envelope(JsonElement Id, string? Method, bool HasId, byte[]? Error);
}
