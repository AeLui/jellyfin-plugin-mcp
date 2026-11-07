using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Lists the sessions the caller can control.
/// </summary>
public sealed class ListSessionsTool : PlaybackToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private static readonly JsonElement SessionsSchema = OutputSchemas.Object(
        "\"items\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
            "\"sessionId\":{\"type\":\"string\"},\"deviceName\":{\"type\":\"string\"},\"client\":{\"type\":\"string\"},"
                + "\"userName\":{\"type\":\"string\"},\"nowPlaying\":" + SessionMapper.NowPlayingSchema + ","
                + "\"isPaused\":{\"type\":\"boolean\"},\"positionMinutes\":{\"type\":\"number\"}",
            "sessionId",
            "isPaused",
            "positionMinutes")),
        "items");

    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListSessionsTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessions">The session resolver.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public ListSessionsTool(IUserManager userManager, IPluginSettings settings, SessionResolver sessions, ISessionManager sessionManager, ItemLookup lookup)
        : base(userManager, settings, sessions, sessionManager)
    {
        _lookup = lookup;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_list_sessions";

    /// <inheritdoc />
    public override string Title => "List devices";

    /// <inheritdoc />
    public override string Description => "List the active player sessions you can control, with what each is playing.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => SessionsSchema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(true, false, true);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        var scope = principal.IsLibraryRestricted ? GetScopedUser(principal) : null;
        var result = Sessions.Controllable(principal).Select(s =>
        {
            // The caller's own sessions can play items outside a library-restricted token's libraries; their playback state
            // is left out entirely, as if nothing were playing.
            var hidden = scope is not null && IsHidden(s, scope);
            return new
            {
                sessionId = s.Id,
                deviceName = s.DeviceName,
                client = s.Client,
                userName = s.UserName,
                nowPlaying = hidden ? null : SessionMapper.NowPlaying(s, _ => true),
                isPaused = !hidden && (s.PlayState?.IsPaused ?? false),
                positionMinutes = hidden ? 0 : Math.Round((double)(s.PlayState?.PositionTicks ?? 0) / TimeSpan.TicksPerMinute, 1),
            };
        }).ToList();
        return Task.FromResult(ToolResult.Json(new { items = result }));
    }

    private bool IsHidden(SessionInfo session, ScopedUser scope)
        => (session.FullNowPlayingItem?.Id ?? session.NowPlayingItem?.Id) is { } id
            && id != Guid.Empty
            && _lookup.FindVisible(scope, id) is null;
}
