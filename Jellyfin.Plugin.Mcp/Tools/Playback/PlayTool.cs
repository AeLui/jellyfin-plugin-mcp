using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Starts or queues items on a session.
/// </summary>
public sealed class PlayTool : PlaybackToolBase
{
    private const int MaxItems = 100;

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"sessionId":{"type":"string","description":"Session id from jellyfin_list_sessions."},"itemIds":{"type":"array","items":{"type":"string"},"minItems":1,"description":"Item ids (GUIDs) to play."},"mode":{"type":"string","enum":["now","next","last"],"description":"Play now (default), play next or add to the end of the queue."},"startPositionMinutes":{"type":"integer","minimum":0,"description":"Where to start, in minutes."}},"required":["sessionId","itemIds"]}""").RootElement.Clone();

    private readonly ItemLookup _lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessions">The session resolver.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    public PlayTool(IUserManager userManager, IPluginSettings settings, SessionResolver sessions, ISessionManager sessionManager, ItemLookup lookup)
        : base(userManager, settings, sessions, sessionManager)
    {
        _lookup = lookup;
    }

    private enum PlayMode
    {
        Now,
        Next,
        Last,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_play";

    /// <inheritdoc />
    public override string Description => "Play items on one of your sessions (now, next or at the end of the queue).";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, false, false);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var sessionId = args.GetRequiredString("sessionId");
        var ids = args.GetGuidArray("itemIds");
        if (ids.Count == 0)
        {
            throw new ToolException("Invalid argument 'itemIds': at least one id is required");
        }

        if (ids.Count > MaxItems)
        {
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"Invalid argument 'itemIds': at most {MaxItems} ids"));
        }

        var mode = args.GetEnum<PlayMode>("mode") ?? PlayMode.Now;
        var startMinutes = args.GetInt("startPositionMinutes");
        if (startMinutes is < 0)
        {
            throw new ToolException("Invalid argument 'startPositionMinutes': must not be negative");
        }

        var session = Sessions.Resolve(principal, sessionId);
        var user = GetUser(principal);
        foreach (var id in ids)
        {
            _lookup.GetVisible(user, id);
        }

        var request = new PlayRequest
        {
            ItemIds = ids.ToArray(),
            PlayCommand = mode switch
            {
                PlayMode.Next => PlayCommand.PlayNext,
                PlayMode.Last => PlayCommand.PlayLast,
                _ => PlayCommand.PlayNow,
            },
            StartPositionTicks = startMinutes is null ? null : startMinutes.Value * TimeSpan.TicksPerMinute,
        };
        await SendAsync(() => SessionManager.SendPlayCommand(ControllingSessionId, session.Id, request, ct), isPlay: true).ConfigureAwait(false);
        return ToolResult.Json(new { sessionId = session.Id, mode = mode.ToString().ToLowerInvariant(), itemCount = ids.Count });
    }
}
