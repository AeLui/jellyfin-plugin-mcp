using System;
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
/// Pauses, resumes, stops, seeks or skips on a session.
/// </summary>
public sealed class PlaybackControlTool : PlaybackToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"sessionId":{"type":"string","description":"Session id from jellyfin_list_sessions."},"command":{"type":"string","enum":["pause","unpause","stop","seek","next","previous"]},"positionMinutes":{"type":"number","minimum":0,"description":"Target position in minutes; required for seek."}},"required":["sessionId","command"]}""").RootElement.Clone();

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackControlTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessions">The session resolver.</param>
    /// <param name="sessionManager">The session manager.</param>
    public PlaybackControlTool(IUserManager userManager, IPluginSettings settings, SessionResolver sessions, ISessionManager sessionManager)
        : base(userManager, settings, sessions, sessionManager)
    {
    }

    private enum Control
    {
        Pause,
        Unpause,
        Stop,
        Seek,
        Next,
        Previous,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_playback_control";

    /// <inheritdoc />
    public override string Description => "Pause, unpause, stop, seek, or skip to the next or previous item on one of your sessions.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, false, true);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var sessionId = args.GetRequiredString("sessionId");
        var control = args.GetEnum<Control>("command") ?? throw new ToolException("Invalid argument 'command': is required");
        long? ticks = null;
        if (control == Control.Seek)
        {
            var minutes = args.GetDouble("positionMinutes")
                ?? throw new ToolException("Invalid argument 'positionMinutes': is required for seek");
            if (minutes < 0 || minutes > TimeSpan.MaxValue.TotalMinutes)
            {
                throw new ToolException("Invalid argument 'positionMinutes': must be a non-negative number of minutes");
            }

            ticks = (long)(minutes * TimeSpan.TicksPerMinute);
        }

        var session = Sessions.Resolve(principal, sessionId);
        var request = new PlaystateRequest
        {
            Command = control switch
            {
                Control.Pause => PlaystateCommand.Pause,
                Control.Unpause => PlaystateCommand.Unpause,
                Control.Stop => PlaystateCommand.Stop,
                Control.Seek => PlaystateCommand.Seek,
                Control.Next => PlaystateCommand.NextTrack,
                _ => PlaystateCommand.PreviousTrack,
            },
            SeekPositionTicks = ticks,
        };
        await SessionManager.SendPlaystateCommand(ControllingSessionId, session.Id, request, ct).ConfigureAwait(false);
        return ToolResult.Json(new { sessionId = session.Id, command = control.ToString().ToLowerInvariant() });
    }
}
