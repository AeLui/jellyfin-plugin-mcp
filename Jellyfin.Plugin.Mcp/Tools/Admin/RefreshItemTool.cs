using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Mcp.Configuration;
using Jellyfin.Plugin.Mcp.Mcp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;

namespace Jellyfin.Plugin.Mcp.Tools.Admin;

/// <summary>
/// Queues a metadata refresh for an item.
/// </summary>
public sealed class RefreshItemTool : AdminToolBase
{
    private static readonly JsonElement Schema = JsonDocument.Parse(
        """{"type":"object","properties":{"itemId":{"type":"string","description":"Item id (GUID)."},"replaceImages":{"type":"boolean","description":"Replace existing images. Default false."},"replaceMetadata":{"type":"boolean","description":"Replace existing metadata. Default false."}},"required":["itemId"]}""").RootElement.Clone();

    private static readonly JsonElement ResultSchema = OutputSchemas.Object("\"queued\":{\"type\":\"boolean\"}", "queued");

    private readonly ItemLookup _lookup;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshItemTool"/> class.
    /// </summary>
    /// <param name="userManager">The user manager.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="lookup">The visibility-checked item lookup.</param>
    /// <param name="providerManager">The provider manager.</param>
    /// <param name="fileSystem">The file system.</param>
    public RefreshItemTool(IUserManager userManager, IPluginSettings settings, ItemLookup lookup, IProviderManager providerManager, IFileSystem fileSystem)
        : base(userManager, settings)
    {
        _lookup = lookup;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public override string Name => "jellyfin_refresh_item";

    /// <inheritdoc />
    public override string Title => "Refresh item metadata";

    /// <inheritdoc />
    public override string Description => "Queue a metadata refresh for an item, optionally replacing its images and/or metadata. The refresh runs in the background and may overwrite existing metadata.";

    /// <inheritdoc />
    public override JsonElement InputSchema => Schema;

    /// <inheritdoc />
    public override JsonElement? OutputSchema => ResultSchema;

    /// <inheritdoc />
    public override ToolAnnotations Annotations { get; } = new(false, true, false);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(ToolArguments args, McpPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var id = args.GetRequiredGuid("itemId");
        var replaceImages = args.GetBool("replaceImages") ?? false;
        var replaceMetadata = args.GetBool("replaceMetadata") ?? false;

        // Admins can still have library restrictions, so the item must be visible to the token's user.
        var item = _lookup.GetVisible(GetScopedUser(principal), id);

        var mode = replaceImages || replaceMetadata ? MetadataRefreshMode.FullRefresh : MetadataRefreshMode.Default;
        var options = new MetadataRefreshOptions(new DirectoryService(_fileSystem))
        {
            MetadataRefreshMode = mode,
            ImageRefreshMode = mode,
            ReplaceAllImages = replaceImages,
            ReplaceAllMetadata = replaceMetadata,
            RemoveOldMetadata = replaceMetadata,
            ForceSave = replaceImages || replaceMetadata,
            IsAutomated = false,
        };

        _providerManager.QueueRefresh(item.Id, options, RefreshPriority.High);
        return Task.FromResult(ToolResult.Json(new Dictionary<string, object> { ["queued"] = true }));
    }
}
