using System;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
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
    /// The whole result is fetched, filtered and paged in memory (exact <c>TotalRecordCount</c>) when every requested
    /// type is a playlist or collection, when <paramref name="mayContainGuarded"/> says the parent holds them, or when
    /// an untyped paged query turned out to return a hidden one. Mixed type lists (for example playlists and movies)
    /// keep their SQL paging so a common term cannot load the whole library; there the result is filtered and the count
    /// reduced by what was removed, which can overcount by the hidden items on later pages.
    /// </remarks>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="query">The query; its <c>User</c> must be set.</param>
    /// <param name="mayContainGuarded">Whether the query's parent is known to hold playlists or collections.</param>
    /// <returns>The filtered, paged result.</returns>
    /// <exception cref="InvalidOperationException">The query has no user.</exception>
    public static QueryResult<BaseItem> GetVisibleItemsResult(this ILibraryManager libraryManager, InternalItemsQuery query, bool mayContainGuarded = false)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(query);
        var user = query.User ?? throw new InvalidOperationException("The query must be scoped to a user.");

        var types = query.IncludeItemTypes;
        var startIndex = query.StartIndex ?? 0;
        var limit = query.Limit;
        if (mayContainGuarded || (types.Length > 0 && types.All(Guarded.Contains)))
        {
            return Unpaged(libraryManager, query, user, startIndex, limit);
        }

        var result = libraryManager.GetItemsResult(query);
        var visible = result.Items.Where(i => IsVisibleTo(i, user)).ToList();
        if (visible.Count == result.Items.Count)
        {
            return result;
        }

        // A hidden item showed up in an untyped query (the parent held playlists or collections): redo it exactly.
        if (types.Length == 0)
        {
            return Unpaged(libraryManager, query, user, startIndex, limit);
        }

        return new QueryResult<BaseItem>(result.StartIndex, Math.Max(0, result.TotalRecordCount - (result.Items.Count - visible.Count)), visible);
    }

    private static bool IsVisibleTo(BaseItem item, User user)
        => item is not (Playlist or BoxSet) || item.IsVisibleStandalone(user);

    private static QueryResult<BaseItem> Unpaged(ILibraryManager libraryManager, InternalItemsQuery query, User user, int startIndex, int? limit)
    {
        query.StartIndex = null;
        query.Limit = null;
        var visible = libraryManager.GetItemsResult(query).Items.Where(i => IsVisibleTo(i, user)).ToList();
        var page = visible.Skip(startIndex);
        if (limit is { } take)
        {
            page = page.Take(take);
        }

        return new QueryResult<BaseItem>(startIndex, visible.Count, page.ToList());
    }
}
