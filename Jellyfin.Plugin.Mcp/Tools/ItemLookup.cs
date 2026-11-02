using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Loads items on behalf of a user, honouring library access and parental controls.
/// </summary>
public sealed class ItemLookup
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemLookup"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    public ItemLookup(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
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
}
