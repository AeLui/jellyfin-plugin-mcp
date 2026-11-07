using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Mcp;

namespace Jellyfin.Plugin.Mcp.Prompts;

/// <summary>
/// Lists what was added to the library recently.
/// </summary>
public sealed class WhatsNewPrompt : IMcpPrompt
{
    private static readonly string[] Types = ["movies", "shows", "music", "all"];

    private static readonly PromptArgument[] Args =
    [
        new("days", "Days", "How many days back to look (1 to 90, default 7)."),
        new("type", "Type", "movies, shows, music or all (default all)."),
    ];

    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="WhatsNewPrompt"/> class.
    /// </summary>
    /// <param name="clock">The clock used for the cut-off date.</param>
    public WhatsNewPrompt(IClock clock)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public string Name => "whats_new";

    /// <inheritdoc />
    public string Title => "What's new";

    /// <inheritdoc />
    public string Description => "See what was added to your library recently.";

    /// <inheritdoc />
    public string RequiredScope => McpScopes.Library;

    /// <inheritdoc />
    public IReadOnlyList<PromptArgument> Arguments => Args;

    /// <inheritdoc />
    public PromptMessage Render(PromptArguments args, McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(principal);
        var days = args.GetInt("days", 1, 90, 7);
        var type = args.GetEnum("type", Types, "all");
        var since = PromptText.Since(_clock, days);
        var types = type switch
        {
            "movies" => "[\"movie\"]",
            "shows" => "[\"episode\"]",
            "music" => "[\"audio\"]",
            _ => null,
        };

        var text = new StringBuilder("Tell me what was added to my Jellyfin library in the last ")
            .Append(PromptText.Days(days)).Append(", since ").Append(since).Append('.')
            .Append(" Call jellyfin_recently_added")
            .Append(types is null ? string.Empty : " with types " + types)
            .Append(" and limit 50; results are newest first, so keep paging with startIndex until an item's dateAdded is before ")
            .Append(since).Append('.')
            .Append(" Group episodes by series and tracks by album.")
            .Append(" Use jellyfin_watch_history to point out what matches what I usually watch.");

        var description = "What's new in the last " + PromptText.Days(days) + (type == "all" ? string.Empty : " (" + type + ")");
        return new PromptMessage(description, PromptText.Finish(text, principal, offerPlayback: true));
    }
}
