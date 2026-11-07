using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Returns personal movie recommendations grouped by reason.
/// </summary>
public sealed class MovieRecommendationsTool : LibraryToolBase
{
    private const int DefaultCategoryLimit = 5;
    private const int MaxCategoryLimit = 10;
    private const int DefaultItemsPerCategory = 8;
    private const int MaxItemsPerCategory = 20;

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"libraryId":{"type":"string","description":"Restrict to one library (GUID)."},"categoryLimit":{"type":"integer","minimum":1,"maximum":10,"description":"Number of categories (default 5)."},"itemsPerCategory":{"type":"integer","minimum":1,"maximum":20,"description":"Movies per category (default 8)."}}}""").RootElement.Clone();

    private static readonly JsonElement ResultSchema = OutputSchemas.Object(
        "\"categories\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
            "\"reason\":{\"type\":\"string\",\"description\":\"similar_to_recently_played, similar_to_liked_item, has_director_from_recently_played, has_actor_from_recently_played, has_liked_director or has_liked_actor.\"},"
                + "\"baseline\":{\"type\":\"string\",\"description\":\"The movie or person the category is based on.\"},"
                + "\"items\":" + OutputSchemas.ArrayOf(OutputSchemas.ItemSummary),
            "reason",
            "items")),
        "categories");

    private readonly ItemLookup _lookup;
    private readonly ISimilarItemsManager _similarItems;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieRecommendationsTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="similarItems">The similar items manager.</param>
    public MovieRecommendationsTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ItemLookup lookup, ISimilarItemsManager similarItems)
        : base(userManager, settings, userDataManager)
    {
        _lookup = lookup;
        _similarItems = similarItems;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_movie_recommendations";

    /// <inheritdoc />
    public override string Title => "Recommend movies";

    /// <inheritdoc />
    public override string Description => "Personal movie recommendations grouped by reason (similar to what you watched or liked, same director or actor).";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => ResultSchema;

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var libraryId = args.GetGuid("libraryId");
        var categoryLimit = Math.Clamp(args.GetInt("categoryLimit") ?? DefaultCategoryLimit, 1, MaxCategoryLimit);
        var itemsPerCategory = Math.Clamp(args.GetInt("itemsPerCategory") ?? DefaultItemsPerCategory, 1, MaxItemsPerCategory);
        var user = GetUser(principal);
        if (libraryId is not null)
        {
            _lookup.GetVisibleContainer(user, libraryId.Value);
        }

        var recommendations = await _similarItems.GetMovieRecommendationsAsync(
            user,
            libraryId ?? Guid.Empty,
            categoryLimit,
            itemsPerCategory,
            new DtoOptions(false),
            ct).ConfigureAwait(false);
        var categories = recommendations
            .Select(c => new
            {
                reason = SnakeCase.From(c.RecommendationType.ToString()),
                baseline = string.IsNullOrEmpty(c.BaselineItemName) ? null : c.BaselineItemName,
                items = c.Items
                    .Where(i => VisibleItems.IsVisible(i, user))
                    .Select(i => ItemMapper.Summary(i, UserDataManager.GetUserData(user, i)))
                    .ToList(),
            })
            .Where(c => c.items.Count > 0)
            .ToList();
        return ToolResult.Json(new { categories });
    }
}
