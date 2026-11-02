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
/// Lists the direct children of a library or folder.
/// </summary>
public sealed class BrowseTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "parentId":{"type":"string","description":"Library or folder id (GUID)."},
        "sortBy":{"type":"string","enum":["name","date_added","premiere_date","community_rating","random"],"description":"Default name."},
        "sortOrder":{"type":"string","enum":["asc","desc"],"description":"Default asc."},
        "filter":{"type":"string","enum":["all","unplayed","played","favorites"],"description":"Default all."},
        "limit":{"type":"integer","description":"Page size, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first result, default 0."}},
        "required":["parentId"]}
        """).RootElement.Clone();

    private readonly ILibraryManager _libraryManager;
    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowseTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public BrowseTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ILibraryManager libraryManager, ItemLookup lookup)
        : base(userManager, settings, userDataManager)
    {
        _libraryManager = libraryManager;
        _lookup = lookup;
    }

    private enum SortKey
    {
        Name,
        DateAdded,
        PremiereDate,
        CommunityRating,
        Random,
    }

    private enum SortDirection
    {
        Asc,
        Desc,
    }

    private enum BrowseFilter
    {
        All,
        Unplayed,
        Played,
        Favorites,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_browse";

    /// <inheritdoc />
    public override string Description => "List the direct children of a library or folder with sorting and played/favorite filters.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var parentId = args.GetRequiredGuid("parentId");
        var sortBy = args.GetEnum<SortKey>("sortBy") ?? SortKey.Name;
        var direction = args.GetEnum<SortDirection>("sortOrder") ?? SortDirection.Asc;
        var filter = args.GetEnum<BrowseFilter>("filter") ?? BrowseFilter.All;
        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var user = GetUser(principal);
        var parent = _lookup.GetVisibleContainer(user, parentId);

        var sortField = sortBy switch
        {
            SortKey.DateAdded => ItemSortBy.DateCreated,
            SortKey.PremiereDate => ItemSortBy.PremiereDate,
            SortKey.CommunityRating => ItemSortBy.CommunityRating,
            SortKey.Random => ItemSortBy.Random,
            _ => ItemSortBy.SortName,
        };
        var query = new InternalItemsQuery(user)
        {
            ParentId = parentId,
            Recursive = false,
            StartIndex = startIndex,
            Limit = limit,
            EnableTotalRecordCount = true,
            OrderBy = new List<(ItemSortBy, SortOrder)> { (sortField, direction == SortDirection.Desc ? SortOrder.Descending : SortOrder.Ascending) },
        };
        switch (filter)
        {
            case BrowseFilter.Unplayed:
                query.IsPlayed = false;
                break;
            case BrowseFilter.Played:
                query.IsPlayed = true;
                break;
            case BrowseFilter.Favorites:
                query.IsFavorite = true;
                break;
        }

        var result = _libraryManager.GetVisibleItemsResult(query, parent is BasePluginFolder);
        return Task.FromResult(ToolResult.Json(Page(user, result.TotalRecordCount, result.Items)));
    }
}
