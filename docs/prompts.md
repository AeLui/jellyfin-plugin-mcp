# Prompt reference

The server also offers four MCP prompts. A prompt is a ready-made request that the client shows to the user, usually as a slash command: in Claude Code they appear as `/mcp__<server>__<prompt>`, for example `/mcp__jellyfin__movie_night`. Picking one sends the model a short instruction that names the `jellyfin_*` tools to call; the prompt itself reads nothing from Jellyfin.

Prompts follow the token's scopes like tools: `prompts/list` only returns the prompts the token may use, and `prompts/get` for any other name is a JSON-RPC error (-32602, `Unknown prompt: <name>`). A prompt only helps when the token also has the tools it names, which the scope guarantees. When the token also has the `playback` scope, `movie_night`, `catch_me_up` and `whats_new` end by offering to play the choice on a device, and never start playback without the user's confirmation.

All arguments are optional strings, as MCP prompt arguments are. Empty values count as missing; a bad value is a -32602 error naming the argument (`Invalid argument 'days': must be an integer between 1 and 90`). Argument values are never logged.

| Prompt | Scope | Arguments | What it does |
|---|---|---|---|
| `movie_night` | library | `genre`, `maxMinutes`, `mood` | Picks three unwatched movies for tonight |
| `catch_me_up` | library | none | Summarises what you are watching and what comes next |
| `whats_new` | library | `days`, `type` | Lists what was added recently |
| `library_health` | admin | `days` | Checks server health and recent errors |

## movie_night

Pick a movie the user has not watched yet. The model uses `jellyfin_movie_recommendations`, `jellyfin_search`, `jellyfin_browse` with `filter: "unplayed"` and `jellyfin_get_item`, and proposes three options with one line each on why they fit.

| Argument | Type | Default | Notes |
|---|---|---|---|
| `genre` | text | | Up to 100 characters. |
| `maxMinutes` | integer | | 30 to 600; longest runtime. |
| `mood` | text | | Up to 100 characters, for example `something light`. |

```json
{"jsonrpc":"2.0","id":1,"method":"prompts/get","params":{"name":"movie_night","arguments":{"genre":"Drama","maxMinutes":"120"}}}
```
```json
{"jsonrpc":"2.0","id":1,"result":{"description":"Movie night: Drama, under 120 minutes","messages":[{"role":"user","content":{"type":"text","text":"Pick a movie for me to watch tonight that I have not seen yet. Genre: \"Drama\". It must be at most 120 minutes long (runtimeMinutes). Find candidates with jellyfin_movie_recommendations ... Reply in the user's language."}}]}}
```

## catch_me_up

Summarise what the user has in progress (with minutes left) and the next episode of each series they follow, from `jellyfin_continue_watching` and `jellyfin_next_up`. No arguments.

## whats_new

List what was added in the last days with `jellyfin_recently_added`, paging until an item's `dateAdded` is older than the cut-off. The cut-off is computed by the server when the prompt is fetched and written into the text as an ISO 8601 UTC timestamp. Episodes are grouped by series and tracks by album, and items matching the user's `jellyfin_watch_history` are pointed out.

| Argument | Type | Default | Notes |
|---|---|---|---|
| `days` | integer | 7 | 1 to 90. |
| `type` | string | `all` | `movies`, `shows`, `music` or `all` (case-insensitive); maps to `types` `["movie"]`, `["episode"]`, `["audio"]` or none. |

## library_health

For administrators. Checks `jellyfin_server_status` (pending restart, low disk space), `jellyfin_list_tasks` (failed or long-running tasks), `jellyfin_activity_log` (errors, then warnings, since the cut-off) and `jellyfin_list_all_sessions` (transcodes), and reports warnings first. The text tells the model not to run tasks or scans unless asked and not to repeat client IP addresses from the activity log unless asked.

| Argument | Type | Default | Notes |
|---|---|---|---|
| `days` | integer | 1 | 1 to 30; how far back to read the activity log. |

## Protocol notes

- Handshake era (2025-xx): `initialize` advertises `"prompts":{"listChanged":false}`.
- Stateless era (2026-07-28): `server/discover` advertises `"prompts":{}`; `prompts/get` needs the `Mcp-Name` header with the prompt name (400, -32020 when missing or different); `prompts/list` carries `ttlMs: 300000` and `cacheScope: "private"`.
- No pagination: `prompts/list` returns every prompt the token may use.
