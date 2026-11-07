using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Searches the library.
/// </summary>
public sealed class SearchTool : LibraryToolBase
{
    private const int MinYear = 1880;
    private const int MaxYearSpan = 200;

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "query":{"type":"string","description":"Text to search for."},
        "types":{"type":"array","items":{"type":"string","enum":["movie","series","season","episode","audio","music_album","music_artist","playlist","box_set"]},"description":"Restrict to these item types. Playlists and collections (playlist, box_set) are only returned when requested here."},
        "genres":{"type":"array","items":{"type":"string"},"description":"Restrict to these genres."},
        "yearFrom":{"type":"integer","description":"Earliest production year; without yearTo runs to the current year."},
        "yearTo":{"type":"integer","description":"Latest production year; without yearFrom starts at 1880."},
        "libraryId":{"type":"string","description":"Restrict to one library (GUID)."},
        "limit":{"type":"integer","description":"Page size, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first result, default 0."}},
        "required":["query"]}
        """).RootElement.Clone();

    private readonly ILibraryManager _libraryManager;
    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public SearchTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ILibraryManager libraryManager, ItemLookup lookup)
        : base(userManager, settings, userDataManager)
    {
        _libraryManager = libraryManager;
        _lookup = lookup;
    }

    private enum SearchType
    {
        Movie,
        Series,
        Season,
        Episode,
        Audio,
        MusicAlbum,
        MusicArtist,
        Playlist,
        BoxSet,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_search";

    /// <inheritdoc />
    public override string Title => "Search library";

    /// <inheritdoc />
    public override string Description => "Search the library by text, optionally filtered by type, genre, production year range or library.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => OutputSchemas.ItemPage;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var term = args.GetRequiredString("query");
        var types = args.GetEnumArray<SearchType>("types");
        var genres = args.GetStringArray("genres");
        var yearFrom = Year(args, "yearFrom");
        var yearTo = Year(args, "yearTo");
        var libraryId = args.GetGuid("libraryId");
        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var years = YearRange(yearFrom, yearTo);
        var scope = GetScopedUser(principal);
        var user = scope.User;
        if (libraryId is not null)
        {
            _lookup.EnsureUserLibrary(scope, libraryId.Value);
        }

        var query = new InternalItemsQuery(user)
        {
            SearchTerm = term,
            IncludeItemTypes = types.Select(t => Enum.Parse<BaseItemKind>(t.ToString())).Distinct().ToArray(),
            Genres = genres,
            Recursive = true,
            StartIndex = startIndex,
            Limit = limit,
            EnableTotalRecordCount = true,
        };
        if (query.IncludeItemTypes.Length == 0)
        {
            // Playlists and collections are only searched when asked for by type, where they are checked per user.
            query.ExcludeItemTypes = VisibleItems.ExcludedKinds();
        }

        if (years.Length > 0)
        {
            query.Years = years;
        }

        if (libraryId is not null)
        {
            query.ParentId = libraryId.Value;
        }

        var result = _libraryManager.GetVisibleItemsResult(query, scope);
        return Task.FromResult(ToolResult.Json(Page(user, result.TotalRecordCount, result.Items)));
    }

    private static int[] YearRange(int? yearFrom, int? yearTo)
    {
        if (yearFrom is null && yearTo is null)
        {
            return [];
        }

        var from = yearFrom ?? MinYear;
        var to = yearTo ?? Math.Max(from, DateTime.UtcNow.Year);
        if (from > to)
        {
            throw new ToolException("Invalid argument 'yearFrom': must not be greater than 'yearTo'");
        }

        if (to - from >= MaxYearSpan)
        {
            throw new ToolException($"Invalid argument 'yearTo': range must span fewer than {MaxYearSpan} years");
        }

        return Enumerable.Range(from, to - from + 1).ToArray();
    }

    private static int? Year(ToolArguments args, string name)
    {
        var year = args.GetInt(name);
        return year is < 1 or > 9999
            ? throw new ToolException($"Invalid argument '{name}': must be between 1 and 9999")
            : year;
    }
}
