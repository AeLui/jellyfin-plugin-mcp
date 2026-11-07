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
Mcp/        JsonRpcDispatcher  JSON-RPC 2.0: initialize, ping, server/discover, tools/list, tools/call, prompts/list, prompts/get, batches
 |          ToolRegistry       tools filtered by the principal's scopes
 |          PromptRegistry     prompts filtered by the principal's scopes
 |          ToolArguments      typed, validated argument access
 v
Tools/      JellyfinToolBase -> Library / Playback / Manage / Admin tools
            (the only layer that touches Jellyfin services)
Prompts/    static prompt templates (IMcpPrompt) that name tools; no Jellyfin access
Configuration/  PluginConfiguration + the dashboard page (configPage.html/js)
```

Rule: `Mcp/` must not reference any `Jellyfin.*` or `MediaBrowser.*` namespace. It is a pure protocol layer that is tested without Jellyfin. Tools implement `IMcpTool` and prompts implement `IMcpPrompt`; both are discovered and registered automatically by `PluginServiceRegistrator`.

## Request flow

1. Client sends `POST /mcp` with `Authorization: Bearer jmcp_...` and a JSON-RPC body.
2. `McpController` checks the `Origin` header (`OriginPolicy`; a foreign browser origin gets 403 before authentication), then authenticates. Disabled plugin or not ready: 503. Rate limited: 429. Bad token: 401 with `WWW-Authenticate: Bearer`.
3. `TokenAuthenticator` hashes the token, finds it, checks enabled and expiry, loads the Jellyfin user (must exist and not be disabled), intersects the token's scopes with the known scopes, drops `admin` if the user is not an administrator or the token is limited to some libraries, and records last use. The result is an `McpPrincipal` (user id, scopes and allowed library ids), recreated on every request.
4. `JsonRpcDispatcher` picks the protocol era from the `MCP-Protocol-Version` header (see Protocol support), then parses the body. Notifications get no response (HTTP 202 when nothing is returned). Handshake-era requests are routed to `initialize`, `ping`, `tools/list`, `tools/call`, `prompts/list` or `prompts/get`; stateless requests to `server/discover`, `tools/list`, `tools/call`, `prompts/list` or `prompts/get`.
5. `tools/call` looks the tool up in the registry filtered by the principal's scopes, wraps the arguments in `ToolArguments`, and runs the tool. `ToolException` becomes an `isError` result with its message; any other exception becomes a generic `isError` result and is logged.
6. The tool result is returned as `structuredContent` and, with the same JSON, as one text content block. Error results carry only the text.

## Protocol support

- Streamable HTTP transport, JSON responses only (no SSE stream, no server-initiated messages). `GET` and `DELETE` answer 405.
- Stateless: no sessions, no `Mcp-Session-Id`. Each request is self-contained.
- Capabilities: `tools` and `prompts` (no `listChanged`, so no `subscriptions/listen`). No resources.
- Two protocol eras, chosen per request by the `MCP-Protocol-Version` header:
  - **Handshake** (2025-11-25, 2025-06-18, 2025-03-26, or no header): versions are negotiated at `initialize`; an unknown requested version gets the latest of these. Batches are accepted. Behaviour is unchanged from earlier releases apart from the additive fields below.
  - **Stateless** (2026-07-28): no `initialize`. Every request must carry `_meta["io.modelcontextprotocol/protocolVersion"]` and `_meta["io.modelcontextprotocol/clientCapabilities"]` (missing: 400, -32602), and `Mcp-Method` (plus `Mcp-Name` for `tools/call` and `prompts/get`) headers that match the body, base64 `=?base64?...?=` values decoded (mismatch: 400, -32020). `server/discover` returns the supported versions, capabilities and instructions. Results carry `resultType: "complete"` and `_meta["io.modelcontextprotocol/serverInfo"]`; `server/discover`, `tools/list` and `prompts/list` add `ttlMs: 300000` and `cacheScope: "private"`. Unknown methods (including `initialize` and `ping`): 404, -32601. Batches: 400, -32600. `StatelessRequestValidator` holds the checks; the stateless method table is `JsonRpcDispatcher.Stateless.cs`.
  - Any other header value: 400, -32022, with `data.supported` listing every version.
- Tools carry `title`, `outputSchema` and the `readOnlyHint`, `destructiveHint` and `idempotentHint` annotations (plus `annotations.title`).

## Key decisions

- **Hand-written transport.** The official SDKs would add runtime dependencies to a plugin loaded into Jellyfin's process, with version-conflict risk. The needed protocol subset is small.
- **Stateless.** Fits Jellyfin's plugin model and horizontal simplicity: nothing to expire, nothing to leak between users, no sticky routing.
- **Static bearer tokens (v1).** Works with Claude Code (supported); Claude Desktop via `mcp-remote` is expected to work but is not yet verified. OAuth 2.1 for claude.ai connectors was built and discarded; static tokens stay.
- **Principal per request.** No cached identity, so revocation and demotion are immediate.
- **User-aware lookups.** Tools resolve the caller with `GetScopedUser(principal)`, a `ScopedUser` (the Jellyfin user plus the token's allowed libraries). Every id goes through `ItemLookup.GetVisible(scope, id)`, which uses the user overload of `GetItemById` and then `LibraryRestriction`, and every list query through `VisibleItems.GetVisibleItemsResult(query, scope)`. Neither accepts a bare `User`, so a tool cannot skip the library restriction. See [security](security.md).
- **Tokens in plugin config, hashed.** `Plugin.UpdateConfiguration` is overridden so the generic config API cannot change tokens; `TokenStore` writes them through a dedicated method under one lock, so a token created while `LastUsedAt` is being saved is never lost.
- **Scopes decide tools.** A tool declares one required scope; the registry hides tools the token cannot use.

## Plugin facts

- Name `MCP Server`, GUID `08dd6fbf-c026-48c9-9405-824c3906df09`.
- Compiled against `Jellyfin.Controller`/`Jellyfin.Model` 12.4.0; `targetAbi` 12.4.0.0; `net10.0`.
- Analyzers (StyleCop, Serilog, multithreading) run with warnings as errors on the plugin project.
