using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Lists the next unwatched episodes of the series the user is following.
/// </summary>
public sealed class NextUpTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "seriesId":{"type":"string","description":"Restrict to one series (GUID)."},
        "limit":{"type":"integer","description":"Page size, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first result, default 0."}}}
        """).RootElement.Clone();

    private readonly ITVSeriesManager _tvSeriesManager;
    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="NextUpTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="tvSeriesManager">The TV series manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public NextUpTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ITVSeriesManager tvSeriesManager, ItemLookup lookup)
        : base(userManager, settings, userDataManager)
    {
        _tvSeriesManager = tvSeriesManager;
        _lookup = lookup;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_next_up";

    /// <inheritdoc />
    public override string Title => "Next up";

    /// <inheritdoc />
    public override string Description => "List the next episode to watch for each series the user is following, or for one series.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var seriesId = args.GetGuid("seriesId");
        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var user = GetUser(principal);
        if (seriesId is not null && _lookup.GetVisible(user, seriesId.Value) is not Series)
        {
            throw new ToolException("Invalid argument 'seriesId': item is not a series");
        }

        // Without a series, ITVSeriesManager walks only the user's own library folders (GetUserRootFolder().GetChildren(user)).
        var query = new NextUpQuery
        {
            User = user,
            SeriesId = seriesId,
            StartIndex = startIndex,
            Limit = limit,
        };

        var result = _tvSeriesManager.GetNextUp(query, new DtoOptions());
        return Task.FromResult(ToolResult.Json(Page(user, result.TotalRecordCount, result.Items)));
    }
}
