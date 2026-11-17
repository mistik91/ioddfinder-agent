using System.ComponentModel;
using System.Text.Json;
using IoddFinder.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace IoddFinder.Cli;

public static class McpHost
{
    public static async Task RunAsync(CatalogClient client, DownloadLibrary library, CancellationToken ct)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [], DisableDefaults = true
        });
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddSingleton(client);
        builder.Services.AddSingleton(library);
        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<McpTools>();
        await builder.Build().RunAsync(ct);
    }
}

[McpServerToolType]
public sealed class McpTools(CatalogClient client, DownloadLibrary library)
{
    [McpServerTool(Name = "iodd_search", ReadOnly = true), Description("Search the live IODDfinder catalog. No local catalog required. Results are paginated; IDs identify variants and exact packages. Exact text matching is optional.")]
    public async Task<string> Search(string? vendorName = null, int? vendorId = null, string? productName = null,
        string? productId = null, int? deviceId = null, string? ioLinkRevision = null,
        bool allVersions = false, int page = 0, int size = 20, bool exact = false, string? sort = null, CancellationToken ct = default) =>
        (await client.SearchAsync(new(vendorName, vendorId, productName, productId, deviceId,
            ioLinkRevision, allVersions, page, size, exact, sort), ct)).ToJsonString();

    [McpServerTool(Name = "iodd_suggest", ReadOnly = true), Description("Find categorized live suggestions for manufacturer, product name or article number. Follow with iodd_search to get device records.")]
    public async Task<string> Suggest(string text, CancellationToken ct = default) => (await client.SuggestAsync(text, ct)).ToJsonString();

    [McpServerTool(Name = "iodd_vendors", ReadOnly = true), Description("List manufacturer names from the live service.")]
    public async Task<string> Vendors(CancellationToken ct = default) => (await client.VendorsAsync(ct)).ToJsonString();

    [McpServerTool(Name = "iodd_details", ReadOnly = true), Description("Get live product details by productVariantId from a search result.")]
    public async Task<string> Details(int productVariantId, CancellationToken ct = default) => (await client.DetailsAsync(productVariantId, ct)).ToJsonString();

    [McpServerTool(Name = "iodd_versions", ReadOnly = true), Description("List live versions for a vendor/device, optionally narrowed by exact product name and IO-Link revision. Results may include multiple variants; do not assume the first is newest.")]
    public async Task<string> Versions(int vendorId, int deviceId, string? productName = null,
        string? ioLinkRevision = null, int page = 0, int size = 20, CancellationToken ct = default) =>
        (await client.SearchAsync(new(VendorId: vendorId, DeviceId: deviceId, ProductName: productName,
            IoLinkRevision: ioLinkRevision, AllVersions: true, Exact: true, Page: page, Size: size), ct)).ToJsonString();

    [McpServerTool(Name = "iodd_inspect", ReadOnly = true), Description("Inspect live structured IODDviewer data. Omit section/query to list sections. Select a section by exact name or ID; query filters names/descriptions/indexes. Results are bounded. ioLinkRevision is NOT an IODD release selector; historical package inspection is not supported here.")]
    public async Task<string> Inspect(int vendorId, int deviceId, string ioLinkRevision = "1.1",
        string? section = null, string? query = null, int offset = 0, int limit = 20, CancellationToken ct = default) =>
        ViewerInspection.Select(await client.ViewerAsync(vendorId, deviceId, ioLinkRevision, ct),
            vendorId, deviceId, ioLinkRevision, section, query, offset, limit).ToJsonString();

    [McpServerTool(Name = "iodd_download"), Description("Download an exact IODD package by vendorId and ioddId. Saves into the local managed library by default; a hash-verified saved package avoids repeat downloads. save=false inspects a transient download and keeps no file. refresh=true forces a download. Returns file metadata, not ZIP bytes.")]
    public async Task<string> Download(int vendorId, long ioddId, bool save = true, bool refresh = false, CancellationToken ct = default) =>
        JsonSerializer.Serialize(await library.DownloadAsync(client, vendorId, ioddId, save, refresh, ct: ct), LibraryJson.Default.DownloadResult);

    [McpServerTool(Name = "iodd_library_list", ReadOnly = true), Description("List locally saved packages. This library is optional and is not the remote catalog.")]
    public async Task<string> LibraryList(CancellationToken ct = default) => JsonSerializer.Serialize(await library.ListAsync(ct), LibraryJson.Default.PackageInfoArray);

    [McpServerTool(Name = "iodd_library_remove", Destructive = true), Description("Delete one exact package and metadata from the managed local library. Exported files are unaffected; the package can be downloaded again.")]
    public async Task<string> LibraryRemove(int vendorId, long ioddId, CancellationToken ct = default) =>
        "{\"removedFiles\":" + await library.RemoveAsync(vendorId, ioddId, ct) + "}";

    [McpServerTool(Name = "iodd_library_clear", Destructive = true), Description("Delete all saved packages and metadata from the managed library. Does not delete exports or disable live search. Requires confirm=true.")]
    public async Task<string> LibraryClear(bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm) throw new ArgumentException("Set confirm=true to clear the managed library.");
        return "{\"removedFiles\":" + await library.RemoveAsync(ct: ct) + "}";
    }
}
