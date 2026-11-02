# Tool reference

22 tools in four scopes. `tools/list` only returns the tools allowed by the token's scopes, and `tools/call` on an unknown or not-permitted tool is a JSON-RPC error (-32602, `Unknown tool: <name>`), not an `isError` result.

## Conventions

- **Ids** are GUIDs. Results use the 32-hex "N" form (`3f2a...`); both forms are accepted as input.
- **Annotations** are the MCP hints `readOnlyHint` (RO), `destructiveHint` (D) and `idempotentHint` (I). Every tool declares all three.
- **Results** are returned as one text content block containing compact JSON. Null fields are omitted.
- **Errors** are results with `isError: true` and a message, for example `Invalid argument 'limit': must be an integer`, `Item not found` or `Session not found`. Unexpected failures return `Internal error while running <tool>` without details.
- **Paging**: list tools take `limit` (integer, default 20, clamped to 1..`Max page size`, default 100) and `startIndex` (integer, default 0) and return `{ "totalCount": n, "items": [...] }`.
- **Visibility**: every user-supplied item id is checked against the token user's library access and parental rating. An item the user cannot see is reported as `Item not found`, exactly like an id that does not exist.
- **Item summary** (used in `items`): `id`, `name`, `type` (snake_case), `year`, `seriesName`, `seasonNumber`, `episodeNumber`, `runtimeMinutes`, `communityRating`, `officialRating`, and the user's `played`, `favorite`, `playbackPositionMinutes`, `lastPlayedDate`.

The example results below are illustrative, not captured from a live server.

Summary of tools:

| Tool | Scope | RO / D / I |
|---|---|---|
| `jellyfin_search` | library | true / false / true |
| `jellyfin_browse` | library | true / false / true |
| `jellyfin_get_item` | library | true / false / true |
| `jellyfin_get_person` | library | true / false / true |
| `jellyfin_list_libraries` | library | true / false / true |
| `jellyfin_recently_added` | library | true / false / true |
| `jellyfin_next_up` | library | true / false / true |
| `jellyfin_continue_watching` | library | true / false / true |
| `jellyfin_watch_history` | library | true / false / true |
| `jellyfin_list_sessions` | playback | true / false / true |
| `jellyfin_play` | playback | false / false / false |
| `jellyfin_playback_control` | playback | false / false / true |
| `jellyfin_send_message` | playback | false / false / false |
| `jellyfin_list_playlists` | manage | true / false / true |
| `jellyfin_manage_playlist` | manage | false / true / false |
| `jellyfin_set_favorite` | manage | false / false / true |
| `jellyfin_set_played` | manage | false / true / true |
| `jellyfin_list_users` | admin | true / false / true |
| `jellyfin_list_tasks` | admin | true / false / true |
| `jellyfin_run_task` | admin | false / true / false |
| `jellyfin_scan_libraries` | admin | false / false / true |
| `jellyfin_refresh_item` | admin | false / true / false |

In the parameter tables, "req" means required.

---

## Scope `library`

All library tools are read-only and run as the token's user.

### jellyfin_search

Search the library by text, optionally filtered by type, genre, production year range or library.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `query` | string | yes | | Text to search for. |
| `types` | string[] | no | all except playlists and collections | Any of `movie`, `series`, `season`, `episode`, `audio`, `music_album`, `music_artist`, `playlist`, `box_set`. Playlists and collections are only searched when listed here, and then only those the user can see. |
| `genres` | string[] | no | | Restrict to these genres. |
| `yearFrom` | integer | no | | Earliest production year; without `yearTo` runs to the current year. |
| `yearTo` | integer | no | | Latest production year; without `yearFrom` starts at 1880. Years must be 1 to 9999, `yearFrom` must not exceed `yearTo`, and the span must be fewer than 200 years. |
| `libraryId` | string | no | | Restrict to one library; must be an id returned by `jellyfin_list_libraries` (anything else, including root folders, is `Item not found`). |
| `limit`, `startIndex` | integer | no | 20, 0 | Paging. |

```json
{"name":"jellyfin_search","arguments":{"query":"blade runner","types":["movie"]}}
```
```json
{"totalCount":2,"items":[{"id":"a1b2...","name":"Blade Runner","type":"movie","year":1982,"runtimeMinutes":117.0,"played":true,"favorite":false}]}
```

