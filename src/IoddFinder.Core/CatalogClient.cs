using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace IoddFinder.Core;

public sealed record SearchQuery(
    string? VendorName = null, int? VendorId = null, string? ProductName = null,
    string? ProductId = null, int? DeviceId = null, string? IoLinkRevision = null,
    bool AllVersions = false, int Page = 0, int Size = 20, bool Exact = false, string? Sort = null);

/// <summary>Live upstream reads only. No catalog or viewer responses are persisted.</summary>
public sealed class CatalogClient(HttpClient http, Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
{
    public static readonly Uri PublicApi = new("https://ioddfinder.io-link.com/api/");
    private const int MaxJsonBytes = 8 * 1024 * 1024;
    public const int MaxZipBytes = 32 * 1024 * 1024;

    public Task<JsonNode> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(query.Page);
        if (query.Size is < 1 or > 100) throw new ArgumentException("Page size must be between 1 and 100.");
        if (query.IoLinkRevision is not (null or "1.0" or "1.1"))
            throw new ArgumentException("IO-Link revision must be 1.0 or 1.1.");
        ValidateIdentity(query.VendorId, query.DeviceId);
        var values = new List<KeyValuePair<string, string>>
        {
            new("status", "APPROVED"), new("status", "UPLOADED"),
            new("page", query.Page.ToString(CultureInfo.InvariantCulture)),
            new("size", query.Size.ToString(CultureInfo.InvariantCulture))
        };
        void Add(string key, object? value)
        {
            if (value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
                values.Add(new(key, Convert.ToString(value, CultureInfo.InvariantCulture)!));
        }
        string? Text(string? value) => query.Exact && !string.IsNullOrWhiteSpace(value)
            ? '"' + value.Trim('"') + '"' : value;
        Add("vendorName", Text(query.VendorName));
        Add("vendorId", query.VendorId);
        Add("productName", Text(query.ProductName));
        Add("productId", Text(query.ProductId));
        Add("deviceId", query.DeviceId);
        Add("ioLinkRev", query.IoLinkRevision);
        if (query.Sort is not null)
        {
            var sort = query.Sort.Split(',');
            if (sort.Length != 2 || sort[0] is not ("vendorName" or "productName" or "productId" or "deviceId" or "ioLinkRev") ||
                sort[1].ToUpperInvariant() is not ("ASC" or "DESC"))
                throw new ArgumentException("Sort must be field,ASC or field,DESC; fields: vendorName, productName, productId, deviceId, ioLinkRev.");
            Add("sort", sort[0] + "," + sort[1].ToUpperInvariant());
        }
        if (query.AllVersions) Add("allVersions", "true");
        return GetPageAsync("drivers?" + Encode(values), ct);
    }

    public Task<JsonNode> SuggestAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200)
            throw new ArgumentException("Search text must contain 1 to 200 characters.");
        return GetJsonAsync("entrypage/search?search=" + Uri.EscapeDataString(text), ct);
    }

    public Task<JsonNode> VendorsAsync(CancellationToken ct = default) => GetJsonAsync("drivers/vendors", ct);

    public Task<JsonNode> DetailsAsync(int variantId, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(variantId);
        return GetJsonAsync($"productvariants/{variantId}", ct);
    }

    public Task<JsonNode> ViewerAsync(int vendorId, int deviceId, string revision, CancellationToken ct = default)
    {
        ValidateIdentity(vendorId, deviceId);
        if (revision is not ("1.0" or "1.1")) throw new ArgumentException("IO-Link revision must be 1.0 or 1.1.");
        return GetJsonAsync($"productvariants/{vendorId}/{deviceId}/viewer?version={revision}", ct);
    }

    public async Task<byte[]> DownloadAsync(int vendorId, long ioddId, CancellationToken ct = default)
    {
        ValidateIdentity(vendorId, null);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ioddId);
        return await GetBytesAsync($"vendors/{vendorId}/iodds/{ioddId}/files/zip/rated", MaxZipBytes, ct);
    }

    private async Task<JsonNode> GetJsonAsync(string path, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(path, MaxJsonBytes, ct);
        return JsonNode.Parse(bytes) ?? throw new InvalidDataException("IODDfinder returned an empty JSON document.");
    }

    private async Task<JsonNode> GetPageAsync(string path, CancellationToken ct)
    {
        var page = await GetJsonAsync(path, ct);
        if (page is not JsonObject || page["content"] is not JsonArray ||
            page["totalElements"] is not JsonValue total || !total.TryGetValue<long>(out _))
            throw new InvalidDataException("IODDfinder returned an unexpected catalog response; its API may have changed.");
        return page;
    }

    private async Task<byte[]> GetBytesAsync(string path, int maxBytes, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        ct = timeout.Token;
        using var response = await SendWithRetryAsync(path, ct);
        if (!response.IsSuccessStatusCode)
        {
            var hint = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => " Rate limited; try again later, respecting the service's Retry-After header.",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => " Upstream denied access.",
                HttpStatusCode.NotFound => " The requested upstream entry was not found.",
                _ => ""
            };
            throw new HttpRequestException($"IODDfinder returned HTTP {(int)response.StatusCode}.{hint}", null, response.StatusCode);
        }
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException($"Upstream response exceeds the {maxBytes}-byte limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + count > maxBytes) throw new InvalidDataException("Upstream response is too large.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return output.ToArray();
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.UserAgent.ParseAdd("IoddFinderAgent/0.1.1");
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (HttpRequestException) when (attempt < 2)
            {
                await DelayAsync(TimeSpan.FromMilliseconds(300 * (attempt + 1)), ct);
                continue;
            }
            var transient = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
            if (!transient || attempt >= 2) return response;
            var retryAfter = response.Headers.RetryAfter;
            var delay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow) ??
                TimeSpan.FromMilliseconds(300 * (attempt + 1) + Random.Shared.Next(50, 200));
            // Do not retry earlier than requested or tie up the caller for long delays.
            if (delay > TimeSpan.FromSeconds(5)) return response;
            response.Dispose();
            await DelayAsync(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, ct);
        }
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        retryDelay is null ? Task.Delay(delay, ct) : retryDelay(delay, ct);

    private static string Encode(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join('&', values.Select(x => Uri.EscapeDataString(x.Key) + '=' + Uri.EscapeDataString(x.Value)));

    private static void ValidateIdentity(int? vendorId, int? deviceId)
    {
        if (vendorId is < 0 or > 65535) throw new ArgumentException("Vendor ID must be between 0 and 65535.");
        if (deviceId is < 0 or > 16777215) throw new ArgumentException("Device ID must be between 0 and 16777215.");
    }
}
