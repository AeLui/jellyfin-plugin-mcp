using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Library;

namespace Jellyfin.Plugin.Mcp.Tools;

/// <summary>
/// Loads items on behalf of a user, honouring library access, parental controls and the token's library restriction.
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
    /// enabled/blocked libraries, then the token's library restriction (<see cref="LibraryRestriction"/>), so hidden items
    /// look exactly like missing ones. An <c>InternalItemsQuery</c> with <c>ItemIds</c> or <c>ParentId</c> does not enforce
    /// library access, so it must not be used for this. Never replace this with the non-user <c>GetItemById</c>.
    /// </remarks>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="id">The item id supplied by the caller.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ToolException">The item does not exist or is not visible to the user or the token.</exception>
    public BaseItem GetVisible(ScopedUser scope, Guid id)
        => FindVisible(scope, id) ?? throw new ToolException("Item not found");

    /// <summary>
    /// Finds an item the user can see, like <see cref="GetVisible"/> but without failing.
    /// </summary>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="id">The item id.</param>
    /// <returns>The item, or <c>null</c> when it does not exist or the user or the token cannot see it.</returns>
    public BaseItem? FindVisible(ScopedUser scope, Guid id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var item = _libraryManager.GetItemById<BaseItem>(id, scope.User);
        return item is not null && LibraryRestriction.IsAllowed(_libraryManager, scope, item) ? item : null;
    }

    /// <summary>
    /// Tells whether the token's library restriction lets it see an item the user can see, for results that do not come
    /// from <see cref="GetVisible"/> or <see cref="VisibleItems.GetVisibleItemsResult"/>.
    /// </summary>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="item">An item the user can see.</param>
    /// <returns><c>true</c> when the token may see it.</returns>
    public bool IsAllowed(ScopedUser scope, BaseItem item) => LibraryRestriction.IsAllowed(_libraryManager, scope, item);

    /// <summary>
    /// Gets the user's children of a folder (for a playlist or collection, its entries) that the token may see.
    /// </summary>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="folder">The folder.</param>
    /// <returns>The children.</returns>
    public IReadOnlyList<BaseItem> VisibleChildren(ScopedUser scope, Folder folder)
        => LibraryRestriction.VisibleChildren(_libraryManager, scope, folder);

    /// <summary>
    /// Gets the libraries a restricted token may use (the allowed ones the user can still open).
    /// </summary>
    /// <param name="scope">A restricted scope.</param>
    /// <returns>The libraries.</returns>
    public IReadOnlyList<Folder> AllowedLibraries(ScopedUser scope)
        => LibraryRestriction.GetAllowedLibraries(_libraryManager, scope);

    /// <summary>
    /// Gets a folder the user may use as the parent of a query.
    /// </summary>
    /// <remarks>
    /// <see cref="GetVisible"/> accepts the server's root folders (<see cref="UserRootFolder"/> and
    /// <see cref="AggregateFolder"/>); as a query parent they bypass per-user library scoping and expose hidden
    /// libraries, so they are rejected here with the same message as a missing item. Items that are not folders
    /// have no children and are rejected the same way.
    /// </remarks>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="id">The folder id supplied by the caller.</param>
    /// <returns>The folder.</returns>
    /// <exception cref="ToolException">The item does not exist, is not visible, is not a folder, or is a root folder.</exception>
    public Folder GetVisibleContainer(ScopedUser scope, Guid id)
    {
        var item = GetVisible(scope, id);
        return item is Folder and not (UserRootFolder or AggregateFolder) ? (Folder)item : throw new ToolException("Item not found");
    }

    /// <summary>
    /// Gets a person by item id, if the user may see them.
    /// </summary>
    /// <remarks>
    /// People are metadata items that live outside every library (no parents, no collection folder), so the
    /// library-scoped <see cref="GetVisible"/> does not apply. Like Jellyfin's own <c>DtoService</c>, this loads the
    /// <see cref="Person"/> without a user and checks <see cref="BaseItem.IsVisible"/> (parental rating) for the user.
    /// A library-restricted token only sees people credited on an item of its libraries.
    /// </remarks>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="id">The person item id supplied by the caller.</param>
    /// <returns>The person.</returns>
    /// <exception cref="ToolException">The id is not a person, or the person is not visible to the user or the token.</exception>
    public Person GetVisiblePerson(ScopedUser scope, Guid id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return _libraryManager.GetItemById(id) is Person person && IsVisiblePerson(scope, person)
            ? person
            : throw new ToolException("Item not found");
    }

    /// <summary>
    /// Gets a person by exact name, if the user may see them.
    /// </summary>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="name">The person's name.</param>
    /// <returns>The person.</returns>
    /// <exception cref="ToolException">There is no such person, or the person is not visible to the user or the token.</exception>
    public Person GetVisiblePerson(ScopedUser scope, string name)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return _libraryManager.GetPerson(name) is { } person && IsVisiblePerson(scope, person)
            ? person
            : throw new ToolException("Item not found");
    }

    /// <summary>
    /// Maps the names of credited people to their <see cref="Person"/> item ids, leaving out people the user cannot see.
    /// </summary>
    /// <remarks>
    /// <c>PersonInfo.Id</c> is a row id of the credits table, not the id of the <see cref="Person"/> item, so it cannot
    /// be used with <c>jellyfin_get_person</c>. Jellyfin's <c>DtoService</c> resolves the same way: by name.
    /// Pass only the people of an item the scope can see: they are credited on it, so the library restriction admits them.
    /// </remarks>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="people">The credited people of an item the scope can see.</param>
    /// <returns>Person item ids keyed by name (case-insensitive).</returns>
    public IReadOnlyDictionary<string, Guid> GetPersonIds(ScopedUser scope, IEnumerable<PersonInfo> people)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(people);
        var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in people.Select(p => p.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_libraryManager.GetPerson(name) is { } person && person.IsVisible(scope.User))
            {
                ids[name] = person.Id;
            }
        }

        return ids;
    }

    /// <summary>
    /// Checks that an id is one of the user's own libraries, exactly the set <c>jellyfin_list_libraries</c> returns
    /// (for a library-restricted token, its allowed libraries).
    /// </summary>
    /// <param name="scope">The user and the token's library restriction.</param>
    /// <param name="id">The library id supplied by the caller.</param>
    /// <exception cref="ToolException">The id is not one of the user's libraries.</exception>
    public void EnsureUserLibrary(ScopedUser scope, Guid id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var ids = scope.IsRestricted
            ? AllowedLibraries(scope).Select(l => l.Id)
            : _userViewManager.GetUserViews(new UserViewQuery { User = scope.User }).Select(v => v.Id);
        if (!ids.Contains(id))
        {
            throw new ToolException("Item not found");
        }
    }

    private bool IsVisiblePerson(ScopedUser scope, Person person)
        => person.IsVisible(scope.User) && LibraryRestriction.IsAllowed(_libraryManager, scope, person);
}
