using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// The outcome of a tool call.
/// </summary>
public sealed class ToolResult
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private ToolResult(string text, bool isError, JsonElement? structured)
    {
        Text = text;
        IsError = isError;
        Structured = structured;
    }

    /// <summary>
    /// Gets a value indicating whether the call failed.
    /// </summary>
    public bool IsError { get; }

    /// <summary>
    /// Gets the text content returned to the client.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the result as JSON for <c>structuredContent</c>; same JSON as <see cref="Text"/>, or <c>null</c> for errors.
    /// </summary>
    public JsonElement? Structured { get; }

    /// <summary>
    /// Creates a successful result holding compact camelCase JSON.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The result.</returns>
    public static ToolResult Json(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = JsonSerializer.Serialize(value, value.GetType(), SerializerOptions);
        using var document = JsonDocument.Parse(text);
        return new ToolResult(text, false, document.RootElement.Clone());
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The result.</returns>
    public static ToolResult Error(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new ToolResult(message, true, null);
    }
}
