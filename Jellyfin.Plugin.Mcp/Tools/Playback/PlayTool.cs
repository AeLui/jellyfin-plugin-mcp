using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Starts or queues items on a session.
/// </summary>
public sealed class PlayTool : PlaybackToolBase
{
    private const int MaxItems = 100;

    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"sessionId":{"type":"string","description":"Session id from jellyfin_list_sessions."},"itemIds":{"type":"array","items":{"type":"string"},"minItems":1,"description":"Item ids (GUIDs) to play."},"mode":{"type":"string","enum":["now","next","last"],"description":"Play now (default), play next or add to the end of the queue."},"startPositionMinutes":{"type":"integer","minimum":0,"description":"Where to start, in minutes."},"audioStreamIndex":{"type":"integer","minimum":0,"description":"Audio track index from jellyfin_get_media_tracks; only with one item and mode now."},"subtitleStreamIndex":{"type":"integer","minimum":-1,"description":"Subtitle track index from jellyfin_get_media_tracks, or -1 for no subtitles; only with one item and mode now."}},"required":["sessionId","itemIds"]}""").RootElement.Clone();

    private static readonly JsonElement ResultSchema = OutputSchemas.Object(
        "\"sessionId\":{\"type\":\"string\"},\"mode\":{\"type\":\"string\",\"enum\":[\"now\",\"next\",\"last\"]},\"itemCount\":{\"type\":\"integer\"},"
            + "\"audioStreamIndex\":{\"type\":\"integer\"},\"subtitleStreamIndex\":{\"type\":\"integer\"}",
        "sessionId",
        "mode",
        "itemCount");

    private readonly ItemLookup _lookup;
    private readonly TrackValidator _tracks;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="sessions">The session resolver.</param>
    /// <param name="sessionManager">The session manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="tracks">The audio and subtitle track validator.</param>
    public PlayTool(IUserManager userManager, IPluginSettings settings, SessionResolver sessions, ISessionManager sessionManager, ItemLookup lookup, TrackValidator tracks)
        : base(userManager, settings, sessions, sessionManager)
    {
        _lookup = lookup;
        _tracks = tracks;
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
    public override string Title => "Play on device";

    /// <inheritdoc />
    public override string Description => "Play items on one of your sessions (now, next or at the end of the queue), optionally with chosen audio and subtitle tracks.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => ResultSchema;

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

        var audioIndex = args.GetInt("audioStreamIndex");
        var subtitleIndex = args.GetInt("subtitleStreamIndex");
        CheckTrackArguments(audioIndex, subtitleIndex, ids.Count, mode);
        var session = Sessions.Resolve(principal, sessionId);
        var scope = GetScopedUser(principal);
        var items = ids.Select(id => _lookup.GetVisible(scope, id)).ToList();
        if (audioIndex is not null)
        {
            _tracks.Validate(items[0], MediaStreamType.Audio, audioIndex.Value, "audioStreamIndex");
        }

        if (subtitleIndex is not null)
        {
            _tracks.Validate(items[0], MediaStreamType.Subtitle, subtitleIndex.Value, "subtitleStreamIndex");
        }

        // Clients expand a playlist or collection themselves, entries from every library included; a library-restricted
        // token sends the visible entries instead.
        var playIds = scope.IsRestricted
            ? items.SelectMany(i => i is Playlist or BoxSet ? _lookup.VisibleChildren(scope, (Folder)i) : [i]).Select(i => i.Id).ToArray()
            : ids.ToArray();
        if (playIds.Length == 0)
        {
            throw new ToolException("Nothing to play");
        }

        var request = new PlayRequest
        {
            ItemIds = playIds,
            PlayCommand = mode switch
            {
                PlayMode.Next => PlayCommand.PlayNext,
                PlayMode.Last => PlayCommand.PlayLast,
                _ => PlayCommand.PlayNow,
            },
            StartPositionTicks = startMinutes is null ? null : startMinutes.Value * TimeSpan.TicksPerMinute,
            AudioStreamIndex = audioIndex,
            SubtitleStreamIndex = subtitleIndex,

            // The indexes were checked against the item's own media source; pin it so the client does not pick another version.
            MediaSourceId = audioIndex is null && subtitleIndex is null ? null : items[0].Id.ToString("N", CultureInfo.InvariantCulture),
        };
        await SendAsync(() => SessionManager.SendPlayCommand(ControllingSessionId, session.Id, request, ct), isPlay: true).ConfigureAwait(false);
        return ToolResult.Json(new
        {
            sessionId = session.Id,
            mode = mode.ToString().ToLowerInvariant(),
            itemCount = playIds.Length,
            audioStreamIndex = audioIndex,
            subtitleStreamIndex = subtitleIndex,
        });
    }

    // Jellyfin applies the stream indexes to the first item it starts, so they only make sense for one item played now.
    private static void CheckTrackArguments(int? audioIndex, int? subtitleIndex, int itemCount, PlayMode mode)
    {
        foreach (var (name, value, minimum) in new[] { ("audioStreamIndex", audioIndex, 0), ("subtitleStreamIndex", subtitleIndex, TrackValidator.SubtitlesOff) })
        {
            if (value is null)
            {
                continue;
            }

            if (itemCount != 1 || mode != PlayMode.Now)
            {
                throw new ToolException("Invalid argument '" + name + "': only allowed when playing one item now");
            }

            if (value < minimum)
            {
                throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"Invalid argument '{name}': must be at least {minimum}"));
            }
        }
    }
}
