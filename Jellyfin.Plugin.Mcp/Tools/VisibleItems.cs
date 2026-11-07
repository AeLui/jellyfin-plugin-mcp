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
/// For a library-restricted token the query is also narrowed to the allowed libraries (<see cref="LibraryRestriction"/>),
/// and playlists, collections and by-name items (artists, people, genres, studios), which have no library, are checked
/// one by one.
/// </remarks>
public static class VisibleItems
{
    private static readonly BaseItemKind[] Guarded = [BaseItemKind.Playlist, BaseItemKind.BoxSet];

    private static readonly BaseItemKind[] RestrictedGuarded =
    [
        BaseItemKind.Playlist,
        BaseItemKind.BoxSet,
        BaseItemKind.MusicArtist,
        BaseItemKind.Person,
        BaseItemKind.Genre,
        BaseItemKind.MusicGenre,
        BaseItemKind.Studio,
    ];

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
    /// reduced by what was removed, which can overcount by the hidden items on later pages. For a library-restricted
    /// token every query that asks for a checked kind is filtered in memory, so its count is always exact.
    /// </remarks>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="query">The query; its <c>User</c> must be the scope's user.</param>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="mayContainGuarded">Whether the query's parent is known to hold playlists or collections.</param>
    /// <returns>The filtered, paged result.</returns>
    /// <exception cref="InvalidOperationException">The query has no user, or another user than the scope.</exception>
    public static QueryResult<BaseItem> GetVisibleItemsResult(this ILibraryManager libraryManager, InternalItemsQuery query, ScopedUser scope, bool mayContainGuarded = false)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);
        var user = query.User ?? throw new InvalidOperationException("The query must be scoped to a user.");
        if (!user.Id.Equals(scope.User.Id))
        {
            throw new InvalidOperationException("The query must be scoped to the caller's user.");
        }

        LibraryRestriction.Restrict(libraryManager, scope, query);
        var guarded = scope.IsRestricted ? RestrictedGuarded : Guarded;
        bool Visible(BaseItem item) => IsVisible(item, user) && (!IsChecked(item) || LibraryRestriction.IsAllowed(libraryManager, scope, item));

        var types = query.IncludeItemTypes;
        var startIndex = query.StartIndex ?? 0;
        var limit = query.Limit;
        if (mayContainGuarded
            || (types.Length > 0 && types.All(guarded.Contains))
            || (scope.IsRestricted && types.Any(guarded.Contains)))
        {
            return Unpaged(libraryManager, query, Visible, startIndex, limit);
        }

        var result = libraryManager.GetItemsResult(query);
        var visible = result.Items.Where(Visible).ToList();
        if (visible.Count == result.Items.Count)
        {
            return result;
        }

        // A hidden item showed up in an untyped query (the parent held playlists or collections): redo it exactly.
        if (types.Length == 0)
        {
            return Unpaged(libraryManager, query, Visible, startIndex, limit);
        }

        return new QueryResult<BaseItem>(result.StartIndex, Math.Max(0, result.TotalRecordCount - (result.Items.Count - visible.Count)), visible);

        bool IsChecked(BaseItem item) => scope.IsRestricted && guarded.Contains(item.GetBaseItemKind());
    }

    /// <summary>
    /// Applies the visibility rule Jellyfin's own queries skip for playlists and collections.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="user">The user.</param>
    /// <returns><c>true</c> when the user may see the item.</returns>
    internal static bool IsVisible(BaseItem item, User user)
        => item is not (Playlist or BoxSet) || item.IsVisibleStandalone(user);

    /// <summary>
    /// Applies the playlist and collection rule and the token's library restriction to an item from a Jellyfin service
    /// that is not an item query (similar items, recommendations, collections containing an item).
    /// </summary>
    /// <param name="lookup">The item lookup.</param>
    /// <param name="item">An item the service returned for the user.</param>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <returns><c>true</c> when the caller may see the item.</returns>
    internal static bool IsVisible(ItemLookup lookup, BaseItem item, ScopedUser scope)
        => IsVisible(item, scope.User) && lookup.IsAllowed(scope, item);

    private static QueryResult<BaseItem> Unpaged(ILibraryManager libraryManager, InternalItemsQuery query, Func<BaseItem, bool> visible, int startIndex, int? limit)
    {
        query.StartIndex = null;
        query.Limit = null;
        var items = libraryManager.GetItemsResult(query).Items.Where(visible).ToList();
        var page = items.Skip(startIndex);
        if (limit is { } take)
        {
            page = page.Take(take);
        }

        return new QueryResult<BaseItem>(startIndex, items.Count, page.ToList());
    }
}
