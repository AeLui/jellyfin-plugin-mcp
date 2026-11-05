# Development

## Requirements

- .NET SDK 10 (`global.json` pins 10.0.100 with roll-forward to the latest feature band).
- Docker, only for the local Jellyfin test server.
- Python 3 for the manifest script tests.

## Layout

```
Jellyfin.Plugin.Mcp/        plugin (Api, Auth, Mcp, Tools, Configuration)
Jellyfin.Plugin.Mcp.Tests/  xUnit tests
scripts/                    update-manifest.py (+ unit tests)
.github/workflows/          ci.yml, release.yml
docker-compose.dev.yml      local Jellyfin 12.4
manifest.json               plugin repository manifest (maintained by the release job)
build.yaml                  plugin metadata and version
server.json                 MCP Registry entry (published by hand, see Releasing)
.claude-plugin/             Claude Code plugin marketplace: the skill plus the MCP connection (URL and token as userConfig); check with `claude plugin validate .`
```

## Commands

```bash
dotnet build -c Release          # 0 warnings expected (warnings are errors in the plugin)
dotnet test                      # all tests
dotnet format --verify-no-changes
python3 -I -m unittest discover -s scripts
```

`dotnet format` without the flag applies fixes. CI runs the same four commands.

## Conventions

- Target `net10.0`; Jellyfin packages pinned to 12.4.0 with `ExcludeAssets=runtime`. No other runtime NuGet dependencies.
- Verify every Jellyfin API against the v12.4 source before use.
- `Mcp/` must not reference `Jellyfin.*` or `MediaBrowser.*`.
- Tool names are `jellyfin_<snake_case>`; every tool declares `readOnlyHint`, `destructiveHint`, `idempotentHint`.
- Docs, comments and commit messages are in English.
- Never log tokens. Always query as the token's user.

## Adding a tool

1. Create a class in `Tools/<Scope>/` deriving from that scope's base (`LibraryToolBase`, `PlaybackToolBase`, `ManageToolBase`, `AdminToolBase`), which derive from `JellyfinToolBase`.
2. Define `Name`, `Description`, a JSON `InputSchema`, `Annotations` and `ExecuteAsync`. Parse arguments only through `ToolArguments`; throw `ToolException` for user errors.
3. Check every supplied item id with `ItemLookup.GetVisible(user, id)`; folder ids used as a query parent go through `GetVisibleContainer`, and library ids through `EnsureUserLibrary`.
4. No manual registration is needed: `PluginServiceRegistrator` registers every non-abstract `IMcpTool` in the assembly.
5. Write tests, including user scoping (a user must not reach items or sessions outside their access) and bad arguments.
6. Update [tools.md](tools.md) and the tool-count test (currently 22 tools).

## Tests

xUnit tests mirror the source layout: `Api`, `Auth`, `Mcp`, `Tools/{Library,Playback,Manage,Admin}`. Jellyfin services are faked, so no server is needed. The `Mcp` tests use a fake tool and never touch Jellyfin.

## Local Jellyfin and manual verification

Not yet run against a live server: the manual checklist below has not been run. Run it before the first release.

Setup:

```bash
dotnet publish Jellyfin.Plugin.Mcp -c Release   # or: dotnet build -c Release
docker compose -f docker-compose.dev.yml up -d
```

`docker-compose.dev.yml` runs `jellyfin/jellyfin:12.4` on port 8096 and mounts `Jellyfin.Plugin.Mcp/bin/Release/net10.0` as the plugin folder `MCP Server_0.1.0.0`. State lives in `.dev/` (git-ignored); put a few test media files in `.dev/media`, finish the Jellyfin setup wizard, add the libraries, and create a non-admin test user. Create a second library the test user cannot access.

Checklist:

