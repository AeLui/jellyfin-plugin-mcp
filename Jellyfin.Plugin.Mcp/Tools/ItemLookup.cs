using System;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Library;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Loads items on behalf of a user, honouring library access and parental controls.
/// </summary>
public sealed class ItemLookup
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserViewManager _userViewManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemLookup"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="userViewManager">The user view manager.</param>
    public ItemLookup(ILibraryManager libraryManager, IUserViewManager userViewManager)
    {
        _libraryManager = libraryManager;
        _userViewManager = userViewManager;
    }

    /// <summary>
    /// Gets an item the user is allowed to see.
    /// </summary>
    /// <remarks>
    /// Uses the user-aware <c>GetItemById</c> overload, which checks parental rating, tags and the user's
    /// enabled/blocked libraries, so hidden items look exactly like missing ones. An <c>InternalItemsQuery</c>
    /// with <c>ItemIds</c> or <c>ParentId</c> does not enforce library access, so it must not be used for this.
    /// Never replace this with the non-user <c>GetItemById</c>.
    /// </remarks>
    /// <param name="user">The user.</param>
    /// <param name="id">The item id supplied by the caller.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ToolException">The item does not exist or is not visible to the user.</exception>
    public BaseItem GetVisible(User user, Guid id)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _libraryManager.GetItemById<BaseItem>(id, user) ?? throw new ToolException("Item not found");
    }

    /// <summary>
    /// Gets a folder the user may use as the parent of a query.
    /// </summary>
    /// <remarks>
    /// <see cref="GetVisible"/> accepts the server's root folders (<see cref="UserRootFolder"/> and
    /// <see cref="AggregateFolder"/>); as a query parent they bypass per-user library scoping and expose hidden
    /// libraries, so they are rejected here with the same message as a missing item.
    /// </remarks>
    /// <param name="user">The user.</param>
    /// <param name="id">The folder id supplied by the caller.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="ToolException">The item does not exist, is not visible, or is a root folder.</exception>
    public BaseItem GetVisibleContainer(User user, Guid id)
    {
        var item = GetVisible(user, id);
        return item is UserRootFolder or AggregateFolder ? throw new ToolException("Item not found") : item;
    }

    /// <summary>
    /// Checks that an id is one of the user's own libraries, exactly the set <c>jellyfin_list_libraries</c> returns.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="id">The library id supplied by the caller.</param>
    /// <exception cref="ToolException">The id is not one of the user's libraries.</exception>
    public void EnsureUserLibrary(User user, Guid id)
    {
        ArgumentNullException.ThrowIfNull(user);
        var views = _userViewManager.GetUserViews(new UserViewQuery { User = user });
        if (!views.Any(v => v.Id == id))
        {
            throw new ToolException("Item not found");
        }
    }
}
