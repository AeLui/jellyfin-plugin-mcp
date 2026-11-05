using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Mcp.Configuration;

namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// Validates the <c>Origin</c> header of MCP requests (DNS rebinding protection required by the MCP transport).
/// </summary>
/// <remarks>
/// Requests without an <c>Origin</c> (Claude Code, Claude Desktop, scripts) are allowed. A browser origin is allowed when
/// its host is the host the request was sent to (scheme and port are ignored, so TLS-terminating proxies work) or when it
/// is listed in the plugin's allowed origins.
/// </remarks>
public sealed class OriginPolicy
{
    private readonly IPluginSettings _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="OriginPolicy"/> class.
    /// </summary>
    /// <param name="settings">The plugin settings holding the allowed origins.</param>
    public OriginPolicy(IPluginSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Decides whether a request with the given <c>Origin</c> may proceed.
    /// </summary>
    /// <param name="origin">The <c>Origin</c> header value, or <c>null</c> when absent.</param>
    /// <param name="requestHost">The host name the request was sent to, without port.</param>
    /// <returns><c>true</c> when the request may proceed.</returns>
    public bool IsAllowed(string? origin, string requestHost)
    {
        if (origin is null)
        {
            return true;
        }

        var normalized = Normalize(origin);
        if (normalized is null)
        {
            return false;
        }

        var host = new Uri(normalized).Host;
        if (string.Equals(TrimBrackets(host), TrimBrackets(requestHost), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (_settings.AllowedOrigins ?? Array.Empty<string>())
            .Select(Normalize)
            .Any(allowed => string.Equals(allowed, normalized, StringComparison.Ordinal));
    }

    /// <summary>
    /// Canonicalizes an origin to <c>scheme://host[:port]</c> in lower case, without a default port or trailing slash.
    /// </summary>
    /// <param name="origin">The origin.</param>
    /// <returns>The canonical origin, or <c>null</c> when it is not an absolute http or https origin.</returns>
    internal static string? Normalize(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.PathAndQuery != "/"
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        return (uri.Scheme + "://" + uri.Host + port).ToLowerInvariant();
    }

    private static string TrimBrackets(string host) => host.Trim('[', ']');
}
