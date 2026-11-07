namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// A rendered prompt: one user text message.
/// </summary>
/// <param name="Description">The description returned with the prompt.</param>
/// <param name="Text">The text of the user message.</param>
public sealed record PromptMessage(string Description, string Text);
