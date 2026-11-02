using System;

namespace Jellyfin.Plugin.Mcp.Mcp;

/// <summary>
/// Thrown by a tool for a user-facing failure; its message is returned to the client.
/// </summary>
public sealed class ToolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolException"/> class.
    /// </summary>
    public ToolException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolException"/> class.
    /// </summary>
    /// <param name="message">The message shown to the client.</param>
    public ToolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolException"/> class.
    /// </summary>
    /// <param name="message">The message shown to the client.</param>
    /// <param name="innerException">The underlying exception.</param>
    public ToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
