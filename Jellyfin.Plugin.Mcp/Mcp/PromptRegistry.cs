using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Registry of the MCP prompts exposed by the server.
/// </summary>
public sealed class PromptRegistry
{
    private readonly Dictionary<string, IMcpPrompt> _prompts = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptRegistry"/> class.
    /// </summary>
    /// <param name="prompts">The available prompts.</param>
    public PromptRegistry(IEnumerable<IMcpPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        foreach (var prompt in prompts)
        {
            if (!_prompts.TryAdd(prompt.Name, prompt))
            {
                throw new ArgumentException("Duplicate prompt name: " + prompt.Name, nameof(prompts));
            }
        }
    }

    /// <summary>
    /// Lists the prompts the principal may use, sorted by name.
    /// </summary>
    /// <param name="principal">The caller.</param>
    /// <returns>The visible prompts.</returns>
    public IReadOnlyList<IMcpPrompt> ListFor(McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return _prompts.Values
            .Where(p => principal.Scopes.Contains(p.RequiredScope))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Finds a prompt by name.
    /// </summary>
    /// <param name="name">The prompt name.</param>
    /// <param name="principal">The caller.</param>
    /// <returns>The prompt, or <c>null</c> when it is unknown or outside the caller's scopes.</returns>
    public IMcpPrompt? Find(string name, McpPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(principal);
        return _prompts.TryGetValue(name, out var prompt) && principal.Scopes.Contains(prompt.RequiredScope) ? prompt : null;
    }
}
