using System.Collections.Generic;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// An MCP prompt: a template the client offers to the user, for example as a slash command.
/// </summary>
public interface IMcpPrompt
{
    /// <summary>
    /// Gets the unique prompt name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the short human-readable name shown by clients.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets the description shown to the user.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the scope a token needs to see and get this prompt.
    /// </summary>
    string RequiredScope { get; }

    /// <summary>
    /// Gets the arguments the prompt accepts.
    /// </summary>
    IReadOnlyList<PromptArgument> Arguments { get; }

    /// <summary>
    /// Renders the prompt.
    /// </summary>
    /// <param name="args">The prompt arguments.</param>
    /// <param name="principal">The authenticated caller.</param>
    /// <returns>The rendered message.</returns>
    /// <exception cref="PromptException">An argument is invalid.</exception>
    PromptMessage Render(PromptArguments args, McpPrincipal principal);
}
