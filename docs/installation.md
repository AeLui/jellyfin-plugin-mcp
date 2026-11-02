# Installation

## Requirements

- Jellyfin 12.x (the plugin targets ABI 12.4.0.0).
- For Claude Desktop (expected to work via `mcp-remote`, not yet verified): [Node.js](https://nodejs.org), because the desktop app talks to remote servers through `npx mcp-remote`.

## Install from the plugin repository

1. **Dashboard > Plugins > Repositories > +**, then add:
   `https://raw.githubusercontent.com/AFBenot/jellyfin-plugin-mcp/main/manifest.json`
2. **Dashboard > Plugins > Catalog**, find **MCP Server** (category General), install it and restart Jellyfin.

## Manual install

Download `jellyfin-plugin-mcp_<version>.zip` from the GitHub releases page, extract `Jellyfin.Plugin.Mcp.dll` into `<jellyfin config>/plugins/MCP Server_<version>/` and restart Jellyfin.

## Configuration

Open **Dashboard > Plugins > MCP Server**.

| Setting | Default | Meaning |
|---|---|---|
| Enable MCP endpoint | on | When off, `/mcp` answers 503 and no tool runs. |
| Max page size | 100 | Upper limit for the `limit` of list tools (the dashboard accepts 1 to 1000; the server itself only floors it at 1). A larger `limit` is clamped to this value, not rejected. |
| Log tool calls | off | One log line per tool call: tool name, user, duration, outcome. Arguments and tokens are never logged. |

The page also shows the endpoint URL (`<your server>/mcp`).

## Tokens

Create a token with **New token**:

- **Name**: a label (1 to 100 characters).
- **User**: the Jellyfin user the token acts as. Every call is made as this user.
- **Scopes**: `library`, `playback`, `manage`, `admin`. The `admin` scope can only be granted to a Jellyfin administrator, and is ignored at request time if the user is no longer one.
- **Expires**: never, 30 days, 90 days or 1 year.

The full token (`jmcp_...`) is displayed once, together with ready-to-use snippets for Claude Code and Claude Desktop. Only a SHA-256 hash is stored, so a lost token cannot be recovered: revoke it and create a new one. Revoking takes effect on the next request.

Give each client its own token and grant only the scopes it needs; start with `library`.

## Connecting clients

The endpoint accepts `POST /mcp` with `Authorization: Bearer <token>` and a JSON body. `GET` and `DELETE` answer 405.

### Claude Code

```bash
claude mcp add --transport http jellyfin https://jellyfin.example.com/mcp \
  --header "Authorization: Bearer jmcp_..."
```

Run `/mcp` in Claude Code to check that the server is connected and to see the tool count.

### Claude Desktop (expected to work, not yet verified)

Edit `claude_desktop_config.json` (Settings > Developer > Edit config):

```json
{
  "mcpServers": {
    "jellyfin": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "https://jellyfin.example.com/mcp",
               "--header", "Authorization: Bearer jmcp_..."]
    }
  }
}
```

Restart Claude Desktop afterwards. This `mcp-remote` flow is expected to work but has not been verified against a live server yet.

### Other clients

Any MCP client that can send a custom `Authorization` header over Streamable HTTP works. claude.ai web connectors require OAuth 2.1, which is on the [roadmap](roadmap.md).

## Reverse proxy and HTTPS

Tokens travel in a header, so use HTTPS whenever the server is not on a trusted LAN or VPN. If Jellyfin is behind a reverse proxy, add the proxy to **Dashboard > Networking > Known Proxies**; otherwise every client appears to come from the proxy's IP and shares one failed-login bucket (see [security](security.md)). Make sure the proxy forwards `Authorization` and allows request bodies up to 1 MiB.

## Troubleshooting

| Symptom | Cause |
|---|---|
| 401 | Missing, wrong, revoked or expired token, or the user is disabled or deleted. |
| 429 with `Retry-After: 60` | Too many failed attempts from this IP in a minute. |
| 503 | The endpoint is disabled in the plugin settings, or the plugin is not ready yet. |
| 405 | You used GET or DELETE; only POST is supported. |
| 415 | The request is not `Content-Type: application/json`. |
| 413 | Request body over 1 MiB. |
| Claude Code shows an authentication or OAuth prompt after you revoked a token | The server now answers 401 and Claude Code tries OAuth discovery, which this plugin does not offer. Remove the server (`claude mcp remove <name>`) and add it again with a new token. |
| A tool is missing | The token's scopes do not include it, or `admin` was dropped because the user is not an administrator. |
