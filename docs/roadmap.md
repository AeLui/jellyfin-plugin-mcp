# Roadmap

Nothing here is committed; it is the intended direction.

## Authentication

OAuth 2.1 (authorization server, dynamic client registration, PKCE with a Jellyfin login page) was built and discarded on 2026-11-05. Static, per-user scoped tokens created on the plugin page stay the only authentication method.

## Done

- Per-library token restrictions: a token can be limited to some of its user's libraries (see [security.md](security.md#library-restricted-tokens)).

## Later

- MCP resources were considered on 2026-11-07 and deferred: item metadata as a resource duplicates `jellyfin_get_item`, and clients mostly use resources for manual attachment.
- Optional SSE streaming where useful.
- More clients documented and tested (other desktop and IDE assistants).
- More tools: user-scoped settings.
- Jellyfin plugin catalog listing and automated multi-version compatibility builds when new Jellyfin ABIs ship.
