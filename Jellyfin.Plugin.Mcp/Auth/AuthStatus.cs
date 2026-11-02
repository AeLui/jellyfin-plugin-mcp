namespace Jellyfin.Plugin.Mcp.Auth;

/// <summary>
/// The outcome category of an authentication attempt.
/// </summary>
public enum AuthStatus
{
    /// <summary>The caller is authenticated.</summary>
    Success,

    /// <summary>The credentials are missing or invalid.</summary>
    Unauthorized,

    /// <summary>Too many failed attempts from the caller's address.</summary>
    TooManyRequests,

    /// <summary>The endpoint is disabled.</summary>
    Disabled
}
