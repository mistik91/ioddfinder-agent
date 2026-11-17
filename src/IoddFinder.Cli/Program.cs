using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using IoddFinder.Cli;
using IoddFinder.Core;

var root = new RootCommand("Live IODDfinder CLI and MCP server. No local catalog or initial sync required.");
var libraryOption = new Option<string?>("--library") { Description = "Managed download directory (or IODD_LIBRARY).", Recursive = true };
root.Options.Add(libraryOption);
using var http = new HttpClient { BaseAddress = CatalogClient.PublicApi, Timeout = TimeSpan.FromSeconds(30) };
var client = new CatalogClient(http);
DownloadLibrary Library(ParseResult p) => new(p.GetValue(libraryOption) ?? DownloadLibrary.DefaultDirectory);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
void Print(JsonNode node) => Console.WriteLine(node.ToJsonString(jsonOptions));

void Action(Command command, Func<ParseResult, CancellationToken, Task> handler)
{
    command.SetAction(async (p, ct) =>
    {
        try { await handler(p, ct); return 0; }
        catch (OperationCanceledException) { Console.Error.WriteLine("{\"error\":\"Operation cancelled or timed out.\"}"); return 1; }
        catch (Exception ex) when (ex is ArgumentException or HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(new JsonObject { ["error"] = ex.Message }.ToJsonString());
            return 1;
        }
    });
}

Option<T> Add<T>(Command command, string name, string description, bool required = false)
{
    var option = new Option<T>(name) { Description = description, Required = required };
    command.Options.Add(option);
    return option;
}
Option<int> Number(Command command, string name, string description, int defaultValue)
{
    var option = Add<int>(command, name, description);
    option.DefaultValueFactory = _ => defaultValue;
    return option;
}

var search = new Command("search", "Search the live catalog; combine filters. No filter browses a page.");
var vendor = Add<string?>(search, "--vendor", "Manufacturer name.");
var vendorId = Add<int?>(search, "--vendor-id", "Numeric vendor ID.");
var product = Add<string?>(search, "--product", "Product name/type.");
var article = Add<string?>(search, "--article", "Article/product number.");
var device = Add<int?>(search, "--device-id", "Numeric device ID.");
var revision = Add<string?>(search, "--revision", "IO-Link revision, e.g. 1.1.");
var exact = Add<bool>(search, "--exact", "Match text filters exactly.");
var all = Add<bool>(search, "--all-versions", "Include historical releases.");
var page = Number(search, "--page", "Zero-based page.", 0);
var size = Number(search, "--size", "Rows per page (1..100).", 20);
var sort = Add<string?>(search, "--sort", "field,ASC or field,DESC; e.g. productName,ASC.");
Action(search, async (p, ct) => Print(await client.SearchAsync(new(p.GetValue(vendor), p.GetValue(vendorId),
    p.GetValue(product), p.GetValue(article), p.GetValue(device), p.GetValue(revision),
    p.GetValue(all), p.GetValue(page), p.GetValue(size), p.GetValue(exact), p.GetValue(sort)), ct)));
root.Subcommands.Add(search);

var suggest = new Command("suggest", "Get categorized live search suggestions.");
var text = Add<string>(suggest, "--text", "Search text.", true);
Action(suggest, async (p, ct) => Print(await client.SuggestAsync(p.GetValue(text)!, ct)));
root.Subcommands.Add(suggest);

var vendors = new Command("vendors", "List live manufacturer names.");
Action(vendors, async (_, ct) => Print(await client.VendorsAsync(ct)));
root.Subcommands.Add(vendors);

var details = new Command("details", "Get details for a product variant from a search result.");
var variant = Add<int>(details, "--variant-id", "Product variant ID.", true);
Action(details, async (p, ct) => Print(await client.DetailsAsync(p.GetValue(variant), ct)));
root.Subcommands.Add(details);

