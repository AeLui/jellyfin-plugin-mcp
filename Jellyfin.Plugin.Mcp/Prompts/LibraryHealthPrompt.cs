using System;
using System.Collections.Generic;
using System.Text;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Mcp;

namespace Jellyfin.Plugin.Mcp.Prompts;

/// <summary>
/// Reports on server health for administrators: restart, storage, tasks, errors and transcodes.
/// </summary>
public sealed class LibraryHealthPrompt : IMcpPrompt
{
    private static readonly PromptArgument[] Args =
    [
        new("days", "Days", "How many days of the activity log to check (1 to 30, default 1)."),
    ];

    private readonly IClock _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryHealthPrompt"/> class.
    /// </summary>
    /// <param name="clock">The clock used for the cut-off date.</param>
    public LibraryHealthPrompt(IClock clock)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public string Name => "library_health";

    /// <inheritdoc />
    public string Title => "Library health";

    /// <inheritdoc />
    public string Description => "Check server health: pending restart, disk space, scheduled tasks, recent errors and transcodes.";

    /// <inheritdoc />
    public string RequiredScope => McpScopes.Admin;

    /// <inheritdoc />
    public IReadOnlyList<PromptArgument> Arguments => Args;

    /// <inheritdoc />
    public PromptMessage Render(PromptArguments args, McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(principal);
        var days = args.GetInt("days", 1, 30, 1);
        var since = PromptText.Since(_clock, days);

        var text = new StringBuilder("Check the health of my Jellyfin server for the last ")
            .Append(PromptText.Days(days)).Append(", since ").Append(since).Append('.')
            .Append(" Call jellyfin_server_status (pending restart, server folders or library drives with little free space),")
            .Append(" jellyfin_list_tasks (failed or long-running scheduled tasks),")
            .Append(" jellyfin_activity_log with since: \"").Append(since).Append("\" and severity \"error\", then again with severity \"warning\",")
            .Append(" and jellyfin_list_all_sessions (current transcodes and their reasons).")
            .Append(" Report prioritised warnings first and end with a one-line all-clear when nothing is wrong.")
            .Append(" Do not run tasks or library scans unless I ask.")
            .Append(" Do not repeat client IP addresses from the activity log unless I ask.");

        return new PromptMessage("Server health for the last " + PromptText.Days(days), PromptText.Finish(text, principal, offerPlayback: false));
    }
}
