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
/// Lists the items the user has started but not finished.
/// </summary>
public sealed class ContinueWatchingTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "limit":{"type":"integer","description":"Page size, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first result, default 0."}}}
        """).RootElement.Clone();

    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContinueWatchingTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    public ContinueWatchingTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ILibraryManager libraryManager)
        : base(userManager, settings, userDataManager)
    {
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_continue_watching";

    /// <inheritdoc />
    public override string Description => "List movies, episodes and videos the user has started but not finished, most recently played first.";

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
            IsResumable = true,
            Recursive = true,
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Video],
            StartIndex = startIndex,
            Limit = limit,
            EnableTotalRecordCount = true,
            OrderBy = new List<(ItemSortBy, SortOrder)> { (ItemSortBy.DatePlayed, SortOrder.Descending) },
        };

        var result = _libraryManager.GetItemsResult(query);
        return Task.FromResult(ToolResult.Json(Page(user, result.TotalRecordCount, result.Items)));
    }
}
