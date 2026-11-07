using System;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Validates a stateless (MCP 2026-07-28) request: the required <c>_meta</c> fields and the HTTP headers that must
/// mirror the request body.
/// </summary>
public static class StatelessRequestValidator
{
    /// <summary>The <c>_meta</c> key holding the protocol version.</summary>
    public const string ProtocolVersionKey = "io.modelcontextprotocol/protocolVersion";

    /// <summary>The <c>_meta</c> key holding the client capabilities.</summary>
    public const string ClientCapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";

    private const string Base64Prefix = "=?base64?";
    private const string Base64Suffix = "?=";

    /// <summary>
    /// Validates one JSON-RPC request object (a message with an <c>id</c>).
    /// </summary>
    /// <param name="message">The request.</param>
    /// <param name="headers">The request headers.</param>
    /// <returns>The error to return, or <c>null</c> when the request is valid. Messages never contain argument values.</returns>
    public static StatelessValidationError? Validate(JsonElement message, McpRequestHeaders headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        message.TryGetProperty("params", out var parameters);
        JsonElement meta = default;
        var hasMeta = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("_meta", out meta)
            && meta.ValueKind == JsonValueKind.Object;

        if (!hasMeta
            || !meta.TryGetProperty(ProtocolVersionKey, out var version)
            || version.ValueKind != JsonValueKind.String)
        {
            return new(JsonRpcErrorCodes.InvalidParams, "Invalid params: missing _meta " + ProtocolVersionKey);
        }

        if (!meta.TryGetProperty(ClientCapabilitiesKey, out var capabilities) || capabilities.ValueKind != JsonValueKind.Object)
        {
            return new(JsonRpcErrorCodes.InvalidParams, "Invalid params: missing _meta " + ClientCapabilitiesKey);
        }

        if (!string.Equals(version.GetString(), headers.ProtocolVersion, StringComparison.Ordinal))
        {
            return Mismatch("MCP-Protocol-Version");
        }

        var method = message.GetProperty("method").GetString();
        if (headers.Method is null)
        {
            return Missing("Mcp-Method");
        }

        if (!string.Equals(DecodeHeader(headers.Method), method, StringComparison.Ordinal))
        {
            return Mismatch("Mcp-Method");
        }

        // Mcp-Name mirrors params.name for these methods (resources/read would use params.uri; resources are not served).
        if (string.Equals(method, "tools/call", StringComparison.Ordinal) || string.Equals(method, "prompts/get", StringComparison.Ordinal))
        {
            if (headers.Name is null)
            {
                return Missing("Mcp-Name");
            }

            var name = parameters.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            if (!string.Equals(DecodeHeader(headers.Name), name, StringComparison.Ordinal))
            {
                return Mismatch("Mcp-Name");
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes a header value, including the <c>=?base64?...?=</c> form used for non-ASCII values.
    /// </summary>
    /// <param name="raw">The raw header value.</param>
    /// <returns>The decoded value, or <c>null</c> when the value is missing or malformed.</returns>
    internal static string? DecodeHeader(string? raw)
    {
        if (raw is null
            || !raw.StartsWith(Base64Prefix, StringComparison.Ordinal)
            || !raw.EndsWith(Base64Suffix, StringComparison.Ordinal)
            || raw.Length < Base64Prefix.Length + Base64Suffix.Length)
        {
            return raw;
        }

        var payload = raw.AsSpan(Base64Prefix.Length, raw.Length - Base64Prefix.Length - Base64Suffix.Length);
        var buffer = new byte[payload.Length];
        if (!Convert.TryFromBase64Chars(payload, buffer, out var written))
        {
            return null;
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(buffer, 0, written);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static StatelessValidationError Missing(string header)
        => new(JsonRpcErrorCodes.HeaderMismatch, "Header mismatch: missing " + header + " header");

    private static StatelessValidationError Mismatch(string header)
        => new(JsonRpcErrorCodes.HeaderMismatch, "Header mismatch: " + header + " header does not match the request body");
}
