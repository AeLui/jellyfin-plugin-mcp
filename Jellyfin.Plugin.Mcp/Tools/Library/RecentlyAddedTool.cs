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
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Lists the most recently added items.
/// </summary>
public sealed class RecentlyAddedTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "libraryId":{"type":"string","description":"Restrict to one library (GUID)."},
        "types":{"type":"array","items":{"type":"string","enum":["movie","episode","video","audio"]},"description":"Restrict to these item types; folders are never returned."},
        "limit":{"type":"integer","description":"Page size, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first result, default 0."}}}
        """).RootElement.Clone();

    private readonly ILibraryManager _libraryManager;
    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecentlyAddedTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public RecentlyAddedTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ILibraryManager libraryManager, ItemLookup lookup)
        : base(userManager, settings, userDataManager)
    {
        _libraryManager = libraryManager;
        _lookup = lookup;
    }

    private enum RecentType
    {
        Movie,
        Episode,
        Video,
        Audio,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_recently_added";

    /// <inheritdoc />
    public override string Title => "Recently added";

    /// <inheritdoc />
    public override string Description => "List the most recently added items (newest first), optionally restricted to one library or to some item types.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => OutputSchemas.RecentItemPage;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var libraryId = args.GetGuid("libraryId");
        var types = args.GetEnumArray<RecentType>("types");
        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var user = GetUser(principal);
        if (libraryId is not null)
        {
            _lookup.EnsureUserLibrary(user, libraryId.Value);
        }

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = types.Select(t => Enum.Parse<BaseItemKind>(t.ToString())).Distinct().ToArray(),
            IsFolder = false,
            Recursive = true,
            StartIndex = startIndex,
            Limit = limit,
            EnableTotalRecordCount = true,
            OrderBy = new List<(ItemSortBy, SortOrder)> { (ItemSortBy.DateCreated, SortOrder.Descending) },
        };
        if (libraryId is not null)
        {
            query.ParentId = libraryId.Value;
        }

        var result = _libraryManager.GetVisibleItemsResult(query);
        var page = new
        {
            totalCount = result.TotalRecordCount,
            items = result.Items.Select(i => ItemMapper.Recent(i, UserDataManager.GetUserData(user, i))).ToList(),
        };
        return Task.FromResult(ToolResult.Json(page));
    }
}
