using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Finds items similar to one item ("more like this").
/// </summary>
public sealed class SimilarItemsTool : LibraryToolBase
{
    private const int DefaultLimit = 20;

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"itemId":{"type":"string","description":"Id (GUID) of a movie, series, album, artist or trailer."},"limit":{"type":"integer","minimum":1,"description":"Maximum number of results (default 20)."}},"required":["itemId"]}""").RootElement.Clone();

    private readonly ItemLookup _lookup;
    private readonly ILibraryManager _libraryManager;
    private readonly ISimilarItemsManager _similarItems;

    /// <summary>
    /// Initializes a new instance of the <see cref="SimilarItemsTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="similarItems">The similar items manager.</param>
    public SimilarItemsTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ItemLookup lookup, ILibraryManager libraryManager, ISimilarItemsManager similarItems)
        : base(userManager, settings, userDataManager)
    {
        _lookup = lookup;
        _libraryManager = libraryManager;
        _similarItems = similarItems;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_similar_items";

    /// <inheritdoc />
    public override string Title => "Find similar items";

    /// <inheritdoc />
    public override string Description => "Find items similar to a movie, series, album or artist (more like this).";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => OutputSchemas.ItemPage;

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var id = args.GetRequiredGuid("itemId");
        var limit = Math.Clamp(args.GetInt("limit") ?? DefaultLimit, 1, MaxPageSize);
        var scope = GetScopedUser(principal);
        var user = scope.User;
        var item = _lookup.GetVisible(scope, id);
        if (item is Episode)
        {
            throw new ToolException("Similar items are not available for episodes; use the series id");
        }

        if (item is not (Movie or Series or MusicAlbum or MusicArtist or Trailer))
        {
            throw new ToolException("Similar items are not available for this item type");
        }

        // Same call as LibraryController.GetSimilarItems (v12.4); the manager filters results by the user's library access.
        // A library-restricted token drops results from other libraries, so it asks for a larger pool first.
        var similar = await _similarItems.GetSimilarItemsAsync(
            item,
            Array.Empty<Guid>(),
            user,
            new DtoOptions(false),
            scope.IsRestricted ? Math.Max(limit, MaxPageSize) : limit,
            _libraryManager.GetLibraryOptions(item),
            ct).ConfigureAwait(false);
        var visible = similar.Where(i => VisibleItems.IsVisible(_lookup, i, scope)).Take(limit).ToList();
        return ToolResult.Json(Page(user, visible.Count, visible));
    }
}
