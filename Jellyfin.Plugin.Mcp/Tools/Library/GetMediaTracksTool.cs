using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Mcp.Tools.Library;

/// <summary>
/// Lists the audio and subtitle tracks of a video item.
/// </summary>
public sealed class GetMediaTracksTool : LibraryToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"itemId":{"type":"string","description":"Id (GUID) of a movie, episode or other video."}},"required":["itemId"]}""").RootElement.Clone();

    private static readonly JsonElement ResultSchema = OutputSchemas.Object(
        "\"itemId\":{\"type\":\"string\"},"
            + "\"audioTracks\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
                "\"index\":{\"type\":\"integer\",\"description\":\"Track index for jellyfin_play and jellyfin_playback_control.\"},"
                    + "\"displayTitle\":{\"type\":\"string\"},\"language\":{\"type\":\"string\"},\"title\":{\"type\":\"string\"},"
                    + "\"codec\":{\"type\":\"string\"},\"channels\":{\"type\":\"integer\"},"
                    + "\"isDefault\":{\"type\":\"boolean\"},\"isForced\":{\"type\":\"boolean\"}",
                "index",
                "displayTitle",
                "isDefault",
                "isForced")) + ","
            + "\"subtitleTracks\":" + OutputSchemas.ArrayOf(OutputSchemas.ObjectJson(
                "\"index\":{\"type\":\"integer\",\"description\":\"Track index for jellyfin_play and jellyfin_playback_control.\"},"
                    + "\"displayTitle\":{\"type\":\"string\"},\"language\":{\"type\":\"string\"},\"title\":{\"type\":\"string\"},"
                    + "\"codec\":{\"type\":\"string\"},\"isDefault\":{\"type\":\"boolean\"},\"isForced\":{\"type\":\"boolean\"},"
                    + "\"isExternal\":{\"type\":\"boolean\"},\"isHearingImpaired\":{\"type\":\"boolean\"}",
                "index",
                "displayTitle",
                "isDefault",
                "isForced",
                "isExternal",
                "isHearingImpaired")),
        "itemId",
        "audioTracks",
        "subtitleTracks");

    private readonly ItemLookup _lookup;
    private readonly IMediaSourceManager _mediaSources;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetMediaTracksTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="userDataManager">The user data manager.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="mediaSources">The media source manager.</param>
    public GetMediaTracksTool(IUserManager userManager, IPluginSettings settings, IUserDataManager userDataManager, ItemLookup lookup, IMediaSourceManager mediaSources)
        : base(userManager, settings, userDataManager)
    {
        _lookup = lookup;
        _mediaSources = mediaSources;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_get_media_tracks";

    /// <inheritdoc />
    public override string Title => "Get audio and subtitle tracks";

    /// <inheritdoc />
    public override string Description => "List the audio and subtitle tracks of a movie or episode. Pass a track index to jellyfin_play or jellyfin_playback_control.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => ResultSchema;

    /// <summary>
    /// Tells whether an item has selectable audio and subtitle tracks (movies, episodes, music videos, trailers and other videos).
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns><c>true</c> for video items.</returns>
    internal static bool IsVideo(BaseItem item) => item is Video;

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var id = args.GetRequiredGuid("itemId");
        var item = _lookup.GetVisible(GetScopedUser(principal), id);
        if (!IsVideo(item))
        {
            throw new ToolException("Tracks are only available for video items");
        }

        var streams = _mediaSources.GetMediaStreams(item.Id);
        var audioTracks = streams.Where(s => s.Type == MediaStreamType.Audio).Select(s => new
        {
            index = s.Index,
            displayTitle = DisplayTitle(s),
            language = NonEmpty(s.Language),
            title = NonEmpty(s.Title),
            codec = NonEmpty(s.Codec),
            channels = s.Channels,
            isDefault = s.IsDefault,
            isForced = s.IsForced,
        }).ToList();
        var subtitleTracks = streams.Where(s => s.Type == MediaStreamType.Subtitle).Select(s => new
        {
            index = s.Index,
            displayTitle = DisplayTitle(s),
            language = NonEmpty(s.Language),
            title = NonEmpty(s.Title),
            codec = NonEmpty(s.Codec),
            isDefault = s.IsDefault,
            isForced = s.IsForced,
            isExternal = s.IsExternal,
            isHearingImpaired = s.IsHearingImpaired,
        }).ToList();
        return Task.FromResult(ToolResult.Json(new
        {
            itemId = item.Id.ToString("N", CultureInfo.InvariantCulture),
            audioTracks,
            subtitleTracks,
        }));
    }

    private static string DisplayTitle(MediaStream stream)
        => NonEmpty(stream.DisplayTitle) ?? "Track " + stream.Index.ToString(CultureInfo.InvariantCulture);

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
