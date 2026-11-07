using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Pauses, resumes, stops, seeks or skips on a session.
/// </summary>
public sealed class PlaybackControlTool : PlaybackToolBase
{
    private readonly ItemLookup _lookup;
    private readonly TrackValidator _tracks;

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"sessionId":{"type":"string","description":"Session id from jellyfin_list_sessions."},"command":{"type":"string","enum":["pause","unpause","stop","seek","next","previous","set_audio_track","set_subtitle_track"]},"positionMinutes":{"type":"number","minimum":0,"description":"Target position in minutes; required for seek."},"streamIndex":{"type":"integer","minimum":-1,"description":"Track index from jellyfin_get_media_tracks; required for set_audio_track and set_subtitle_track (-1 turns subtitles off)."}},"required":["sessionId","command"]}""").RootElement.Clone();

    private static readonly JsonElement ResultSchema = OutputSchemas.Object(
        "\"sessionId\":{\"type\":\"string\"},\"command\":{\"type\":\"string\"},\"streamIndex\":{\"type\":\"integer\"}",
        "sessionId",
        "command");

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackControlTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessions">The session resolver.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="tracks">The audio and subtitle track validator.</param>
    public PlaybackControlTool(IUserManager userManager, IPluginSettings settings, SessionResolver sessions, ISessionManager sessionManager, ItemLookup lookup, TrackValidator tracks)
        : base(userManager, settings, sessions, sessionManager)
    {
        _lookup = lookup;
        _tracks = tracks;
    }

    private enum Control
    {
        Pause,
        Unpause,
        Stop,
        Seek,
        Next,
        Previous,
        SetAudioTrack,
        SetSubtitleTrack,
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_playback_control";

    /// <inheritdoc />
    public override string Title => "Control playback";

    /// <inheritdoc />
    public override string Description => "Pause, unpause, stop, seek, skip to the next or previous item, or switch the audio or subtitle track on one of your sessions.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => ResultSchema;

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

        var isTrack = control is Control.SetAudioTrack or Control.SetSubtitleTrack;
        var streamIndex = args.GetInt("streamIndex");
        if (!isTrack && streamIndex is not null)
        {
            throw new ToolException("Invalid argument 'streamIndex': only allowed for set_audio_track and set_subtitle_track");
        }

        var session = Sessions.Resolve(principal, sessionId);
        if (isTrack)
        {
            var index = await SetTrackAsync(principal, session, control == Control.SetAudioTrack, streamIndex, ct).ConfigureAwait(false);
            return ToolResult.Json(new { sessionId = session.Id, command = control == Control.SetAudioTrack ? "set_audio_track" : "set_subtitle_track", streamIndex = index });
        }

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
        await SendAsync(() => SessionManager.SendPlaystateCommand(ControllingSessionId, session.Id, request, ct)).ConfigureAwait(false);
        return ToolResult.Json(new { sessionId = session.Id, command = control.ToString().ToLowerInvariant() });
    }

    private async Task<int> SetTrackAsync(McpPrincipal principal, SessionInfo session, bool audio, int? streamIndex, CancellationToken ct)
    {
        var minimum = audio ? 0 : TrackValidator.SubtitlesOff;
        var index = streamIndex ?? throw new ToolException("Invalid argument 'streamIndex': is required for set_audio_track and set_subtitle_track");
        if (index < minimum)
        {
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"Invalid argument 'streamIndex': must be at least {minimum}"));
        }

        var playingId = session.FullNowPlayingItem?.Id ?? session.NowPlayingItem?.Id;
        if (playingId is null || playingId.Value == Guid.Empty)
        {
            throw new ToolException("Nothing is playing on this session");
        }

        var scope = GetScopedUser(principal);
        var item = _lookup.GetVisible(scope, playingId.Value);

        // Clients report the displayed item and, separately, the version they actually play; the command acts on that version.
        if (Guid.TryParse(session.PlayState?.MediaSourceId, out var versionId) && versionId != item.Id)
        {
            item = _lookup.GetVisible(scope, versionId);
        }

        _tracks.Validate(item, audio ? MediaStreamType.Audio : MediaStreamType.Subtitle, index, "streamIndex");
        var command = new GeneralCommand { Name = audio ? GeneralCommandType.SetAudioStreamIndex : GeneralCommandType.SetSubtitleStreamIndex };
        command.Arguments["Index"] = index.ToString(CultureInfo.InvariantCulture);
        await SendAsync(() => SessionManager.SendGeneralCommand(ControllingSessionId, session.Id, command, ct)).ConfigureAwait(false);
        return index;
    }
}
