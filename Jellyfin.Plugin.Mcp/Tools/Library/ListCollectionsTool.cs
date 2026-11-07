using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Lists the user's collections, or the collections that contain one item.
/// </summary>
public sealed class ListCollectionsTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"itemId":{"type":"string","description":"Only the collections that contain this item (GUID)."},"limit":{"type":"integer","minimum":1},"startIndex":{"type":"integer","minimum":0}}}""").RootElement.Clone();

    private readonly ItemLookup _lookup;
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListCollectionsTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="collectionManager">The collection manager.</param>
    public ListCollectionsTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ItemLookup lookup, ILibraryManager libraryManager, ICollectionManager collectionManager)
        : base(userManager, settings, userDataManager)
    {
        _lookup = lookup;
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_list_collections";

    /// <inheritdoc />
    public override string Title => "List collections";

    /// <inheritdoc />
    public override string Description => "List your collections (box sets), or with itemId the collections that contain that item. Open a collection with jellyfin_browse using its id as parentId.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => OutputSchemas.CollectionPage;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var itemId = args.GetGuid("itemId");
        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var scope = GetScopedUser(principal);
        var user = scope.User;
        int totalCount;
        IReadOnlyList<BaseItem> page;
        if (itemId is null)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.BoxSet],
                Recursive = true,
                StartIndex = startIndex,
                Limit = limit,
                EnableTotalRecordCount = true,
                OrderBy = new List<(ItemSortBy, SortOrder)> { (ItemSortBy.SortName, SortOrder.Ascending) },
            };
            var result = _libraryManager.GetVisibleItemsResult(query, scope);
            totalCount = result.TotalRecordCount;
            page = result.Items;
        }
        else
        {
            _lookup.GetVisible(scope, itemId.Value);
            var visible = _collectionManager.GetCollectionsContainingItem(user, itemId.Value)
                .Where(c => VisibleItems.IsVisible(_lookup, c, scope))
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            totalCount = visible.Count;
            page = visible.Skip(startIndex).Take(limit).ToList<BaseItem>();
        }

        var items = page.Select(c => ItemMapper.Collection(c, UserDataManager.GetUserData(user, c), ChildCount(c, scope))).ToList();
        return Task.FromResult(ToolResult.Json(new { totalCount, items }));
    }

    // BoxSet filters its linked children per user (v12.4 BoxSet.FilterLinkedChildrenPerUser), so this counts what the user can
    // open; a library-restricted token only counts the entries of its libraries.
    private int ChildCount(BaseItem collection, ScopedUser scope)
        => collection is Folder folder ? _lookup.VisibleChildren(scope, folder).Count : 0;
}
