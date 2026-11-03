---
name: jellyfin-mcp
description: Use when the user asks about their Jellyfin media server - finding or recommending movies, shows, music or people in their library, what to watch next, playing or controlling playback on a TV or device, managing playlists, favorites or watched state, or running Jellyfin admin tasks - and jellyfin_* MCP tools are available.
license: GPL-3.0
---

# Jellyfin MCP

## Overview

The `jellyfin_*` tools come from the open-source [Jellyfin MCP Server plugin](https://github.com/AeLui/jellyfin-plugin-mcp). Every call runs as the Jellyfin user who owns the token and only the tools of the token's scopes (`library`, `playback`, `manage`, `admin`) are listed. If a tool is missing, the token lacks that scope; say so instead of working around it.

Core principle: **get ids from the tools, never guess them.** Ids are GUIDs; an id the user cannot see returns `Item not found`, exactly like a wrong id. Argument names and enums are in each tool's input schema; `types` values are lowercase snake_case (`movie`, `series`, `episode`, `music_album`).

List results are item summaries that already carry the user's `played`, `favorite` and `playbackPositionMinutes`, so "unwatched films by X" is one `jellyfin_get_person` call filtered on `played`. When a search returns several plausible matches (remakes, same-name series), ask which one, showing the year.

## Setup (only if no jellyfin_* tools exist)

The user installs the plugin from the repository above, creates a token in **Dashboard > Plugins > MCP Server**, then:

```bash
claude mcp add --transport http jellyfin https://<server>/mcp --header "Authorization: Bearer <token>"
```

## Quick reference

| User wants | Call |
|---|---|
| Find a title | `jellyfin_search` (`query`, optional `types`, `genres`, `yearFrom`/`yearTo`) |
| Details, cast, overview | `jellyfin_get_item` |
| Everything with an actor/director | `jellyfin_get_person` (`name` or `personId` from `get_item` people) |
| Browse a library | `jellyfin_list_libraries`, then `jellyfin_browse` with that id |
| "What should I watch?" | `jellyfin_next_up`, `jellyfin_continue_watching`, `jellyfin_recently_added`, `jellyfin_browse` with `filter: unplayed` |
| What was watched | `jellyfin_watch_history` |
| Play on a device | `jellyfin_list_sessions`, then `jellyfin_play` |
| Pause, seek, skip | `jellyfin_playback_control` (`seek` needs `positionMinutes`) |
| Playlists | `jellyfin_list_playlists`, `jellyfin_manage_playlist` |
| Favorite / watched | `jellyfin_set_favorite`, `jellyfin_set_played` |
| Admin | `jellyfin_list_tasks`, `jellyfin_run_task`, `jellyfin_scan_libraries`, `jellyfin_refresh_item` |

## Workflows

**Play something:** search for the item, call `jellyfin_list_sessions`, pick the session by `deviceName`. If several devices match or none is named, ask which one. Then `jellyfin_play` with `mode` `now`, `next` or `last`. An empty session list means no controllable client is open; tell the user to open Jellyfin on the device.

**Recommendations:** use the user's own data (next up, continue watching, unplayed items, history) and only suggest items returned by the tools, so everything you recommend is actually in their library.

**Lists:** results are paged (`limit` default 20, `startIndex`, `totalCount`). When `totalCount` is larger than what you fetched, say so or page further; do not present a first page as the whole library.

**Playlists:** create with at least one item, because an empty playlist defaults to the audio type. `remove_items` takes `entryId`s from `jellyfin_list_playlists` with `playlistId`, not item ids, and removes every duplicate of that entry.

## Confirm before acting

Ask the user first for these, since they cannot be undone:

- `jellyfin_manage_playlist` with `delete` or `remove_items`
- `jellyfin_set_played` with `played: false` on a series or season (clears all episode progress)
- `jellyfin_run_task` (some tasks delete caches or clean the database)
- `jellyfin_refresh_item` with `replaceMetadata` or `replaceImages`

Searching, browsing and starting playback the user asked for need no confirmation.

## Common mistakes

- Passing a library name instead of an id to `browse`, `search` or `recently_added`: call `jellyfin_list_libraries` first.
- Reusing a session id from earlier in the conversation: sessions change; list them again before `play`.
- Treating `Item not found` as "not in the library" after a guessed id: search instead.
- Expecting `jellyfin_run_task` to update plugins: the plugin update task is refused by design.