var versions = new Command("versions", "List available revisions; do not assume the first is newest.");
var vv = Add<int>(versions, "--vendor-id", "Vendor ID.", true);
var vd = Add<int>(versions, "--device-id", "Device ID.", true);
var vp = Add<string?>(versions, "--product", "Exact product name to narrow variants.");
var vr = Add<string?>(versions, "--revision", "IO-Link revision.");
var vpage = Number(versions, "--page", "Zero-based page.", 0);
var vsize = Number(versions, "--size", "Rows per page (1..100).", 20);
Action(versions, async (p, ct) => Print(await client.SearchAsync(new(VendorId: p.GetValue(vv), DeviceId: p.GetValue(vd),
    ProductName: p.GetValue(vp), IoLinkRevision: p.GetValue(vr), AllVersions: true, Exact: true,
    Page: p.GetValue(vpage), Size: p.GetValue(vsize)), ct)));
root.Subcommands.Add(versions);

var inspect = new Command("inspect", "Inspect live viewer sections or parameters; cannot select historical IODD releases.");
var iv = Add<int>(inspect, "--vendor-id", "Vendor ID.", true);
var id = Add<int>(inspect, "--device-id", "Device ID.", true);
var ir = Add<string>(inspect, "--revision", "IO-Link revision, not IODD release.");
ir.DefaultValueFactory = _ => "1.1";
var section = Add<string?>(inspect, "--section", "Exact viewer section name or ID; omit to list sections.");
var query = Add<string?>(inspect, "--query", "Filter names, descriptions, IDs or indexes.");
var offset = Number(inspect, "--offset", "Zero-based item offset.", 0);
var limit = Number(inspect, "--limit", "Items per response (1..100).", 20);
Action(inspect, async (p, ct) => Print(ViewerInspection.Select(await client.ViewerAsync(p.GetValue(iv), p.GetValue(id),
    p.GetValue(ir)!, ct), p.GetValue(iv), p.GetValue(id), p.GetValue(ir)!, p.GetValue(section), p.GetValue(query),
    p.GetValue(offset), p.GetValue(limit))));
root.Subcommands.Add(inspect);

var download = new Command("download", "Download an exact package, optionally saving/reusing it in the library.");
var dv = Add<int>(download, "--vendor-id", "Vendor ID.", true);
var di = Add<long>(download, "--iodd-id", "Exact IODD ID from a search result.", true);
var noSave = Add<bool>(download, "--no-save", "Keep no library copy; returns metadata unless --output is supplied.");
var refresh = Add<bool>(download, "--refresh", "Force downloading even if this exact package is saved.");
var output = Add<string?>(download, "--output", "Export a ZIP to a new file; never overwrites an existing file.");
Action(download, async (p, ct) => Console.WriteLine(JsonSerializer.Serialize(await Library(p).DownloadAsync(client,
    p.GetValue(dv), p.GetValue(di), !p.GetValue(noSave), p.GetValue(refresh), p.GetValue(output), ct), LibraryJson.Default.DownloadResult)));
root.Subcommands.Add(download);

var library = new Command("library", "Manage saved downloads. Live catalog access works with an empty library.");
var list = new Command("list", "List saved packages.");
Action(list, async (p, ct) => Console.WriteLine(JsonSerializer.Serialize(await Library(p).ListAsync(ct), LibraryJson.Default.PackageInfoArray)));
library.Subcommands.Add(list);
var remove = new Command("remove", "Remove one saved package; exports are unaffected.");
var rv = Add<int>(remove, "--vendor-id", "Vendor ID.", true);
var ri = Add<long>(remove, "--iodd-id", "IODD ID.", true);
Action(remove, async (p, ct) => Print(new JsonObject { ["removedFiles"] = await Library(p).RemoveAsync(p.GetValue(rv), p.GetValue(ri), ct) }));
library.Subcommands.Add(remove);
var clear = new Command("clear", "Remove all managed downloads, leaving exports untouched.");
Action(clear, async (p, ct) => Print(new JsonObject { ["removedFiles"] = await Library(p).RemoveAsync(ct: ct) }));
library.Subcommands.Add(clear);
root.Subcommands.Add(library);

var mcp = new Command("mcp", "Run the MCP server over stdio. Protocol output uses stdout; diagnostics use stderr.");
Action(mcp, (p, ct) => McpHost.RunAsync(client, Library(p), ct));
root.Subcommands.Add(mcp);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
return await root.Parse(args).InvokeAsync(cancellationToken: cancellation.Token);
