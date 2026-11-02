# Roadmap

Nothing here is committed; it is the intended direction.

## v2: OAuth 2.1 and claude.ai connectors

claude.ai connectors (and other clients that cannot send a static header) need OAuth 2.1. Plan:

- Authorization server endpoints: authorization, token, revocation and server metadata (RFC 8414), plus protected-resource metadata (RFC 9728) for `/mcp`.
- Dynamic client registration (RFC 7591) so clients can register themselves.
- Authorization with PKCE, using a Jellyfin login page so users sign in with their Jellyfin account and approve a consent screen.
- Scopes are the existing ones: `library`, `playback`, `manage`, `admin`. OAuth grants map onto the same principal and tool filtering as static tokens.
- Static tokens stay supported for Claude Code and scripts.

## Protocol

- Support the stateless 2026-07-28 MCP revision once clients adopt it. Today the plugin supports 2025-11-25, 2025-06-18 and 2025-03-26.

## Later

- MCP resources (for example library and item metadata as resources) and prompts.
- Optional SSE streaming where useful.
- More clients documented and tested (other desktop and IDE assistants).
- More tools: subtitles and audio track selection, collections, user-scoped settings, richer admin views.
- Finer-grained, per-library token restrictions.
- Jellyfin plugin catalog listing and automated multi-version compatibility builds when new Jellyfin ABIs ship.
