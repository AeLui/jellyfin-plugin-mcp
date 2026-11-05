# Roadmap

Nothing here is committed; it is the intended direction.

## Authentication

OAuth 2.1 (authorization server, dynamic client registration, PKCE with a Jellyfin login page) was built and discarded on 2026-11-05. Static, per-user scoped tokens created on the plugin page stay the only authentication method.

## Later

- MCP resources (for example library and item metadata as resources) and prompts.
- Optional SSE streaming where useful.
- More clients documented and tested (other desktop and IDE assistants).
- More tools: subtitles and audio track selection, collections, user-scoped settings, richer admin views.
- Finer-grained, per-library token restrictions.
- Jellyfin plugin catalog listing and automated multi-version compatibility builds when new Jellyfin ABIs ship.
