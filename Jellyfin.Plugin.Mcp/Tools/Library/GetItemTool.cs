using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Returns the details of one item.
/// </summary>
public sealed class GetItemTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"itemId":{"type":"string","description":"Item id (GUID)."}},"required":["itemId"]}""").RootElement.Clone();

    private static readonly JsonElement DetailSchema = OutputSchemas.Parse(OutputSchemas.ItemDetail);

    private readonly ItemLookup _lookup;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetItemTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="libraryManager">The library manager.</param>
    public GetItemTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ItemLookup lookup, ILibraryManager libraryManager)
        : base(userManager, settings, userDataManager)
    {
        _lookup = lookup;
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_get_item";

    /// <inheritdoc />
    public override string Title => "Get item details";

    /// <inheritdoc />
    public override string Description => "Get the details of one item (overview, genres, studios, tags, people) by id.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => DetailSchema;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var id = args.GetRequiredGuid("itemId");
        var scope = GetScopedUser(principal);
        var item = _lookup.GetVisible(scope, id);
        var people = _libraryManager.GetPeople(item);
        var detail = ItemMapper.Detail(item, UserDataManager.GetUserData(scope.User, item), people, _lookup.GetPersonIds(scope, people));
        return Task.FromResult(ToolResult.Json(detail));
    }
}
