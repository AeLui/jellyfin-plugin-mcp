using System;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Helpers that serialize JSON-RPC 2.0 response envelopes.
/// </summary>
internal static class JsonRpcMessages
{
    /// <summary>
    /// Writes a success response.
    /// </summary>
    /// <param name="id">The request id token, copied verbatim.</param>
    /// <param name="writeResult">Writes the value of the <c>result</c> member.</param>
    /// <returns>The UTF-8 JSON response.</returns>
    public static byte[] Success(JsonElement id, Action<Utf8JsonWriter> writeResult)
    {
        return Build(id, writer =>
        {
            writer.WritePropertyName("result");
            writeResult(writer);
        });
    }

    /// <summary>
    /// Writes an error response.
    /// </summary>
    /// <param name="id">The request id token, or default for <c>null</c>.</param>
    /// <param name="code">The JSON-RPC error code.</param>
    /// <param name="message">The error message.</param>
    /// <returns>The UTF-8 JSON response.</returns>
    public static byte[] Error(JsonElement id, int code, string message)
    {
        return Build(id, writer =>
        {
            writer.WriteStartObject("error");
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
        });
    }

    private static byte[] Build(JsonElement id, Action<Utf8JsonWriter> writeBody)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            if (id.ValueKind == JsonValueKind.Undefined)
            {
                writer.WriteNullValue();
            }
            else
            {
                id.WriteTo(writer);
            }

            writeBody(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