### jellyfin_browse

List the direct children of a library or folder with sorting and played/favorite filters.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `parentId` | string | yes | | Library or folder id. The server root folders are rejected with `Item not found`; use ids from `jellyfin_list_libraries` and below. |
| `sortBy` | string | no | `name` | `name`, `date_added`, `premiere_date`, `community_rating`, `random`. |
| `sortOrder` | string | no | `asc` | `asc`, `desc`. |
| `filter` | string | no | `all` | `all`, `unplayed`, `played`, `favorites`. |
| `limit`, `startIndex` | integer | no | 20, 0 | Paging. |

```json
{"name":"jellyfin_browse","arguments":{"parentId":"c0ffee...","sortBy":"date_added","sortOrder":"desc","filter":"unplayed","limit":2}}
```
```json
{"totalCount":57,"items":[{"id":"...","name":"Dune: Part Two","type":"movie","year":2024,"played":false,"favorite":false}]}
```

### jellyfin_get_item

Get the details of one item (overview, genres, studios, tags, people) by id.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `itemId` | string | yes | | Item id. |

```json
{"name":"jellyfin_get_item","arguments":{"itemId":"a1b2..."}}
```
```json
{"id":"a1b2...","name":"Blade Runner","type":"movie","year":1982,"overview":"...","genres":["Science Fiction"],"premiereDate":"1982-06-25T00:00:00.0000000Z","people":[{"name":"Harrison Ford","role":"Rick Deckard","type":"Actor","id":"9f8e..."}]}
```

### jellyfin_get_person

Get a person (actor, director, ...) by id or name, with the items of theirs that the user can see.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `personId` | string | one of | | Person id, as listed in the `people` of an item. |
| `name` | string | one of | | Exact person name. |
| `limit`, `startIndex` | integer | no | 20, 0 | Paging of the items list. |

Either `personId` or `name` is required. A person may be returned with metadata but an empty `items` list when none of their work is visible to the user.

```json
{"name":"jellyfin_get_person","arguments":{"name":"Harrison Ford","limit":2}}
```
```json
{"id":"9f8e...","name":"Harrison Ford","totalCount":5,"items":[{"id":"a1b2...","name":"Blade Runner","type":"movie","year":1982}],"overview":"...","birthDate":"1942-07-13T00:00:00.0000000Z"}
```

### jellyfin_list_libraries

List the media libraries the user can access, with their ids and collection types. No parameters.

```json
{"name":"jellyfin_list_libraries","arguments":{}}
```
```json
[{"id":"c0ffee...","name":"Movies","collectionType":"movies"},{"id":"d00d...","name":"Shows","collectionType":"tvshows"}]
```

### jellyfin_recently_added

List the most recently added items, newest first. Episodes are listed individually, so a freshly added season shows up as many entries; folders are never returned.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `libraryId` | string | no | | Restrict to one library; must be an id returned by `jellyfin_list_libraries`. |
| `types` | string[] | no | all non-folder | Any of `movie`, `episode`, `video`, `audio`. |
| `limit`, `startIndex` | integer | no | 20, 0 | Paging. |

```json
{"name":"jellyfin_recently_added","arguments":{"types":["movie"],"limit":1}}
```
```json
{"totalCount":412,"items":[{"id":"...","name":"Dune: Part Two","type":"movie","year":2024}]}
```

### jellyfin_next_up

List the next episode to watch for each series the user is following, or for one series.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `seriesId` | string | no | | Restrict to one series; an id that is not a series is an error. |
| `limit`, `startIndex` | integer | no | 20, 0 | Paging. |

```json
{"name":"jellyfin_next_up","arguments":{"limit":1}}
```
```json
{"totalCount":3,"items":[{"id":"...","name":"Pilot","type":"episode","seriesName":"Severance","seasonNumber":1,"episodeNumber":1,"played":false}]}
```

### jellyfin_continue_watching

List movies, episodes and videos the user has started but not finished, most recently played first.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `limit`, `startIndex` | integer | no | 20, 0 | Paging. |

```json
{"name":"jellyfin_continue_watching","arguments":{"limit":1}}
```
```json
{"totalCount":1,"items":[{"id":"...","name":"Dune","type":"movie","runtimeMinutes":155.0,"playbackPositionMinutes":48.5,"played":false}]}
```

