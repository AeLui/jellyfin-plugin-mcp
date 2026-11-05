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
using MediaBrowser.Model.Entities;

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

    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowseTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public BrowseTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ItemLookup lookup)
        : base(userManager, settings, userDataManager)
    {
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
    public override string Title => "Browse folder";

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

        // Mirrors ItemsController: a library root's children belong to its physical folders, and playlist and collection
        // libraries are lists of Playlist/BoxSet items, so the folder translates the query itself (Folder.GetItems).
        var collectionType = (parent as IHasCollectionType)?.CollectionType;
        BaseItemKind[] types = collectionType switch
        {
            CollectionType.playlists => [BaseItemKind.Playlist],
            CollectionType.boxsets when parent is ICollectionFolder => [BaseItemKind.BoxSet],
            _ => [],
        };
        var query = new InternalItemsQuery(user)
        {
            ParentId = parentId,
            IncludeItemTypes = types,
            Recursive = types.Length > 0,
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

        // Visibility guarantee comes from Jellyfin, not from this plugin (v12.4): Folder.GetItemsInternal builds the child
        // list with GetChildren(user) / AddChildren, which keeps only children with IsVisible(user) and resolves linked
        // children with GetLinkedChildren(user) (Playlist and BoxSet override IsVisible with owner/share/library rules),
        // and the Playlist/BoxSet type queries run through QueryWithPostFiltering (IsVisibleStandalone(user)). So no extra
        // VisibleItems filtering is needed here; the in-repo tests only prove delegation, see the manual checklist.
        var result = parent.GetItems(query);
        return Task.FromResult(ToolResult.Json(Page(user, result.TotalRecordCount, result.Items)));
    }
}
