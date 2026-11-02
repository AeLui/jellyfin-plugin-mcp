using System;
using System.Collections.Generic;
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
    /// libraries, so they are rejected here with the same message as a missing item. Items that are not folders
    /// have no children and are rejected the same way.
    /// </remarks>
    /// <param name="user">The user.</param>
    /// <param name="id">The folder id supplied by the caller.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="ToolException">The item does not exist, is not visible, is not a folder, or is a root folder.</exception>
    public Folder GetVisibleContainer(User user, Guid id)
    {
        var item = GetVisible(user, id);
        return item is Folder and not (UserRootFolder or AggregateFolder) ? (Folder)item : throw new ToolException("Item not found");
    }

    /// <summary>
    /// Gets a person by item id, if the user may see them.
    /// </summary>
    /// <remarks>
    /// People are metadata items that live outside every library (no parents, no collection folder), so the
    /// library-scoped <see cref="GetVisible"/> does not apply. Like Jellyfin's own <c>DtoService</c>, this loads the
    /// <see cref="Person"/> without a user and checks <see cref="BaseItem.IsVisible"/> (parental rating) for the user.
    /// </remarks>
    /// <param name="user">The user.</param>
    /// <param name="id">The person item id supplied by the caller.</param>
    /// <returns>The person.</returns>
    /// <exception cref="ToolException">The id is not a person, or the person is not visible to the user.</exception>
    public Person GetVisiblePerson(User user, Guid id)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _libraryManager.GetItemById(id) is Person person && person.IsVisible(user)
            ? person
            : throw new ToolException("Item not found");
    }

    /// <summary>
    /// Gets a person by exact name, if the user may see them.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="name">The person's name.</param>
    /// <returns>The person.</returns>
    /// <exception cref="ToolException">There is no such person, or the person is not visible to the user.</exception>
    public Person GetVisiblePerson(User user, string name)
    {
        ArgumentNullException.ThrowIfNull(user);
        return _libraryManager.GetPerson(name) is { } person && person.IsVisible(user)
            ? person
            : throw new ToolException("Item not found");
    }

    /// <summary>
    /// Maps the names of credited people to their <see cref="Person"/> item ids, leaving out people the user cannot see.
    /// </summary>
    /// <remarks>
    /// <c>PersonInfo.Id</c> is a row id of the credits table, not the id of the <see cref="Person"/> item, so it cannot
    /// be used with <c>jellyfin_get_person</c>. Jellyfin's <c>DtoService</c> resolves the same way: by name.
    /// </remarks>
    /// <param name="user">The user.</param>
    /// <param name="people">The credited people.</param>
    /// <returns>Person item ids keyed by name (case-insensitive).</returns>
    public IReadOnlyDictionary<string, Guid> GetPersonIds(User user, IEnumerable<PersonInfo> people)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(people);
        var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in people.Select(p => p.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_libraryManager.GetPerson(name) is { } person && person.IsVisible(user))
            {
                ids[name] = person.Id;
            }
        }

        return ids;
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
