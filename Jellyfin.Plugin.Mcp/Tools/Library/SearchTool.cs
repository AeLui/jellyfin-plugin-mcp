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
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "query":{"type":"string","description":"Text to search for."},
        "types":{"type":"array","items":{"type":"string","enum":["movie","series","season","episode","audio","music_album","music_artist","person","playlist","box_set"]},"description":"Restrict to these item types."},
        "genres":{"type":"array","items":{"type":"string"},"description":"Restrict to these genres."},
        "yearFrom":{"type":"integer","description":"Earliest premiere year."},
        "yearTo":{"type":"integer","description":"Latest premiere year."},
        "libraryId":{"type":"string","description":"Restrict to one library (GUID)."},
        "limit":{"type":"integer","description":"Page size, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first result, default 0."}},
        "required":["query"]}
        """).RootElement.Clone();

    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    public SearchTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ILibraryManager libraryManager)
        : base(userManager, settings, userDataManager)
    {
        _libraryManager = libraryManager;
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
        Person,
        Playlist,
        BoxSet,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_search";

    /// <inheritdoc />
    public override string Description => "Search the library by text, optionally filtered by type, genre, premiere year range or library.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

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
        var user = GetUser(principal);

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
        if (yearFrom is not null)
        {
            query.MinPremiereDate = new DateTime(yearFrom.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        if (yearTo is not null)
        {
            query.MaxPremiereDate = new DateTime(yearTo.Value, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        }

        if (libraryId is not null)
        {
            query.ParentId = libraryId.Value;
        }

        var result = _libraryManager.GetItemsResult(query);
        return Task.FromResult(ToolResult.Json(Page(user, result.TotalRecordCount, result.Items)));
    }

    private static int? Year(ToolArguments args, string name)
    {
        var year = args.GetInt(name);
        return year is < 1 or > 9999
            ? throw new ToolException($"Invalid argument '{name}': must be between 1 and 9999")
            : year;
    }
}
