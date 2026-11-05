using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Manage;

/// <summary>
/// Marks an item as played or unplayed for the token's user.
/// </summary>
public sealed class SetPlayedTool : ManageToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"itemId":{"type":"string","description":"Item id (GUID). Series and seasons also update their episodes."},"played":{"type":"boolean","description":"true to mark as played, false to mark as unplayed."}},"required":["itemId","played"]}""").RootElement.Clone();

    private static readonly JsonElement SummarySchema = OutputSchemas.Parse(OutputSchemas.ItemSummary);

    private readonly ItemLookup _lookup;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetPlayedTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="userDataManager">The user data manager.</param>
    public SetPlayedTool(IUserManager userManager, IPluginSettings settings, ItemLookup lookup, IUserDataManager userDataManager)
        : base(userManager, settings)
    {
        _lookup = lookup;
        _userDataManager = userDataManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_set_played";

    /// <inheritdoc />
    public override string Title => "Set watched state";

    /// <inheritdoc />
    public override string Description => "Mark an item as played or unplayed for the token's user. For a series or season this applies to all its episodes. Changes watch history. played:false on a series or season clears the play state and resume positions of all its episodes and cannot be undone.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => SummarySchema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, true, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var id = args.GetRequiredGuid("itemId");
        var played = args.GetBool("played") ?? throw new ToolException("Invalid argument 'played': is required");
        var user = GetUser(principal);
        var item = _lookup.GetVisible(user, id);

        // The same calls PlaystateController makes; folders cascade to their children.
        if (played)
        {
            item.MarkPlayed(user, null, true);
        }
        else
        {
            item.MarkUnplayed(user);
        }

        return Task.FromResult(ToolResult.Json(ItemMapper.Summary(item, _userDataManager.GetUserData(user, item))));
    }
}
