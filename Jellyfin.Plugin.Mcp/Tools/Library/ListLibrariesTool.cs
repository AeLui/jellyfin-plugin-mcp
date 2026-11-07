using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Library;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Lists the libraries the user can access.
/// </summary>
public sealed class ListLibrariesTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private static readonly JsonElement LibrariesSchema = OutputSchemas.Object(
        "\"items\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
            "\"id\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},\"collectionType\":{\"type\":\"string\"}",
            "id")),
        "items");

    private readonly IUserViewManager _userViewManager;
    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListLibrariesTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="userViewManager">The user view manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public ListLibrariesTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, IUserViewManager userViewManager, ItemLookup lookup)
        : base(userManager, settings, userDataManager)
    {
        _userViewManager = userViewManager;
        _lookup = lookup;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_list_libraries";

    /// <inheritdoc />
    public override string Title => "List libraries";

    /// <inheritdoc />
    public override string Description => "List the media libraries the user can access, with their ids and collection types.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => LibrariesSchema;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var scope = GetScopedUser(principal);

        // A library-restricted token lists its allowed libraries themselves rather than the user's views, which can group
        // several libraries (and so hidden ones) into one view.
        IEnumerable<Folder> views = scope.IsRestricted
            ? _lookup.AllowedLibraries(scope)
            : _userViewManager.GetUserViews(new UserViewQuery { User = scope.User });
        var libraries = views.Select(v => new
        {
            id = v.Id.ToString("N", CultureInfo.InvariantCulture),
            name = v.Name,
            collectionType = CollectionTypeOf(v)?.ToString().ToLowerInvariant(),
        }).ToList();
        return Task.FromResult(ToolResult.Json(new { items = libraries }));
    }

    private static CollectionType? CollectionTypeOf(Folder folder)
        => folder switch
        {
            ICollectionFolder collection => collection.CollectionType,
            UserView view => view.CollectionType,
            _ => null,
        };
}
