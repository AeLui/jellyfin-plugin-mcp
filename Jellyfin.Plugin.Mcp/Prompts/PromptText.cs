using System;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Mcp;

namespace Jellyfin.Plugin.Mcp.Prompts;

/// <summary>
/// Shared wording and helpers for the prompt texts.
/// </summary>
internal static class PromptText
{
    /// <summary>The closing sentence of every prompt.</summary>
    public const string ReplyInUserLanguage = "Reply in the user's language.";

    /// <summary>The sentence added when the token can also control playback.</summary>
    public const string PlaybackOffer = "If the user wants to watch one, offer to play it on a device: list devices with jellyfin_list_sessions and start it with jellyfin_play. Never start playback without the user's confirmation.";

    /// <summary>
    /// Appends the closing sentences and returns the text.
    /// </summary>
    /// <param name="text">The prompt text so far, ending with a full sentence.</param>
    /// <param name="principal">The caller.</param>
    /// <param name="offerPlayback">Whether to offer playback when the caller has the playback scope.</param>
    /// <returns>The finished text.</returns>
    public static string Finish(StringBuilder text, McpPrincipal principal, bool offerPlayback)
    {
        if (offerPlayback && principal.Scopes.Contains(McpScopes.Playback))
        {
            text.Append(' ').Append(PlaybackOffer);
        }

        return text.Append(' ').Append(ReplyInUserLanguage).ToString();
    }

    /// <summary>
    /// Formats the cut-off date <paramref name="days"/> days before now.
    /// </summary>
    /// <param name="clock">The clock.</param>
    /// <param name="days">The number of days.</param>
    /// <returns>An ISO 8601 UTC timestamp.</returns>
    public static string Since(IClock clock, int days)
        => clock.UtcNow.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a number of days, for example "1 day" or "7 days".
    /// </summary>
    /// <param name="days">The number of days.</param>
    /// <returns>The phrase.</returns>
    public static string Days(int days) => string.Create(CultureInfo.InvariantCulture, $"{days} {(days == 1 ? "day" : "days")}");
}
