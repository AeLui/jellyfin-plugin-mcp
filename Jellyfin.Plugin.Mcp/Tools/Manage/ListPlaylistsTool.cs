using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;

namespace Jellyfin.Plugin.Mcp.Tools.Manage;

/// <summary>
/// Lists the playlists a user can access, or the entries of one playlist.
/// </summary>
public sealed class ListPlaylistsTool : ManageToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """
        {"type":"object","properties":{
        "playlistId":{"type":"string","description":"Optional playlist id (GUID). When given, lists the entries of that playlist; each entry has an entryId for jellyfin_manage_playlist remove_items."},
        "limit":{"type":"integer","description":"Page size, default 20."},
        "startIndex":{"type":"integer","description":"Offset of the first result, default 0."}}}
        """).RootElement.Clone();

    private readonly IPlaylistManager _playlistManager;
    private readonly PlaylistResolver _resolver;
    private readonly IUserDataManager _userDataManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListPlaylistsTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="playlistManager">The playlist manager.</param>
    /// <param name="resolver">The playlist resolver.</param>
    /// <param name="userDataManager">The user data manager.</param>
    public ListPlaylistsTool(IUserManager userManager, IPluginSettings settings, IPlaylistManager playlistManager, PlaylistResolver resolver, IUserDataManager userDataManager)
        : base(userManager, settings)
    {
        _playlistManager = playlistManager;
        _resolver = resolver;
        _userDataManager = userDataManager;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_list_playlists";

    /// <inheritdoc />
    public override string Description => "List the playlists the user can access, or, with playlistId, the entries of one playlist.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var playlistId = args.GetGuid("playlistId");
        var (startIndex, limit) = args.GetPaging(MaxPageSize);
        var user = GetUser(principal);

        if (playlistId is { } id)
        {
            var playlist = _resolver.GetReadable(user, id);
            var entries = playlist.GetManageableItems().Where(e => e.Item2.IsVisible(user)).ToList();
            return Task.FromResult(ToolResult.Json(new
            {
                totalCount = entries.Count,
                items = entries.Skip(startIndex).Take(limit)
                    .Select(e => ItemMapper.Entry(PlaylistResolver.EntryId(e.Item1), e.Item2, _userDataManager.GetUserData(user, e.Item2)))
                    .ToList(),
            }));
        }

        var playlists = _playlistManager.GetPlaylists(user.Id)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(ToolResult.Json(new
        {
            totalCount = playlists.Count,
            items = playlists.Skip(startIndex).Take(limit)
                .Select(p => new
                {
                    id = p.Id.ToString("N", CultureInfo.InvariantCulture),
                    name = p.Name,
                    isOwner = p.OwnerUserId.Equals(user.Id),
                    canEdit = p.OwnerUserId.Equals(user.Id) || p.Shares.Any(s => s.CanEdit && s.UserId.Equals(user.Id)),
                    isPublic = p.OpenAccess,
                })
                .ToList(),
        }));
    }
}
