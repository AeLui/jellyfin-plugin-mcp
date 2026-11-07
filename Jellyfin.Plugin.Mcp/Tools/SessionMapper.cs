using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Maps the parts of a session that several session tools return.
/// </summary>
internal static class SessionMapper
{
    /// <summary>
    /// Gets the schema fragment of <see cref="NowPlaying"/>: an item summary, or only an id and name.
    /// </summary>
    public static string NowPlayingSchema { get; } = "{\"anyOf\":[" + OutputSchemas.ItemSummary + ","
        + OutputSchemas.ObjectJson("\"id\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"}", "id") + "]}";

    /// <summary>
    /// Maps what a session is playing.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="canShowFull">Whether the caller may see the full summary of the playing item.</param>
    /// <returns>An item summary, an id and name, or <c>null</c> when nothing is playing.</returns>
    public static object? NowPlaying(SessionInfo session, Func<BaseItem, bool> canShowFull)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(canShowFull);
        if (session.FullNowPlayingItem is { } item && canShowFull(item))
        {
            return ItemMapper.Summary(item, null);
        }

        Guid id;
        string? name;
        if (session.FullNowPlayingItem is { } hidden)
        {
            id = hidden.Id;
            name = session.NowPlayingItem?.Name ?? hidden.Name;
        }
        else if (session.NowPlayingItem is { } dto)
        {
            id = dto.Id;
            name = dto.Name;
        }
        else
        {
            return null;
        }

        // Null dictionary values are serialized (WhenWritingNull only covers properties), so a missing name is left out.
        var playing = new Dictionary<string, object> { ["id"] = id.ToString("N", CultureInfo.InvariantCulture) };
        if (name is not null)
        {
            playing["name"] = name;
        }

        return playing;
    }
}
