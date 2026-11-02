using System;

namespace Jellyfin.Plugin.Mcp.Api;

/// <summary>
/// Thrown when the plugin configuration is requested before the plugin instance exists.
/// </summary>
public sealed class PluginNotReadyException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginNotReadyException"/> class.
    /// </summary>
    public PluginNotReadyException()
        : base("The MCP plugin is not initialized.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginNotReadyException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public PluginNotReadyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginNotReadyException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PluginNotReadyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