### jellyfin_watch_history

List items the user has played, most recently played first, with the last played date.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `limit`, `startIndex` | integer | no | 20, 0 | Paging. |

```json
{"name":"jellyfin_watch_history","arguments":{"limit":1}}
```
```json
{"totalCount":230,"items":[{"id":"...","name":"Alien","type":"movie","played":true,"lastPlayedDate":"2026-10-30T21:14:02.0000000Z"}]}
```

---

## Scope `playback`

These tools control player sessions. A token controls the active, remote-controllable sessions of its own user. To control sessions of other users the token needs the `admin` scope **and** the Jellyfin user must have the "Allow remote control of other users" permission. A session that is not controllable looks the same as a wrong id: `Session not found`.

### jellyfin_list_sessions

List the active player sessions you can control, with what each is playing. No parameters.

```json
{"name":"jellyfin_list_sessions","arguments":{}}
```
```json
[{"sessionId":"7c1d...","deviceName":"Living room TV","client":"Jellyfin Android TV","userName":"alice","nowPlaying":{"id":"...","name":"Dune","type":"movie"},"isPaused":false,"positionMinutes":48.5}]
```

### jellyfin_play

Play items on one of your sessions (now, next or at the end of the queue).

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `sessionId` | string | yes | | From `jellyfin_list_sessions`. |
| `itemIds` | string[] | yes | | 1 to 100 item ids; all must be visible to the user. |
| `mode` | string | no | `now` | `now`, `next` (play next), `last` (end of the queue). |
| `startPositionMinutes` | integer | no | | Where to start, in minutes (not negative). |

```json
{"name":"jellyfin_play","arguments":{"sessionId":"7c1d...","itemIds":["a1b2..."],"mode":"now","startPositionMinutes":10}}
```
```json
{"sessionId":"7c1d...","mode":"now","itemCount":1}
```

### jellyfin_playback_control

Pause, unpause, stop, seek, or skip to the next or previous item on one of your sessions.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `sessionId` | string | yes | | From `jellyfin_list_sessions`. |
| `command` | string | yes | | `pause`, `unpause`, `stop`, `seek`, `next`, `previous`. |
| `positionMinutes` | number | for `seek` | | Target position in minutes, not negative. |

```json
{"name":"jellyfin_playback_control","arguments":{"sessionId":"7c1d...","command":"seek","positionMinutes":62.5}}
```
```json
{"sessionId":"7c1d...","command":"seek"}
```

### jellyfin_send_message

Show a message on the screen of one of your sessions.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `sessionId` | string | yes | | From `jellyfin_list_sessions`. |
| `text` | string | yes | | 1 to 500 characters. |
| `header` | string | no | empty | Message title. |
| `timeoutSeconds` | integer | no | 10 | 1 to 3600. |

```json
{"name":"jellyfin_send_message","arguments":{"sessionId":"7c1d...","text":"Dinner is ready","timeoutSeconds":30}}
```
```json
{"sessionId":"7c1d...","sent":true}
```

---

## Scope `manage`

These tools change the user's own watch state and favorites, plus playlists the user owns or may edit (playlists shared with the user with edit permission). Nothing else is changed.

### jellyfin_list_playlists

List the playlists the user can access, or, with `playlistId`, the entries of one playlist.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `playlistId` | string | no | | When given, lists the entries of that playlist; each has an `entryId` for `remove_items`. |
| `limit`, `startIndex` | integer | no | 20, 0 | Paging. |

```json
{"name":"jellyfin_list_playlists","arguments":{}}
```
```json
{"totalCount":1,"items":[{"id":"e5f6...","name":"Road trip","isOwner":true,"canEdit":true,"isPublic":false}]}
```

With `playlistId`, `items` are item summaries prefixed by `entryId`.

### jellyfin_manage_playlist

Manage playlists. Every action changes data and `delete` is permanent, so the tool is annotated destructive. Only playlists you own, or that are shared with you with edit permission, can be changed; only the owner can delete.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `action` | string | yes | | `create`, `add_items`, `remove_items`, `rename`, `delete`. |
| `playlistId` | string | all but `create` | | Playlist id. |
| `name` | string | `create`, `rename` | | Playlist name. |
| `itemIds` | string[] | `add_items` (optional for `create`) | | Up to 100 visible item ids. |
| `entryIds` | string[] | `remove_items` | | Entry ids from `jellyfin_list_playlists`; up to 100. |

