using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Behaviour hints a tool declares to clients.
/// </summary>
/// <param name="ReadOnly">Whether the tool never modifies anything.</param>
/// <param name="Destructive">Whether the tool may delete or overwrite data.</param>
/// <param name="Idempotent">Whether repeating the call has no further effect.</param>
public sealed record ToolAnnotations(
    [property: JsonPropertyName("readOnlyHint")] bool ReadOnly,
    [property: JsonPropertyName("destructiveHint")] bool Destructive,
    [property: JsonPropertyName("idempotentHint")] bool Idempotent)
{
    /// <summary>
    /// Gets the display title repeated inside the annotations; filled from <see cref="IMcpTool.Title"/> when listing.
    /// </summary>
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }
}
