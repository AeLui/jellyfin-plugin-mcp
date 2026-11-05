# Security

## Model in one paragraph

The plugin lets a token holder act on Jellyfin as one specific user, restricted to the scopes granted to the token. It never grants more than that user could do in Jellyfin itself, and the `admin` scope additionally requires the user to be an administrator. Anyone who holds a token holds that access, so treat tokens like passwords.

## Tokens

- Format: `jmcp_` followed by 32 random bytes in base64url (no padding).
- Storage: only the lowercase SHA-256 hash and an 8-character display prefix are stored in the plugin configuration. The plaintext is shown once, at creation. Tokens are never logged; tool-call logging (opt-in) records tool name, user, duration and outcome, never arguments or tokens.
- Tokens are looked up by hash on every request. Nothing about the principal is cached: revoking a token, disabling or deleting its user, or removing the user's administrator flag takes effect on the very next request.
- Optional expiry of 30 days, 90 days or 1 year, and an enabled flag.
- Jellyfin's generic plugin-configuration save (the endpoint that replaces the whole configuration object) cannot overwrite the token list: the plugin overrides `UpdateConfiguration` to keep the current tokens. A stale or hostile client therefore cannot resurrect revoked tokens or drop new ones. Tokens change only through the plugin's admin endpoints, under one lock.
- Token management endpoints (`/McpServer/Tokens`) require a Jellyfin administrator session (elevation policy). MCP tokens cannot manage tokens.

## Scopes

| Scope | Allows | Notes |
|---|---|---|
| `library` | Read-only browsing, search, details, history | Everything is limited to what the token's user may see. |
| `playback` | List sessions, play, control, send messages | The user's own sessions only (see below). |
| `manage` | Playlists, favorites, played state | The user's own watch state and favorites, plus playlists the user owns or may edit. |
| `admin` | Users, tasks, scans, metadata refresh | Granted only to administrators and dropped at request time if the user stops being one. |

Start with `library` and add scopes per client.

### Library access and parental controls

Library and parent ids are stricter: `libraryId` (search, recently added) must be one of the libraries `jellyfin_list_libraries` returns for the user, and `parentId` (browse) must be a visible folder that is not the server root. Jellyfin treats its root folders as visible to everyone, and using one as a query parent would bypass per-user library scoping and list hidden libraries, so they answer `Item not found`.

Every user-supplied item id (item, parent, library, series, person, playlist entries, play targets) is loaded with the user overload `ILibraryManager.GetItemById<BaseItem>(id, user)`. That overload enforces the user's enabled/blocked libraries and parental rating limits, so an item the user cannot see is indistinguishable from a missing one (`Item not found`).

`InternalItemsQuery` with `ItemIds` or `ParentId` is deliberately not used for this check: in Jellyfin 12.4 such a query does not enforce library-folder access, so a guessed id could leak an item from a hidden library. Queries without those fields are scoped by Jellyfin to the user's libraries, and list results are always built from a user-scoped query.

Playlists and collections (box sets) are the exception to that scoping: the item query has no owner or share filter for playlists, and Jellyfin's own post-filter (`IsVisibleStandalone(user)`) is skipped when a query is run directly. Every list tool (`browse`, `search`, `recently_added`, `continue_watching`, `watch_history`, `get_person`) therefore runs through a shared helper that drops any playlist or collection the user cannot see (another user's private playlist, a collection outside the user's libraries) and keeps `totalCount` exact. `jellyfin_search` leaves playlists and collections out unless `types` asks for them. When `types` mixes them with other kinds the SQL paging is kept, so `totalCount` can slightly overcount by hidden items on later pages. By-id access (`get_item`, ...) already applies the same check inside `GetItemById<BaseItem>(id, user)`, and `jellyfin_list_playlists` uses `IPlaylistManager.GetPlaylists(userId)`, which filters by `IsVisible(user)`.

### Sessions

`playback` tools act on active, remote-controllable sessions. A token can control sessions of its own user. Controlling another user's session requires both the `admin` scope and the Jellyfin permission "Allow remote control of other users" (`EnableRemoteControlOfOtherUsers`) on the token's user. Commands are sent without a controlling session, which makes Jellyfin skip its own permission check, so the plugin enforces ownership itself. A session you may not control returns the same `Session not found` as a wrong id.

