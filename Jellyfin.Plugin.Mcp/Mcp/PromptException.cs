using System;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Thrown for invalid prompt arguments; its message becomes a JSON-RPC invalid-params error and never contains argument values.
/// </summary>
public sealed class PromptException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PromptException"/> class.
    /// </summary>
    public PromptException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptException"/> class.
    /// </summary>
    /// <param name="message">The message shown to the client.</param>
    public PromptException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PromptException"/> class.
    /// </summary>
    /// <param name="message">The message shown to the client.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PromptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
