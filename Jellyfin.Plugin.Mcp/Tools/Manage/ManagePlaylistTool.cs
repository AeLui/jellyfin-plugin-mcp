using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;

namespace Jellyfin.Plugin.Mcp.Tools.Manage;

/// <summary>
/// Creates, changes and deletes playlists.
/// </summary>
public sealed class ManagePlaylistTool : ManageToolBase
{
    private const int MaxItems = 100;
    private const string Failed = "The server refused the playlist operation";

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"action":{"type":"string","enum":["create","add_items","remove_items","rename","delete"],"description":"What to do."},"playlistId":{"type":"string","description":"Playlist id (GUID); required for every action except create."},"name":{"type":"string","description":"Playlist name; required for create and rename."},"itemIds":{"type":"array","items":{"type":"string"},"description":"Item ids (GUIDs) to put in the playlist; optional for create, required for add_items."},"entryIds":{"type":"array","items":{"type":"string"},"description":"Entry ids from jellyfin_list_playlists; required for remove_items."}},"required":["action"]}""").RootElement.Clone();

    private static readonly JsonElement ResultSchema = OutputSchemas.Parse(
        "{\"type\":\"object\",\"oneOf\":["
            + Variant("create", "\"name\":{\"type\":\"string\"}", "name") + ","
            + Variant("add_items", "\"requestedCount\":{\"type\":\"integer\"}", "requestedCount") + ","
            + Variant("remove_items", "\"removedCount\":{\"type\":\"integer\"}", "removedCount") + ","
            + Variant("rename", "\"name\":{\"type\":\"string\"}", "name") + ","
            + Variant("delete", null, null)
            + "]}");

    private readonly ItemLookup _lookup;
    private readonly IPlaylistManager _playlistManager;
    private readonly PlaylistResolver _resolver;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManagePlaylistTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="playlistManager">The playlist manager.</param>
    /// <param name="resolver">The playlist resolver.</param>
    /// <param name="libraryManager">The library manager.</param>
    public ManagePlaylistTool(IUserManager userManager, IPluginSettings settings, ItemLookup lookup, IPlaylistManager playlistManager, PlaylistResolver resolver, ILibraryManager libraryManager)
        : base(userManager, settings)
    {
        _lookup = lookup;
        _playlistManager = playlistManager;
        _resolver = resolver;
        _libraryManager = libraryManager;
    }

    private enum PlaylistAction
    {
        Create,
        AddItems,
        RemoveItems,
        Rename,
        Delete,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_manage_playlist";

    /// <inheritdoc />
    public override string Title => "Manage playlist";

    /// <inheritdoc />
    public override string Description => "Manage playlists. Actions: create (name, optional itemIds), add_items (itemIds), remove_items (entryIds), rename (name) and delete, which permanently deletes the playlist. Every action changes data. Only playlists you own, or that are shared with you with edit permission, can be changed; only the owner can delete.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => ResultSchema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, true, false);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var action = args.GetEnum<PlaylistAction>("action") ?? throw new ToolException("Invalid argument 'action': is required");
        var user = GetUser(principal);
        return action switch
        {
            PlaylistAction.Create => await CreateAsync(args, user).ConfigureAwait(false),
            PlaylistAction.AddItems => await AddItemsAsync(args, user).ConfigureAwait(false),
            PlaylistAction.RemoveItems => await RemoveItemsAsync(args, user).ConfigureAwait(false),
            PlaylistAction.Rename => await RenameAsync(args, user).ConfigureAwait(false),
            _ => await DeleteAsync(args, user).ConfigureAwait(false),
        };
    }

    private static string Format(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);

    private static string RequiredName(ToolArguments args)
        => args.GetRequiredString("name").Trim();

    private static ToolException Invalid(string name, string reason)
        => new(string.Create(CultureInfo.InvariantCulture, $"Invalid argument '{name}': {reason}"));

    private IReadOnlyList<Guid> VisibleItemIds(ToolArguments args, User user, bool required)
    {
        var ids = args.GetGuidArray("itemIds");
        if (required && ids.Count == 0)
        {
            throw Invalid("itemIds", "at least one id is required");
        }

        if (ids.Count > MaxItems)
        {
            throw Invalid("itemIds", string.Create(CultureInfo.InvariantCulture, $"at most {MaxItems} ids"));
        }

        foreach (var id in ids)
        {
            _lookup.GetVisible(user, id);
        }

        return ids;
    }

    private static string Variant(string action, string? extraProperty, string? extraRequired)
    {
        var properties = "\"action\":{\"const\":\"" + action + "\"},\"playlistId\":{\"type\":\"string\"}"
            + (extraProperty is null ? string.Empty : "," + extraProperty);
        return extraRequired is null
            ? OutputSchemas.ObjectJson(properties, "action", "playlistId")
            : OutputSchemas.ObjectJson(properties, "action", "playlistId", extraRequired);
    }

    private async Task<ToolResult> CreateAsync(ToolArguments args, User user)
    {
        var name = RequiredName(args);
        var ids = VisibleItemIds(args, user, required: false);
        PlaylistCreationResult? created = null;
        await RunAsync(
            async () => created = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = name,
                ItemIdList = ids,
                UserId = user.Id,
                Users = [],
                Public = false,
            }).ConfigureAwait(false),
            Failed).ConfigureAwait(false);
        return ToolResult.Json(new { action = "create", playlistId = created?.Id, name });
    }

    private async Task<ToolResult> AddItemsAsync(ToolArguments args, User user)
    {
        var playlist = _resolver.GetEditable(user, args.GetRequiredGuid("playlistId"));
        var ids = VisibleItemIds(args, user, required: true);
        await RunAsync(() => _playlistManager.AddItemToPlaylistAsync(playlist.Id, ids.ToArray(), null, user.Id), Failed).ConfigureAwait(false);
        return ToolResult.Json(new { action = "add_items", playlistId = Format(playlist.Id), requestedCount = ids.Count });
    }

    private async Task<ToolResult> RemoveItemsAsync(ToolArguments args, User user)
    {
        var playlist = _resolver.GetEditable(user, args.GetRequiredGuid("playlistId"));
        var requested = args.GetGuidArray("entryIds").Select(Format).Distinct(StringComparer.Ordinal).ToList();
        if (requested.Count == 0)
        {
            throw Invalid("entryIds", "at least one id is required");
        }

        if (requested.Count > MaxItems)
        {
            throw Invalid("entryIds", string.Create(CultureInfo.InvariantCulture, $"at most {MaxItems} ids"));
        }

        // Only entries the user can see may be removed, so hidden items are not probed through this tool.
        var known = playlist.GetManageableItems()
            .Where(e => e.Item2.IsVisible(user))
            .Select(e => PlaylistResolver.EntryId(e.Item1))
            .ToHashSet(StringComparer.Ordinal);
        if (requested.Any(e => !known.Contains(e)))
        {
            throw Invalid("entryIds", "contains an id that is not an entry of this playlist");
        }

        await RunAsync(() => _playlistManager.RemoveItemFromPlaylistAsync(Format(playlist.Id), requested), Failed).ConfigureAwait(false);
        return ToolResult.Json(new { action = "remove_items", playlistId = Format(playlist.Id), removedCount = requested.Count });
    }

    private async Task<ToolResult> RenameAsync(ToolArguments args, User user)
    {
        var playlist = _resolver.GetEditable(user, args.GetRequiredGuid("playlistId"));
        var name = RequiredName(args);

        // Only the name is set; null Ids/Users/Public leave items, shares and visibility untouched.
        await RunAsync(
            () => _playlistManager.UpdatePlaylist(new PlaylistUpdateRequest { Id = playlist.Id, UserId = user.Id, Name = name }),
            Failed).ConfigureAwait(false);
        return ToolResult.Json(new { action = "rename", playlistId = Format(playlist.Id), name });
    }

    private async Task<ToolResult> DeleteAsync(ToolArguments args, User user)
    {
        var playlist = _resolver.GetOwned(user, args.GetRequiredGuid("playlistId"));

        // Same authorization LibraryController.DeleteItem applies, on top of the ownership check.
        if (!playlist.CanDelete(user))
        {
            throw new ToolException(PlaylistResolver.NotFound);
        }

        await RunAsync(
            () =>
            {
                _libraryManager.DeleteItem(playlist, new DeleteOptions { DeleteFileLocation = true }, true);
                return Task.CompletedTask;
            },
            Failed).ConfigureAwait(false);
        return ToolResult.Json(new { action = "delete", playlistId = Format(playlist.Id) });
    }
}