Caveats: `remove_items` removes every duplicate of an entry (an item added twice disappears entirely); a playlist created without items defaults to the Audio media type (Jellyfin behaviour), so create it with at least one item of the intended kind.

```json
{"name":"jellyfin_manage_playlist","arguments":{"action":"create","name":"Road trip","itemIds":["a1b2..."]}}
```
```json
{"action":"create","playlistId":"e5f6...","name":"Road trip"}
```

### jellyfin_set_favorite

Add an item to, or remove it from, the favorites of the token's user.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `itemId` | string | yes | | Item id. |
| `favorite` | boolean | yes | | `true` to add, `false` to remove. |

```json
{"name":"jellyfin_set_favorite","arguments":{"itemId":"a1b2...","favorite":true}}
```
```json
{"id":"a1b2...","name":"Blade Runner","type":"movie","favorite":true,"played":true}
```

### jellyfin_set_played

Mark an item as played or unplayed for the token's user. For a series or season this applies to all its episodes. Changes watch history. `played: false` on a series or season clears the play state and resume positions of all its episodes and cannot be undone, so the tool is annotated destructive.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `itemId` | string | yes | | Item id. |
| `played` | boolean | yes | | `true` for played, `false` for unplayed. |

```json
{"name":"jellyfin_set_played","arguments":{"itemId":"a1b2...","played":false}}
```
```json
{"id":"a1b2...","name":"Blade Runner","type":"movie","played":false,"favorite":true}
```

---

## Scope `admin`

Only for Jellyfin administrators. At request time the `admin` scope is dropped for a user who is not (or no longer) an administrator, so these tools disappear from `tools/list`.

### jellyfin_list_users

List the server's users: id (32-character hex without dashes, like every other id), name, administrator and disabled flags, and last activity and login dates. Read-only; no password, policy or authentication-provider details are exposed. No parameters.

```json
{"name":"jellyfin_list_users","arguments":{}}
```
```json
[{"id":"1a2b3c4d5e6f47088192a3b4c5d6e7f8","name":"alice","isAdministrator":true,"isDisabled":false,"lastActivityDate":"2026-11-01T18:00:00.0000000Z"}]
```

### jellyfin_list_tasks

List the server's scheduled tasks with their state, progress (percent) and last result. No parameters.

```json
{"name":"jellyfin_list_tasks","arguments":{}}
```
```json
[{"id":"b7c8...","key":"RefreshLibrary","name":"Scan Media Library","category":"Library","state":"Idle","lastResult":{"status":"Completed","endTime":"2026-11-02T03:00:12.0000000Z"}}]
```

### jellyfin_run_task

Start a scheduled task by id or key. The task runs in the background; follow it with `jellyfin_list_tasks`. Annotated destructive because some tasks delete cache, log or transcode files or clean up the database. The plugin update task (`PluginUpdates`) is refused, because it installs third-party code. A task that is already running is an error.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `taskId` | string | one of | | From `jellyfin_list_tasks`. |
| `key` | string | one of | | Used when `taskId` is not given. |

```json
{"name":"jellyfin_run_task","arguments":{"key":"RefreshLibrary"}}
```
```json
{"started":true}
```

### jellyfin_scan_libraries

Start a scan of all media libraries for new, changed and removed files. Runs in the background; a scan already running counts as success. No parameters.

```json
{"name":"jellyfin_scan_libraries","arguments":{}}
```
```json
{"queued":true}
```

### jellyfin_refresh_item

Queue a metadata refresh for an item, optionally replacing its images and/or metadata. The refresh runs in the background and may overwrite existing metadata, so the tool is annotated destructive. The item must be visible to the token's user.

| Parameter | Type | Req | Default | Notes |
|---|---|---|---|---|
| `itemId` | string | yes | | Item id. |
| `replaceImages` | boolean | no | false | Replace existing images. |
| `replaceMetadata` | boolean | no | false | Replace existing metadata. |

```json
{"name":"jellyfin_refresh_item","arguments":{"itemId":"a1b2...","replaceMetadata":true}}
```
```json
{"queued":true}
```
