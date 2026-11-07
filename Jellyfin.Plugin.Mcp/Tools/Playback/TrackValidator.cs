using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Mcp.Mcp;
using Jellyfin.Plugin.Mcp.Tools.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Mcp.Tools.Playback;

/// <summary>
/// Checks an audio or subtitle stream index against an item's tracks.
/// </summary>
public sealed class TrackValidator
{
    /// <summary>
    /// The subtitle index that turns subtitles off in Jellyfin clients.
    /// </summary>
    public const int SubtitlesOff = -1;

    private readonly IMediaSourceManager _mediaSources;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrackValidator"/> class.
    /// </summary>
    /// <param name="mediaSources">The media source manager.</param>
    public TrackValidator(IMediaSourceManager mediaSources)
    {
        _mediaSources = mediaSources;
    }

    /// <summary>
    /// Validates a stream index for an item the caller may already see.
    /// </summary>
    /// <param name="item">The item, already checked for visibility.</param>
    /// <param name="type">The track type, audio or subtitle.</param>
    /// <param name="index">The stream index.</param>
    /// <param name="argumentName">The argument name used in error messages.</param>
    /// <exception cref="ToolException">The index is not a track of that type on the item.</exception>
    public void Validate(BaseItem item, MediaStreamType type, int index, string argumentName)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (type == MediaStreamType.Subtitle && index == SubtitlesOff)
        {
            return;
        }

        if (!GetMediaTracksTool.IsVideo(item))
        {
            throw new ToolException("Invalid argument '" + argumentName + "': the item has no selectable tracks");
        }

        if (!_mediaSources.GetMediaStreams(item.Id).Any(s => s.Type == type && s.Index == index))
        {
            var kind = type == MediaStreamType.Audio ? "audio" : "subtitle";
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"Invalid argument '{argumentName}': no {kind} track with index {index}"));
        }
    }
}
