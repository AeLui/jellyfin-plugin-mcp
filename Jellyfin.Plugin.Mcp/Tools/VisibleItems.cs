using System;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Runs item queries for a user and removes the playlists and collections that user may not see.
/// </summary>
/// <remarks>
/// The item SQL scopes results to the user's libraries, parental rating and tags, but it has no owner or share
/// filter for <see cref="Playlist"/> and no library check for <see cref="BoxSet"/>. Jellyfin itself applies
/// <c>IsVisibleStandalone(user)</c> afterwards (<c>Folder.QueryWithPostFiltering</c>); calling
/// <see cref="ILibraryManager.GetItemsResult"/> directly skips that step, so every list tool goes through here.
/// </remarks>
public static class VisibleItems
{
    private static readonly BaseItemKind[] Guarded = [BaseItemKind.Playlist, BaseItemKind.BoxSet];

    /// <summary>
    /// Gets the kinds a query should exclude when the caller did not ask for playlists or collections.
    /// </summary>
    /// <returns>A new array of the guarded kinds.</returns>
    public static BaseItemKind[] ExcludedKinds() => (BaseItemKind[])Guarded.Clone();

    /// <summary>
    /// Runs a query and drops the playlists and collections the query's user cannot see.
    /// </summary>
    /// <remarks>
    /// When the query asks for guarded kinds (or <paramref name="mayContainGuarded"/> says its parent holds them), the
    /// whole result is fetched, filtered and then paged in memory, so <c>TotalRecordCount</c> is exact. Otherwise the
    /// query keeps its SQL paging and the result is only defensively filtered; the count is reduced by what was removed.
    /// </remarks>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="query">The query; its <c>User</c> must be set.</param>
    /// <param name="mayContainGuarded">Whether the query's parent is known to hold playlists or collections.</param>
    /// <returns>The filtered, paged result.</returns>
    public static QueryResult<BaseItem> GetVisibleItemsResult(this ILibraryManager libraryManager, InternalItemsQuery query, bool mayContainGuarded = false)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(query);
        var user = query.User ?? throw new ToolException("Item not found");

        var unpaged = mayContainGuarded || query.IncludeItemTypes.Any(Guarded.Contains);
        var startIndex = query.StartIndex ?? 0;
        var limit = query.Limit;
        if (unpaged)
        {
            query.StartIndex = null;
            query.Limit = null;
        }

        var result = libraryManager.GetItemsResult(query);
        var visible = result.Items.Where(i => i is not (Playlist or BoxSet) || i.IsVisibleStandalone(user)).ToList();
        if (!unpaged)
        {
            return new QueryResult<BaseItem>(result.StartIndex, Math.Max(0, result.TotalRecordCount - (result.Items.Count - visible.Count)), visible);
        }

        var page = visible.Skip(startIndex);
        if (limit is { } take)
        {
            page = page.Take(take);
        }

        return new QueryResult<BaseItem>(startIndex, visible.Count, page.ToList());
    }
}
