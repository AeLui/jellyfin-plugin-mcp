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
    /// Goes through a user-aware query so items outside the user's libraries or parental rating look
    /// exactly like items that do not exist. Never replace this with <c>GetItemById</c> alone.
    /// </remarks>
    /// <param name="user">The user.</param>
    /// <param name="id">The item id supplied by the caller.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ToolException">The item does not exist or is not visible to the user.</exception>
    public BaseItem GetVisible(User user, Guid id)
    {
        ArgumentNullException.ThrowIfNull(user);
        var query = new InternalItemsQuery(user)
        {
            ItemIds = [id],
            Limit = 1,
            EnableTotalRecordCount = false,
        };

        var items = _libraryManager.GetItemsResult(query).Items;
        return items.Count > 0 ? items[0] : throw new ToolException("Item not found");
    }
}
