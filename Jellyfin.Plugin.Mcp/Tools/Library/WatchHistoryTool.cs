using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Lists the items the user has watched.
/// </summary>
public sealed class WatchHistoryTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "limit":{"type":"integer","description":"Page size, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first result, default 0."}}}
        """).RootElement.Clone();

    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="WatchHistoryTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    public WatchHistoryTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ILibraryManager libraryManager)
        : base(userManager, settings, userDataManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_watch_history";

    /// <inheritdoc />
    public override string Title => "Watch history";

    /// <inheritdoc />
    public override string Description => "List items the user has played, most recently played first, with the last played date.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var user = GetUser(principal);

        // No ParentId/ItemIds: ILibraryManager scopes the query to the libraries the user may access.
        var query = new InternalItemsQuery(user)
        {
            IsPlayed = true,
            IsFolder = false,
            Recursive = true,
            StartIndex = startIndex,
            Limit = limit,
            EnableTotalRecordCount = true,
            OrderBy = new List<(ItemSortBy, SortOrder)> { (ItemSortBy.DatePlayed, SortOrder.Descending) },
        };

        var result = _libraryManager.GetVisibleItemsResult(query);
        return Task.FromResult(ToolResult.Json(Page(user, result.TotalRecordCount, result.Items)));
    }
}
