using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.Mcp.Mcp;

namespace Jellyfin.Plugin.Mcp.Prompts;

/// <summary>
/// Picks an unwatched movie for tonight.
/// </summary>
public sealed class MovieNightPrompt : IMcpPrompt
{
    private static readonly PromptArgument[] Args =
    [
        new("genre", "Genre", "Only movies of this genre, for example Drama."),
        new("maxMinutes", "Maximum minutes", "Longest runtime in minutes (30 to 600)."),
        new("mood", "Mood", "What you feel like, for example \"something light\"."),
    ];

    /// <inheritdoc />
    public string Name => "movie_night";

    /// <inheritdoc />
    public string Title => "Movie night";

    /// <inheritdoc />
    public string Description => "Pick a movie you have not watched yet for tonight, from your own library.";

    /// <inheritdoc />
    public string RequiredScope => McpScopes.Library;

    /// <inheritdoc />
    public IReadOnlyList<PromptArgument> Arguments => Args;

    /// <inheritdoc />
    public PromptMessage Render(PromptArguments args, McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(principal);
        var genre = args.GetText("genre", 100);
        int? maxMinutes = args.Has("maxMinutes") ? args.GetInt("maxMinutes", 30, 600, 0) : null;
        var mood = args.GetText("mood", 100);

        var parts = new List<string>();
        var text = new StringBuilder("Pick a movie for me to watch tonight that I have not seen yet.");
        if (genre is not null)
        {
            parts.Add(genre);
            text.Append(" Genre: \"").Append(genre).Append("\".");
        }

        if (maxMinutes is not null)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"under {maxMinutes} minutes"));
            text.Append(CultureInfo.InvariantCulture, $" It must be at most {maxMinutes} minutes long (runtimeMinutes).");
        }

        if (mood is not null)
        {
            parts.Add("mood \"" + mood + "\"");
            text.Append(" Mood: \"").Append(mood).Append("\".");
        }

        text.Append(" Find candidates with jellyfin_movie_recommendations and drop items whose played is true.")
            .Append(" Widen the pool with jellyfin_search using types [\"movie\"]")
            .Append(genre is null ? string.Empty : " and genres set to that genre")
            .Append(", and with jellyfin_browse using filter \"unplayed\" on the movie libraries from jellyfin_list_libraries.")
            .Append(" Use jellyfin_get_item to check the overview and genres")
            .Append(mood is null ? "." : " against the mood.")
            .Append(" Propose three options, each with one line on why it fits; when unsure, prefer a higher communityRating.");

        var description = parts.Count == 0 ? "Movie night" : "Movie night: " + string.Join(", ", parts);
        return new PromptMessage(description, PromptText.Finish(text, principal, offerPlayback: true));
    }
}
