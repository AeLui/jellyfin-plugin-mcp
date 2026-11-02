using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Auth;
using Jellyfin.Plugin.Mcp.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Mcp.Api;

/// <summary>
/// The stateless MCP endpoint (Streamable HTTP, POST only).
/// </summary>
[ApiController]
[Route("mcp")]
[AllowAnonymous]
public sealed class McpController : ControllerBase
{
    private const int MaxBodyBytes = 1_048_576;

    // The framework limit sits above the manual cap so that oversized bodies reach ReadBodyAsync and get a 413
    // from this controller instead of a BadHttpRequestException from Kestrel.
    private const int RequestLimitSlackBytes = 65_536;

    private readonly TokenAuthenticator _authenticator;
    private readonly JsonRpcDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpController"/> class.
    /// </summary>
    /// <param name="authenticator">The token authenticator.</param>
    /// <param name="dispatcher">The JSON-RPC dispatcher.</param>
    public McpController(TokenAuthenticator authenticator, JsonRpcDispatcher dispatcher)
    {
        _authenticator = authenticator;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Handles one JSON-RPC message or batch.
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The JSON-RPC response, or an HTTP error.</returns>
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes + RequestLimitSlackBytes)]
    [Consumes("application/json")]
    public async Task<IActionResult> Post(CancellationToken ct)
    {
        AuthResult auth;
        try
        {
            var header = Request.Headers.Authorization.FirstOrDefault();
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            auth = _authenticator.Authenticate(header, remoteIp);
        }
        catch (PluginNotReadyException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        switch (auth.Status)
        {
            case AuthStatus.Success:
                break;
            case AuthStatus.Disabled:
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            case AuthStatus.TooManyRequests:
                Response.Headers.RetryAfter = "60";
                return StatusCode(StatusCodes.Status429TooManyRequests);
            default:
                Response.Headers.WWWAuthenticate = "Bearer realm=\"jellyfin-mcp\"";
                return StatusCode(StatusCodes.Status401Unauthorized);
        }

        var body = await ReadBodyAsync(ct).ConfigureAwait(false);
        if (body is null)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var result = await _dispatcher.DispatchAsync(body, auth.Principal!, ct).ConfigureAwait(false);
        if (result.Body is null)
        {
            return StatusCode(result.StatusCode);
        }

        return new ContentResult
        {
            StatusCode = result.StatusCode,
            ContentType = "application/json",
            Content = result.Body,
        };
    }

    /// <summary>
    /// Rejects GET; this server does not offer a server-to-client stream.
    /// </summary>
    /// <returns>A 405 response.</returns>
    [HttpGet]
    public IActionResult Get() => MethodNotAllowed();

    /// <summary>
    /// Rejects DELETE; this server has no sessions to terminate.
    /// </summary>
    /// <returns>A 405 response.</returns>
    [HttpDelete]
    public IActionResult Delete() => MethodNotAllowed();

    private StatusCodeResult MethodNotAllowed()
    {
        Response.Headers.Allow = "POST";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    /// <summary>
    /// Reads the request body, enforcing the size limit independently of the server feature.
    /// </summary>
    private async Task<byte[]?> ReadBodyAsync(CancellationToken ct)
    {
        if (Request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }
}
