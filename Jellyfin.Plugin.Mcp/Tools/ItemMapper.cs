using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Maps Jellyfin items to the compact shapes returned by tools. Null fields are omitted.
/// </summary>
public static class ItemMapper
{
    /// <summary>
    /// Maps an item to its summary shape.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="data">The user's data for the item, if any.</param>
    /// <returns>The summary.</returns>
    public static object Summary(BaseItem item, UserItemData? data)
        => BuildSummary(item, data);

    /// <summary>
    /// Maps a playlist entry to its summary shape, prefixed with the id used to remove the entry.
    /// </summary>
    /// <param name="entryId">The playlist entry id, if the entry has one.</param>
    /// <param name="item">The item.</param>
    /// <param name="data">The user's data for the item, if any.</param>
    /// <returns>The summary with an <c>entryId</c> field.</returns>
    public static object Entry(string? entryId, BaseItem item, UserItemData? data)
    {
        var result = new Dictionary<string, object>();
        Add(result, "entryId", entryId);
        foreach (var pair in BuildSummary(item, data))
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    /// <summary>
    /// Maps an item to its detailed shape.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="data">The user's data for the item, if any.</param>
    /// <param name="people">The people attached to the item.</param>
    /// <param name="personIds">Person item ids by name, for the people the user may see; others get no id.</param>
    /// <returns>The detail.</returns>
    public static object Detail(BaseItem item, UserItemData? data, IReadOnlyList<PersonInfo> people, IReadOnlyDictionary<string, Guid> personIds)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(personIds);
        var result = BuildSummary(item, data);
        Add(result, "overview", string.IsNullOrWhiteSpace(item.Overview) ? null : item.Overview);
        Add(result, "genres", NonEmpty(item.Genres));
        Add(result, "studios", NonEmpty(item.Studios));
        Add(result, "tags", NonEmpty(item.Tags));
        Add(result, "premiereDate", CalendarDate(item.PremiereDate));
        Add(result, "criticRating", item.CriticRating);
        if (people.Count > 0)
        {
            Add(
                result,
                "people",
                people.Select(p =>
                {
                    var person = new Dictionary<string, object>();
                    Add(person, "name", p.Name);
                    Add(person, "role", string.IsNullOrEmpty(p.Role) ? null : p.Role);
                    Add(person, "type", p.Type.ToString());
                    Add(person, "id", !string.IsNullOrEmpty(p.Name) && personIds.TryGetValue(p.Name, out var personId) ? personId.ToString("N", CultureInfo.InvariantCulture) : null);
                    return person;
                }).ToList());
        }

        return result;
    }

    /// <summary>
    /// Formats a timestamp as ISO 8601 in UTC.
    /// </summary>
    /// <remarks>
    /// Jellyfin stores UTC timestamps, but values read back from the database are often <see cref="DateTimeKind.Unspecified"/>;
    /// <c>ToUniversalTime()</c> would treat those as local time and shift them.
    /// </remarks>
    /// <param name="value">The timestamp; <see cref="DateTimeKind.Unspecified"/> is taken as UTC.</param>
    /// <returns>The formatted value, or <c>null</c> when there is none.</returns>
    internal static string? Utc(DateTime? value)
        => value is null
            ? null
            : (value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
                .ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a calendar date (premiere date, birth date) as <c>yyyy-MM-dd</c>.
    /// </summary>
    /// <remarks>
    /// Metadata providers store these as local midnight converted to UTC, so a server east of UTC keeps
    /// 17 March as 16 March 23:00Z. Rounding to the nearest UTC midnight recovers the intended day.
    /// </remarks>
    /// <param name="value">The stored date; <see cref="DateTimeKind.Unspecified"/> is taken as UTC.</param>
    /// <returns>The formatted day, or <c>null</c> when there is none.</returns>
    internal static string? CalendarDate(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        var utc = value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value;
        return utc.AddHours(12).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, object> BuildSummary(BaseItem item, UserItemData? data)
    {
        ArgumentNullException.ThrowIfNull(item);
        var result = new Dictionary<string, object>();
        Add(result, "id", item.Id.ToString("N", CultureInfo.InvariantCulture));
        Add(result, "name", item.Name);
        Add(result, "type", SnakeCase.From(item.GetBaseItemKind().ToString()));
        Add(result, "year", item.ProductionYear);
        Add(result, "seriesName", SeriesName(item));
        Add(result, "seasonNumber", item.ParentIndexNumber);
        Add(result, "episodeNumber", item.IndexNumber);
        Add(result, "runtimeMinutes", Minutes(item.RunTimeTicks));
        Add(result, "communityRating", item.CommunityRating);
        Add(result, "officialRating", string.IsNullOrEmpty(item.OfficialRating) ? null : item.OfficialRating);
        if (data is not null)
        {
            Add(result, "played", data.Played);
            Add(result, "favorite", data.IsFavorite);
            Add(result, "playbackPositionMinutes", data.PlaybackPositionTicks > 0 ? Minutes(data.PlaybackPositionTicks) : null);
            Add(result, "lastPlayedDate", Utc(data.LastPlayedDate));
        }

        return result;
    }

    private static string? SeriesName(BaseItem item)
        => item switch
        {
            Episode episode => episode.SeriesName,
            Season season => season.SeriesName,
            _ => null,
        };

    private static double? Minutes(long? ticks)
        => ticks is null ? null : Math.Round((double)ticks.Value / TimeSpan.TicksPerMinute, 1);

    private static string[]? NonEmpty(string[]? values)
        => values is { Length: > 0 } ? values : null;

    private static void Add(Dictionary<string, object> target, string key, object? value)
    {
        if (value is not null)
        {
            target[key] = value;
        }
    }
}
