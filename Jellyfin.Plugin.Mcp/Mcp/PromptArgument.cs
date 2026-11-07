namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// One argument of an MCP prompt.
/// </summary>
/// <param name="Name">The argument name.</param>
/// <param name="Title">The short human-readable name.</param>
/// <param name="Description">What the argument does.</param>
/// <param name="Required">Whether the argument must be supplied.</param>
public sealed record PromptArgument(string Name, string Title, string Description, bool Required = false);