1. The plugin appears as **MCP Server** in Dashboard > Plugins and its page loads.
2. Create a token for the non-admin test user with `library` and `playback`; the dialog shows both snippets.
3. `claude mcp add --transport http jellyfin-dev http://localhost:8096/mcp --header "Authorization: Bearer <token>"`; in Claude Code, `/mcp` shows the server connected with 13 tools (9 library + 4 playback).
4. Ask Claude to search the test library and get an item: results are correct, and an item in a library hidden from that user is not returned (also try its id directly).
5. Revoke the token in the plugin page; the next call fails with 401.
6. `curl -i http://localhost:8096/mcp` answers 405.
7. As admin, fetch the real root folder id from `GET /Items/Root` and pass it as `libraryId` to `jellyfin_search` and `jellyfin_recently_added` and as `parentId` to `jellyfin_browse` with the restricted user's token: each must answer `Item not found` and list nothing from the hidden library.
8. With the restricted user's token, `jellyfin_browse` a normal library, the Playlists view and the Collections view (ids from `jellyfin_list_libraries`): the lists are not empty, and another user's private playlist and items of a hidden library must not appear. Also call `jellyfin_get_person` with an actor credited only in the hidden library: `Item not found`.
9. Optional: with an admin token holding all scopes, `/mcp` lists 22 tools, each with a `title` and an `outputSchema`, and `tools/call` results carry `structuredContent`; a `manage` token can create a playlist; with Log tool calls on, the Jellyfin log shows one line per call and no tokens.

Fix any defect with a test first.

Stop the server with `docker compose -f docker-compose.dev.yml down`.

## Releasing

1. Set `version` in `build.yaml` (and the `Version` properties in `Directory.Build.props`) to the new version, for example `0.1.0.0`; update `changelog`. Set the same version, in three parts (`0.1.0`), in `server.json` and `.claude-plugin/plugin.json`. The Claude Code plugin is pinned to that version, so changes to `skills/` or `.claude-plugin/` reach users only when it changes; bump it in the release commit so the skill never describes tools that are not released yet. If the skill changed, optionally upload a new zip of `skills/jellyfin-mcp` to the Agensi listing.
2. Make sure CI is green on `main`.
3. Tag and push: `git tag v0.1.0 && git push origin v0.1.0`.

The tag must be `vX.Y.Z` (or `vX.Y.Z.W`) and its version, padded to four parts, must equal `build.yaml`'s `version`; the release workflow fails otherwise.

The release workflow then:

1. builds with the tag's version, runs the tests;
2. zips `Jellyfin.Plugin.Mcp.dll` as `jellyfin-plugin-mcp_<version>.zip` and computes its MD5 checksum;
3. creates (or updates) the GitHub release with the zip;
4. runs `scripts/update-manifest.py` to prepend the version to `manifest.json` and pushes that commit directly to `main`.

Note: step 4 pushes to `main` with the workflow's `GITHUB_TOKEN`. If `main` ever gets branch protection that blocks direct pushes, the manifest update will fail and the release job must be changed (for example to open a pull request, or to allow the Actions bot to bypass the rule).

Users receive the update through the repository URL `https://raw.githubusercontent.com/AeLui/jellyfin-plugin-mcp/main/manifest.json`.

### MCP Registry

The plugin is listed in the official MCP Registry as `io.github.AeLui/jellyfin-plugin-mcp`, described by `server.json` (a remote-only entry: each user fills in `jellyfin_host` and the `Authorization` header). After a release, publish the new version from the repository root with [`mcp-publisher`](https://github.com/modelcontextprotocol/registry/releases):

```
mcp-publisher login github   # as AeLui
mcp-publisher validate
mcp-publisher publish
```

The registry allows each remote URL for only one server, so keep the `{jellyfin_host}` variable name (`https://{host}/mcp` is already taken by another server).

## Supporting a new Jellyfin version

Bump `Jellyfin.Controller`/`Jellyfin.Model` and `targetAbi` in `build.yaml` and in the release workflow's `--target-abi`, re-verify every Jellyfin API used against the new source, and run the full test suite and the manual checklist.
