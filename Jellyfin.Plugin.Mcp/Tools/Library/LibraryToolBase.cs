using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Base class for read-only tools that need the <c>library</c> scope.
/// </summary>
public abstract class LibraryToolBase : JellyfinToolBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryToolBase"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    protected LibraryToolBase(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager)
        : base(userManager, settings)
    {
        UserDataManager = userDataManager;
    }

    /// <inheritdoc />
    public sealed override string RequiredScope => McpScopes.Library;

    /// <inheritdoc />
    public sealed override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <summary>
    /// Gets the user data manager.
    /// </summary>
    protected IUserDataManager UserDataManager { get; }

    /// <summary>
    /// Builds the paged result shape.
    /// </summary>
    /// <param name="user">The user the data belongs to.</param>
    /// <param name="totalCount">The total number of matches.</param>
    /// <param name="items">The items of this page.</param>
    /// <returns>An object with <c>totalCount</c> and <c>items</c>.</returns>
    protected object Page(User user, int totalCount, IEnumerable<BaseItem> items)
        => new
        {
            totalCount,
            items = items.Select(i => ItemMapper.Summary(i, UserDataManager.GetUserData(user, i))).ToList(),
        };
}