### Destructive tools

- `jellyfin_manage_playlist` can delete playlists permanently.
- `jellyfin_run_task` can start tasks that delete cache, log or transcode files or clean the database. It is annotated destructive and refuses the plugin update task (`PluginUpdates`), which installs third-party code.
- `jellyfin_refresh_item` can overwrite metadata and images.

MCP clients are expected to ask for confirmation on tools annotated destructive; keep that confirmation enabled.

## Request hardening

- `POST /mcp` only; `GET` and `DELETE` answer 405. Maximum body 1 MiB (413 above).
- Failed authentication is rate limited: more than 10 failures per minute from one IP answers 429 with `Retry-After: 60`. To bound memory, at most 10 000 addresses are tracked; when that cap is reached, new addresses are not tracked and the limiter fails open for them. This cap only keeps an address flood from locking out legitimate clients; it does not change the proxy caveat below.
- A blocked IP gets 429 even when it presents a valid token. This is by design; configure Known Proxies (below) so one client's failures do not block others.
- `Origin` validation (DNS-rebinding protection required by the MCP transport) runs before authentication. Requests without an `Origin` header (Claude Code, Claude Desktop, scripts) are allowed. A browser origin is allowed when its host equals the host the request was sent to (scheme and port ignored, so TLS-terminating proxies work) or when it is listed in **Allowed origins**; otherwise the answer is 403 with an empty body, whatever the token. If a reverse proxy rewrites the `Host` header, list the public origin of any browser-based client. The bearer token remains the main protection: a rebinding page cannot obtain or send it.
- With `MCP-Protocol-Version: 2026-07-28`, the `Mcp-Method` and `Mcp-Name` headers must match the body (400 otherwise), and unknown versions are rejected with 400.
- The failure count is per remote IP. Behind a reverse proxy, configure Jellyfin's **Known Proxies** so the real client address is used; otherwise all clients share the proxy's address and a few failed attempts block everyone.
- Playlist `remove_items` only accepts entry ids that belong to the playlist and whose item is visible to the user (`IsVisible(user)` on the playlist's own entries), so hidden items cannot be probed through it.
- Argument validation: malformed arguments return `isError` results naming the argument; `limit` is clamped; id lists are capped (100).
- Error messages do not leak server internals; unexpected failures are reduced to a generic message.

## Deployment guidance

- Prefer a trusted LAN or VPN. If the server is reachable from the internet, put it behind HTTPS (tokens travel in a header) and consider restricting `/mcp` at the proxy by IP.
- Use one token per client, scoped minimally, with an expiry.
- Create a dedicated, low-privilege Jellyfin user for assistants that do not need your personal watch state, and limit its libraries.
- Revoke tokens you no longer use. The plugin page shows prefix and last-used time to identify them.
- Turn on **Log tool calls** if you want an audit trail.

## Threat model

| Threat | Mitigation |
|---|---|
| Token theft | Hashed storage, expiry, instant revocation, minimal scopes, HTTPS/VPN guidance. A stolen token gives only that user's access and scopes. |
| Token guessing | 256 bits of randomness plus the per-IP failure limiter. |
| Privilege escalation to admin | `admin` scope only for administrators, re-checked on every request. |
| Access to hidden libraries or rated-out content | User-aware item lookups for every id; hidden looks like missing. |
| Controlling other people's playback | Session ownership, plus admin scope and remote-control permission for foreign sessions. |
| Tampering with tokens via the generic plugin config API | `UpdateConfiguration` cannot change tokens. |
| Prompt injection through media metadata | Not prevented by the plugin: text in media metadata (titles, overviews) is passed to the assistant and may contain injected instructions. Mitigations are least-privilege scopes, destructive-tool annotations and client-side confirmation of tool calls. |
| Denial of service | 1 MiB body cap, failure limiter, bounded limiter memory, capped page sizes and id lists. |
| Plugin installing third-party code | `PluginUpdates` task cannot be run through MCP. |

Out of scope: a compromised Jellyfin server or host, and an attacker who is already a Jellyfin administrator.

## Reporting vulnerabilities

Please report security issues privately through GitHub security advisories on the repository rather than in a public issue.
