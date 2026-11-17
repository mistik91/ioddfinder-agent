using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using IoddFinder.Core;
using Xunit;

namespace IoddFinder.Tests;

public sealed class ClientTests
{
    [Fact]
    public async Task SearchPreservesCombinedIdentityAndEncodesUserText()
    {
        Uri? requested = null;
        using var http = new HttpClient(new Handler(r =>
        {
            requested = r.RequestUri;
            Assert.Null(r.Headers.Authorization);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"content\":[],\"totalElements\":0}") };
        })) { BaseAddress = CatalogClient.PublicApi };
        var response = await new CatalogClient(http).SearchAsync(new(VendorName: "A&B + Sensors", VendorId: 310,
            DeviceId: 706, ProductName: "TN 24/05", Exact: true, Page: 2, Size: 3, AllVersions: true, Sort: "productName,asc"));
        Assert.Equal(0, response["totalElements"]!.GetValue<int>());
        var query = requested!.Query;
        Assert.Contains("vendorName=%22A%26B%20%2B%20Sensors%22", query);
        Assert.Contains("productName=%22TN%2024%2F05%22", query);
        Assert.Contains("vendorId=310", query);
        Assert.Contains("deviceId=706", query);
        Assert.Contains("page=2", query);
        Assert.Contains("size=3", query);
        Assert.Contains("status=APPROVED&status=UPLOADED", query);
        Assert.Contains("allVersions=true", query);
        Assert.Contains("sort=productName%2CASC", query);
    }

    [Fact]
    public async Task InvalidLimitsFailBeforeRequest()
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not call upstream"))) { BaseAddress = CatalogClient.PublicApi };
        var client = new CatalogClient(http);
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync(new(Size: 101)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.SearchAsync(new(VendorId: -1)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ViewerAsync(310, 706, "V1.0.0.2"));
    }

    [Fact]
    public async Task AccessDeniedIsAnErrorRatherThanAnEmptyCatalog()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden))) { BaseAddress = CatalogClient.PublicApi };
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new CatalogClient(http).SearchAsync(new()));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
    }

    [Fact]
    public async Task TransientFailuresHaveBoundedRetriesAndRespectRetryAfter()
    {
        var requests = 0;
        var delays = new List<TimeSpan>();
        using var http = new HttpClient(new Handler(_ =>
        {
            if (++requests == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(2));
                return response;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        })) { BaseAddress = CatalogClient.PublicApi };
        await new CatalogClient(http, (delay, _) => { delays.Add(delay); return Task.CompletedTask; }).VendorsAsync();
        Assert.Equal(2, requests);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(delays));
    }

    [Fact]
    public async Task PersistentUnavailableStopsAfterThreeRequests()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            return new(HttpStatusCode.ServiceUnavailable);
        })) { BaseAddress = CatalogClient.PublicApi };
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new CatalogClient(http, (_, _) => Task.CompletedTask).VendorsAsync());
        Assert.Equal(3, requests);
    }

    [Fact]
    public async Task LongRetryAfterIsNotIgnoredOrRetriedEarly()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(TimeSpan.FromMinutes(1));
            return response;
        })) { BaseAddress = CatalogClient.PublicApi };
        await Assert.ThrowsAsync<HttpRequestException>(() => new CatalogClient(http,
            (_, _) => throw new Exception("Must not retry early")).VendorsAsync());
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task ChangedCatalogSchemaDoesNotMasqueradeAsAnEmptyPage()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
            { Content = new StringContent("{\"message\":\"maintenance\"}") })) { BaseAddress = CatalogClient.PublicApi };
        await Assert.ThrowsAsync<InvalidDataException>(() => new CatalogClient(http).SearchAsync(new()));
    }

    [Fact]
    public async Task OversizeResponseIsRejected()
    {
        using var http = new HttpClient(new Handler(_ =>
        {
            var content = new StringContent("{}");
            content.Headers.ContentLength = 9 * 1024 * 1024;
            return new(HttpStatusCode.OK) { Content = content };
        })) { BaseAddress = CatalogClient.PublicApi };
        await Assert.ThrowsAsync<InvalidDataException>(() => new CatalogClient(http).VendorsAsync());
    }

    [Fact]
    public void InspectionListsSectionsAndPaginatesNestedVariablesWithoutMutatingSource()
    {
        var viewer = JsonNode.Parse("""
            {"menus":[{"id":"params","name":"Parameter","type":"Menu","children":[
              {"id":"group","type":"Menu","children":[
                {"id":"a","name":"Temperature","index":"16","type":"Variable","children":[]},
                {"id":"b","name":"Temperature limit","index":"17","type":"Variable","children":[]}]}]}]}
            """)!;
        var summary = ViewerInspection.Select(viewer, 310, 706, "1.1");
        Assert.Single(summary["sections"]!.AsArray());
        var filtered = ViewerInspection.Select(viewer, 310, 706, "1.1", "Parameter", "temperature", limit: 1);
        Assert.Equal(2, filtered["total"]!.GetValue<int>());
        Assert.Equal(1, filtered["nextOffset"]!.GetValue<int>());
        Assert.Null(filtered["items"]![0]!["children"]);
        var second = ViewerInspection.Select(viewer, 310, 706, "1.1", "params", "temperature", offset: 1, limit: 1);
        Assert.Equal("b", second["items"]![0]!["id"]!.GetValue<string>());
        Assert.Null(second["nextOffset"]);
        Assert.NotNull(viewer["menus"]![0]!["children"]![0]!["children"]![0]!["children"]);
    }

    [Fact]
    public void UnknownSectionDoesNotSilentlyReturnUnrelatedParameters()
    {
        Assert.Throws<ArgumentException>(() => ViewerInspection.Select(JsonNode.Parse("{\"menus\":[]}")!, 1, 1, "1.1", "Missing"));
    }
}

internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
}
