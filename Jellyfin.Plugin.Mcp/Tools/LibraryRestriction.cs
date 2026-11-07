using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Applies a token's library restriction on top of the user's own access. It only ever removes items.
/// </summary>
/// <remarks>
/// <para>A library is a child of the user root folder that the user may open (<c>GetUserRootFolder().GetChildren(user, true)</c>,
/// the same list Jellyfin's <c>UserViewManager</c> and <c>TVSeriesManager</c> start from). Collection and playlist folders
/// are not selectable: they only hold links into the media libraries.</para>
/// <para>Membership follows Jellyfin's own library check (<c>BaseItem.IsVisibleStandaloneInternal</c>, v12.4): an item belongs
/// to the libraries <see cref="ILibraryManager.GetCollectionFolders(BaseItem)"/> returns. Queries are narrowed the way
/// <c>LibraryManager.AddUserToQuery</c> does it, through <see cref="InternalItemsQuery.TopParentIds"/> built from each
/// library's <see cref="CollectionFolder.PhysicalFolderIds"/> (<c>GetTopParentIdsForQuery</c>).</para>
/// <para>Items that live outside every library follow their own rules: a playlist or collection is visible when it is empty
/// or holds at least one allowed entry, and a by-name item (artist, person, genre, studio) when an item of an allowed
/// library refers to it. Anything else outside the allowed libraries is hidden.</para>
/// </remarks>
public static class LibraryRestriction
{
    /// <summary>
    /// Gets the libraries a token for this user may be limited to: the user's media libraries.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="user">The user.</param>
    /// <returns>The libraries, in the user's order.</returns>
    public static IReadOnlyList<Folder> GetSelectableLibraries(ILibraryManager libraryManager, User user)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(user);
        return UserLibraries(libraryManager, user).Where(f => !IsLinkFolder(f)).ToList();
    }

    /// <summary>
    /// Gets the libraries a restricted scope may use: the allowed ids the user can still open.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="scope">The scope; must be restricted.</param>
    /// <returns>The allowed libraries.</returns>
    public static IReadOnlyList<Folder> GetAllowedLibraries(ILibraryManager libraryManager, ScopedUser scope)
        => Resolve(libraryManager, scope).Libraries;

    /// <summary>
    /// Tells whether the scope's library restriction lets it see an item the user can already see.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="item">An item the user can see.</param>
    /// <returns><c>true</c> when the scope is unrestricted or the item is inside its libraries.</returns>
    public static bool IsAllowed(ILibraryManager libraryManager, ScopedUser scope, BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(item);
        if (!scope.IsRestricted)
        {
            return true;
        }

        var resolved = Resolve(libraryManager, scope);
        return item switch
        {
            Playlist or BoxSet => ContainerChildren((Folder)item, scope.User) is var children
                && (children.Count == 0 || children.Any(c => IsInLibrary(libraryManager, resolved, c))),
            Person or MusicArtist or Genre or MusicGenre or Studio => IsReferenced(libraryManager, scope, resolved, item),
            _ => IsInLibrary(libraryManager, resolved, item),
        };
    }

    /// <summary>
    /// Gets the entries of a playlist or collection that the scope may see.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="container">The playlist, collection or other folder.</param>
    /// <returns>The user's children of the folder, without those outside the scope's libraries.</returns>
    public static IReadOnlyList<BaseItem> VisibleChildren(ILibraryManager libraryManager, ScopedUser scope, Folder container)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(container);
        var children = ContainerChildren(container, scope.User);
        if (!scope.IsRestricted)
        {
            return children;
        }

        var resolved = Resolve(libraryManager, scope);
        return children.Where(c => IsInLibrary(libraryManager, resolved, c)).ToList();
    }

    /// <summary>
    /// Narrows a user query to the scope's libraries (and the user's collection and playlist folders, whose items are
    /// checked afterwards). Does nothing for an unrestricted scope.
    /// </summary>
    /// <remarks>
    /// A query with <c>ParentId</c> is left alone: the caller must have checked the parent with <see cref="ItemLookup"/>, and
    /// <c>LibraryManager</c> scopes it to that parent. Queries that set <c>ItemIds</c>, <c>AncestorIds</c>,
    /// <c>TopParentIds</c> or <c>OwnerIds</c> skip Jellyfin's own library scoping, so they are refused.
    /// </remarks>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="scope">The scope.</param>
    /// <param name="query">The query.</param>
    /// <exception cref="InvalidOperationException">The query bypasses library scoping.</exception>
    public static void Restrict(ILibraryManager libraryManager, ScopedUser scope, InternalItemsQuery query)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        if (!scope.IsRestricted)
        {
            return;
        }

        if (query.ItemIds.Length > 0 || query.AncestorIds.Length > 0 || query.TopParentIds.Length > 0 || query.OwnerIds.Length > 0)
        {
            throw new InvalidOperationException("A restricted query cannot select items by id or ancestor.");
        }

        if (!query.ParentId.Equals(Guid.Empty))
        {
            return;
        }

        query.TopParentIds = Resolve(libraryManager, scope).QueryTopParentIds;
    }

    private static bool IsLinkFolder(Folder folder)
        => (folder as ICollectionFolder)?.CollectionType is CollectionType.boxsets or CollectionType.playlists;

    private static IEnumerable<Folder> UserLibraries(ILibraryManager libraryManager, User user)
        => libraryManager.GetUserRootFolder().GetChildren(user, true).OfType<Folder>();

    // The ids LibraryManager.GetTopParentIdsForQuery yields for a library folder.
    private static IEnumerable<Guid> TopParentIds(Folder folder)
        => folder is CollectionFolder collection ? collection.PhysicalFolderIds : [folder.Id];

    private static IReadOnlyList<BaseItem> ContainerChildren(Folder container, User user)
        => container.GetChildren(user, true, new InternalItemsQuery(user));

    private static bool IsInLibrary(ILibraryManager libraryManager, Resolved resolved, BaseItem item)
        => resolved.LibraryIds.Contains(item.Id)
            || libraryManager.GetCollectionFolders(item).Any(f => resolved.LibraryIds.Contains(f.Id));

    // By-name items have no library; like ApplyItemByNameAccessFiltering, keep one only if an allowed item refers to it.
    private static bool IsReferenced(ILibraryManager libraryManager, ScopedUser scope, Resolved resolved, BaseItem item)
    {
        if (resolved.LibraryTopParentIds.Length == 0)
        {
            return false;
        }

        var query = new InternalItemsQuery(scope.User)
        {
            Recursive = true,
            Limit = 1,
            EnableTotalRecordCount = false,
            TopParentIds = resolved.LibraryTopParentIds,
        };
        switch (item)
        {
            case MusicArtist:
                query.ArtistIds = [item.Id];
                break;
            case Person:
                query.PersonIds = [item.Id];
                break;
            case Studio:
                query.StudioIds = [item.Id];
                break;
            default:
                query.GenreIds = [item.Id];
                break;
        }

        return libraryManager.GetItemsResult(query).Items.Count > 0;
    }

    private static Resolved Resolve(ILibraryManager libraryManager, ScopedUser scope)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        if (scope.Resolved is { } cached)
        {
            return cached;
        }

        var userLibraries = UserLibraries(libraryManager, scope.User).ToList();
        var allowed = userLibraries.Where(f => !IsLinkFolder(f) && scope.LibraryIds.Contains(f.Id)).ToList();
        var libraryTopParents = allowed.SelectMany(TopParentIds).Distinct().ToArray();
        var queryTopParents = libraryTopParents
            .Concat(userLibraries.Where(IsLinkFolder).SelectMany(TopParentIds))
            .Distinct()
            .ToArray();

        // An empty TopParentIds would mean "no filter"; like LibraryManager, match nothing instead.
        Guid[] nothing = [Guid.NewGuid()];
        var resolved = new Resolved(
            allowed,
            allowed.Select(f => f.Id).ToHashSet(),
            libraryTopParents,
            queryTopParents.Length > 0 ? queryTopParents : nothing);
        scope.Resolved = resolved;
        return resolved;
    }

    /// <summary>
    /// The libraries a restricted scope resolved to for one call.
    /// </summary>
    /// <param name="Libraries">The allowed libraries the user can open.</param>
    /// <param name="LibraryIds">Their ids.</param>
    /// <param name="LibraryTopParentIds">Their top parent ids; empty when no allowed library is left.</param>
    /// <param name="QueryTopParentIds">The top parent ids for list queries, never empty.</param>
    internal sealed record Resolved(IReadOnlyList<Folder> Libraries, HashSet<Guid> LibraryIds, Guid[] LibraryTopParentIds, Guid[] QueryTopParentIds);
}
