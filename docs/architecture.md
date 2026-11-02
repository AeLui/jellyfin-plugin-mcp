# Architecture

## Goals

Expose Jellyfin to MCP clients with no runtime dependency besides Jellyfin, with per-user scoped tokens, and with behaviour that stays simple enough to audit. The transport and protocol layer is hand-written (no MCP SDK) and stateless.

## Layers

```
HTTP  POST /mcp
 |
 v
Api/        McpController      reads body (1 MiB cap), maps auth status to HTTP codes
 |          TokensController   admin-only token CRUD at /McpServer/Tokens
 v
Auth/       TokenAuthenticator bearer parsing, hash lookup, expiry, user checks, scope trimming
 |          TokenStore         tokens in plugin config, one lock
 |          AuthFailureLimiter per-IP failure window
 v
Mcp/        JsonRpcDispatcher  JSON-RPC 2.0: initialize, ping, tools/list, tools/call, batches
 |          ToolRegistry       tools filtered by the principal's scopes
 |          ToolArguments      typed, validated argument access
 v
Tools/      JellyfinToolBase -> Library / Playback / Manage / Admin tools
            (the only layer that touches Jellyfin services)
Configuration/  PluginConfiguration + the dashboard page (configPage.html/js)
```

Rule: `Mcp/` must not reference any `Jellyfin.*` or `MediaBrowser.*` namespace. It is a pure protocol layer that is tested without Jellyfin. Tools implement `IMcpTool` and are discovered and registered automatically by `PluginServiceRegistrator`.

## Request flow

1. Client sends `POST /mcp` with `Authorization: Bearer jmcp_...` and a JSON-RPC body.
2. `McpController` authenticates. Disabled plugin or not ready: 503. Rate limited: 429. Bad token: 401 with `WWW-Authenticate: Bearer`.
3. `TokenAuthenticator` hashes the token, finds it, checks enabled and expiry, loads the Jellyfin user (must exist and not be disabled), intersects the token's scopes with the known scopes, drops `admin` if the user is not an administrator, and records last use. The result is an `McpPrincipal` (user id and scopes), recreated on every request.
4. `JsonRpcDispatcher` parses the body (a single message or a batch). Notifications get no response (HTTP 202 when nothing is returned). Requests are routed to `initialize`, `ping`, `tools/list` or `tools/call`.
5. `tools/call` looks the tool up in the registry filtered by the principal's scopes, wraps the arguments in `ToolArguments`, and runs the tool. `ToolException` becomes an `isError` result with its message; any other exception becomes a generic `isError` result and is logged.
6. The tool result is returned as one text content block of JSON.

## Protocol support

- Streamable HTTP transport, JSON responses only (no SSE stream, no server-initiated messages). `GET` and `DELETE` answer 405.
- Stateless: no sessions, no `Mcp-Session-Id`. Each request is self-contained.
- Capabilities: `tools` with `listChanged: false`.
- Revisions negotiated at `initialize`: 2025-11-25, 2025-06-18, 2025-03-26. An unknown requested version gets the latest supported one. The stateless 2026-07-28 revision is not supported yet (see the [roadmap](roadmap.md)).
- Tools carry `readOnlyHint`, `destructiveHint` and `idempotentHint`.

## Key decisions

- **Hand-written transport.** The official SDKs would add runtime dependencies to a plugin loaded into Jellyfin's process, with version-conflict risk. The needed protocol subset is small.
- **Stateless.** Fits Jellyfin's plugin model and horizontal simplicity: nothing to expire, nothing to leak between users, no sticky routing.
- **Static bearer tokens (v1).** Works with Claude Code (supported); Claude Desktop via `mcp-remote` is expected to work but is not yet verified. OAuth 2.1 for claude.ai connectors is the v2 goal.
- **Principal per request.** No cached identity, so revocation and demotion are immediate.
- **User-aware lookups.** Every id goes through `ItemLookup.GetVisible`, which uses the user overload of `GetItemById`. See [security](security.md).
- **Tokens in plugin config, hashed.** `Plugin.UpdateConfiguration` is overridden so the generic config API cannot change tokens; `TokenStore` writes them through a dedicated method under one lock, so a token created while `LastUsedAt` is being saved is never lost.
- **Scopes decide tools.** A tool declares one required scope; the registry hides tools the token cannot use.

## Plugin facts

- Name `MCP Server`, GUID `08dd6fbf-c026-48c9-9405-824c3906df09`.
- Compiled against `Jellyfin.Controller`/`Jellyfin.Model` 12.4.0; `targetAbi` 12.4.0.0; `net10.0`.
- Analyzers (StyleCop, Serilog, multithreading) run with warnings as errors on the plugin project.
