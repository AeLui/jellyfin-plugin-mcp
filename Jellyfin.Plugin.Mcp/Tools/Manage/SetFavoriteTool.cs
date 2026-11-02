using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Mcp.Tools.Manage;

/// <summary>
/// Adds or removes an item from the favorites of the token's user.
/// </summary>
public sealed class SetFavoriteTool : ManageToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"itemId":{"type":"string","description":"Item id (GUID)."},"favorite":{"type":"boolean","description":"true to add to favorites, false to remove."}},"required":["itemId","favorite"]}""").RootElement.Clone();

    private readonly ItemLookup _lookup;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetFavoriteTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="userDataManager">The user data manager.</param>
    public SetFavoriteTool(IUserManager userManager, IPluginSettings settings, ItemLookup lookup, IUserDataManager userDataManager)
        : base(userManager, settings)
    {
        _lookup = lookup;
        _userDataManager = userDataManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_set_favorite";

    /// <inheritdoc />
    public override string Description => "Add an item to, or remove it from, the favorites of the token's user.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var id = args.GetRequiredGuid("itemId");
        var favorite = args.GetBool("favorite") ?? throw new ToolException("Invalid argument 'favorite': is required");
        var user = GetUser(principal);
        var item = _lookup.GetVisible(user, id);

        // The same calls UserLibraryController makes.
        var data = _userDataManager.GetUserData(user, item) ?? throw new ToolException("Item not found");
        data.IsFavorite = favorite;
        _userDataManager.SaveUserData(user, item, data, UserDataSaveReason.UpdateUserRating, ct);

        return Task.FromResult(ToolResult.Json(ItemMapper.Summary(item, _userDataManager.GetUserData(user, item))));
    }
}
