using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.Mcp.Mcp;

namespace Jellyfin.Plugin.Mcp.Prompts;

/// <summary>
/// Summarises what the user is watching: items in progress and the next episodes.
/// </summary>
public sealed class CatchMeUpPrompt : IMcpPrompt
{
    /// <inheritdoc />
    public string Name => "catch_me_up";

    /// <inheritdoc />
    public string Title => "Catch me up";

    /// <inheritdoc />
    public string Description => "Summarise what you have in progress and the next episode of each series you follow.";

    /// <inheritdoc />
    public string RequiredScope => McpScopes.Library;

    /// <inheritdoc />
    public IReadOnlyList<PromptArgument> Arguments => Array.Empty<PromptArgument>();

    /// <inheritdoc />
    public PromptMessage Render(PromptArguments args, McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var text = new StringBuilder("Catch me up on what I am watching.")
            .Append(" Call jellyfin_continue_watching and jellyfin_next_up.")
            .Append(" Summarise what I have in progress, with the minutes left (runtimeMinutes minus playbackPositionMinutes),")
            .Append(" and the next episode of each series I follow, most recent first. Keep it short.");
        return new PromptMessage("Catch up on what you are watching", PromptText.Finish(text, principal, offerPlayback: true));
    }
}
