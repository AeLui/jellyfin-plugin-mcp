using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Reusable JSON Schema fragments for tool output schemas. Fragments are inlined (no <c>$ref</c>), and every object
/// schema forbids undeclared properties so tests catch fields that are emitted but not declared.
/// </summary>
internal static class OutputSchemas
{
    /// <summary>
    /// The properties of the item summary emitted by <see cref="ItemMapper.Summary"/>.
    /// </summary>
    private const string SummaryProperties =
        """
        "id":{"type":"string","description":"Item id (GUID, N format)."},
        "name":{"type":"string"},
        "type":{"type":"string","description":"Item kind in snake_case, for example movie, series, episode."},
        "year":{"type":"integer"},
        "seriesName":{"type":"string"},
        "seasonNumber":{"type":"integer"},
        "episodeNumber":{"type":"integer"},
        "runtimeMinutes":{"type":"number"},
        "communityRating":{"type":"number"},
        "officialRating":{"type":"string"},
        "played":{"type":"boolean"},
        "favorite":{"type":"boolean"},
        "playbackPositionMinutes":{"type":"number"},
        "lastPlayedDate":{"type":"string","description":"ISO 8601 UTC timestamp."}
        """;

    private const string DetailProperties =
        """
        "overview":{"type":"string"},
        "genres":{"type":"array","items":{"type":"string"}},
        "studios":{"type":"array","items":{"type":"string"}},
        "tags":{"type":"array","items":{"type":"string"}},
        "premiereDate":{"type":"string","description":"Calendar date, yyyy-MM-dd."},
        "criticRating":{"type":"number"},
        "people":{"type":"array","items":{"type":"object","properties":{
          "name":{"type":"string"},
          "role":{"type":"string"},
          "type":{"type":"string"},
          "id":{"type":"string","description":"Person id, only when the user can see the person."}},
          "additionalProperties":false}}
        """;

    /// <summary>
    /// Gets the schema of one item summary.
    /// </summary>
    public static string ItemSummary { get; } = ObjectJson(SummaryProperties, "id", "type");

    /// <summary>
    /// Gets the schema of an item detail (summary plus metadata and people).
    /// </summary>
    public static string ItemDetail { get; } = ObjectJson(SummaryProperties + "," + DetailProperties, "id", "type");

    /// <summary>
    /// Gets the schema of a playlist entry (summary prefixed with the entry id).
    /// </summary>
    public static string PlaylistEntry { get; } = ObjectJson("\"entryId\":{\"type\":\"string\"}," + SummaryProperties, "id", "type");

    /// <summary>
    /// Gets the schema of a paged list of item summaries.
    /// </summary>
    public static JsonElement ItemPage { get; } = Object(
        "\"totalCount\":{\"type\":\"integer\"},\"items\":" + ArrayOf(ItemSummary),
        "totalCount",
        "items");

    /// <summary>
    /// Gets the schema of a page of recently added items (item summaries with <c>dateAdded</c>).
    /// </summary>
    public static JsonElement RecentItemPage { get; } = Object(
        "\"totalCount\":{\"type\":\"integer\"},\"items\":" + ArrayOf(ObjectJson(
            SummaryProperties + ",\"dateAdded\":{\"type\":\"string\",\"description\":\"ISO 8601 UTC timestamp of when the item was added.\"}",
            "id",
            "type")),
        "totalCount",
        "items");

    /// <summary>
    /// Gets the schema of a paged list of collection summaries with their visible child counts.
    /// </summary>
    public static JsonElement CollectionPage { get; } = Object(
        "\"totalCount\":{\"type\":\"integer\"},\"items\":"
            + ArrayOf(ObjectJson(SummaryProperties + ",\"childCount\":{\"type\":\"integer\",\"description\":\"Items in the collection that you can see.\"}", "id", "type", "childCount")),
        "totalCount",
        "items");

    /// <summary>
    /// Builds an object schema that forbids undeclared properties.
    /// </summary>
    /// <param name="propertiesJson">The members of <c>properties</c>, without braces.</param>
    /// <param name="required">The required property names.</param>
    /// <returns>The schema as JSON text.</returns>
    public static string ObjectJson(string propertiesJson, params string[] required)
        => "{\"type\":\"object\",\"properties\":{" + propertiesJson + "},\"required\":["
            + string.Join(',', required.Select(r => "\"" + r + "\""))
            + "],\"additionalProperties\":false}";

    /// <summary>
    /// Builds an object schema that forbids undeclared properties.
    /// </summary>
    /// <param name="propertiesJson">The members of <c>properties</c>, without braces.</param>
    /// <param name="required">The required property names.</param>
    /// <returns>The parsed schema.</returns>
    public static JsonElement Object(string propertiesJson, params string[] required)
        => Parse(ObjectJson(propertiesJson, required));

    /// <summary>
    /// Builds an array schema.
    /// </summary>
    /// <param name="itemSchema">The schema of each element, as JSON text.</param>
    /// <returns>The schema as JSON text.</returns>
    public static string ArrayOf(string itemSchema) => "{\"type\":\"array\",\"items\":" + itemSchema + "}";

    /// <summary>
    /// Parses schema JSON into a detached element.
    /// </summary>
    /// <param name="json">The schema JSON.</param>
    /// <returns>The parsed schema.</returns>
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
