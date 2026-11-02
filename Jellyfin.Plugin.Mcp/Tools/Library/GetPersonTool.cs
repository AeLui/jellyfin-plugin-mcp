using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Returns a person and the items of theirs the user can see.
/// </summary>
public sealed class GetPersonTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "personId":{"type":"string","description":"Person id (GUID), as listed in the people of an item. Either personId or name is required."},
        "name":{"type":"string","description":"Exact person name. Either personId or name is required."},
        "limit":{"type":"integer","description":"Page size of the items list, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first item, default 0."}}}
        """).RootElement.Clone();

    private readonly ILibraryManager _libraryManager;
    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetPersonTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public GetPersonTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ILibraryManager libraryManager, ItemLookup lookup)
        : base(userManager, settings, userDataManager)
    {
        _libraryManager = libraryManager;
        _lookup = lookup;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_get_person";

    /// <inheritdoc />
    public override string Description => "Get a person (actor, director, ...) by id or name, with the items of theirs that the user can see.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var personId = args.GetGuid("personId");
        var name = args.GetString("name");
        if (personId is null && string.IsNullOrWhiteSpace(name))
        {
            throw new ToolException("Invalid arguments: one of 'personId' or 'name' is required");
        }

        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var user = GetUser(principal);

        // Mirrors PersonsController.GetPerson / DtoService: the person is loaded without a user (people belong to no
        // library) and only parental visibility is checked. Only person metadata is returned from this lookup; the
        // items below come from a user-scoped query, so library access still applies.
        var person = personId is { } id ? _lookup.GetVisiblePerson(user, id) : _lookup.GetVisiblePerson(user, name!);

        // Person ids are deterministic from names, so a person is only revealed when the user can see at least one of
        // their items; otherwise (whatever startIndex is) the person looks exactly like an unknown one.
        if (_libraryManager.GetVisibleItemsResult(PersonQuery(user, person, 0, 1)).Items.Count == 0)
        {
            throw new ToolException("Item not found");
        }

        var items = _libraryManager.GetVisibleItemsResult(PersonQuery(user, person, startIndex, limit));

        var result = new Dictionary<string, object>
        {
            ["id"] = person.Id.ToString("N", CultureInfo.InvariantCulture),
            ["name"] = person.Name,
            ["totalCount"] = items.TotalRecordCount,
            ["items"] = items.Items.Select(i => ItemMapper.Summary(i, UserDataManager.GetUserData(user, i))).ToList(),
        };
        if (!string.IsNullOrWhiteSpace(person.Overview))
        {
            result["overview"] = person.Overview;
        }

        if (ItemMapper.CalendarDate(person.PremiereDate) is { } birth)
        {
            result["birthDate"] = birth;
        }

        return Task.FromResult(ToolResult.Json(result));
    }

    // PersonIds without ParentId/ItemIds: ILibraryManager adds the user's library access to the query.
    private static InternalItemsQuery PersonQuery(User user, Person person, int startIndex, int limit)
        => new(user)
        {
            PersonIds = [person.Id],
            ExcludeItemTypes = VisibleItems.ExcludedKinds(),
            Recursive = true,
            StartIndex = startIndex,
            Limit = limit,
            EnableTotalRecordCount = true,
            OrderBy = new List<(ItemSortBy, SortOrder)> { (ItemSortBy.SortName, SortOrder.Ascending) },
        };
}
