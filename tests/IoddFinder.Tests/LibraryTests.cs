using System.IO.Compression;
using System.Net;
using System.Text;
using IoddFinder.Core;
using Xunit;

namespace IoddFinder.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ioddfinder-tests-" + Guid.NewGuid().ToString("N"));

    private static byte[] Package()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("example-IODD1.1.xml").Open());
            writer.Write("<IODevice/>");
        }
        return stream.ToArray();
    }

    private static (HttpClient Http, CatalogClient Client) Client(Action? called = null, byte[]? data = null)
    {
        var http = new HttpClient(new Handler(request =>
        {
            called?.Invoke();
            Assert.EndsWith("/files/zip/rated", request.RequestUri!.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data ?? Package()) };
        })) { BaseAddress = CatalogClient.PublicApi };
        return (http, new CatalogClient(http));
    }

    [Fact]
    public async Task EmptyLibraryAndTransientDownloadCreateNoLocalState()
    {
        var library = new DownloadLibrary(Path.Combine(root, "library"));
        Assert.Empty(await library.ListAsync());
        using var http = Client().Http;
        var result = await library.DownloadAsync(new CatalogClient(http), 310, 399, save: false);
        Assert.Null(result.Path);
        Assert.Single(result.Package.Files);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task ExactPackagesAreReusedButRefreshAndCorruptionFetchAgain()
    {
        var calls = 0;
        var (http, client) = Client(() => calls++);
        using var owner = http;
        var library = new DownloadLibrary(root);
        var first = await library.DownloadAsync(client, 310, 399);
        Assert.Equal("network", first.Source);
        Assert.Equal("library", (await library.DownloadAsync(client, 310, 399)).Source);
        Assert.Equal(1, calls);
        await library.DownloadAsync(client, 310, 399, refresh: true);
        Assert.Equal(2, calls);
        await File.WriteAllTextAsync(first.Path!, "corrupted package");
        Assert.Equal("network", (await library.DownloadAsync(client, 310, 399)).Source);
        Assert.Equal(3, calls);
        Assert.Single(await library.ListAsync());
    }

    [Fact]
    public async Task ClearOnlyRemovesManagedPackagesAndLeavesExportsAndOtherFiles()
    {
        var (http, client) = Client();
        using var owner = http;
        var library = new DownloadLibrary(Path.Combine(root, "library"));
        Directory.CreateDirectory(root);
        var export = Path.Combine(root, "export.zip");
        await library.DownloadAsync(client, 310, 399, output: export);
        var unrelated = Path.Combine(library.DirectoryPath, "user-notes.txt");
        await File.WriteAllTextAsync(unrelated, "keep");
        Assert.Equal(2, await library.RemoveAsync());
        Assert.Empty(await library.ListAsync());
        Assert.True(File.Exists(export));
        Assert.True(File.Exists(unrelated));
        Assert.Equal("network", (await library.DownloadAsync(client, 310, 399)).Source);
    }

    [Fact]
    public async Task ExportNeverOverwritesAnExistingFile()
    {
        var (http, client) = Client();
        using var owner = http;
        Directory.CreateDirectory(root);
        var export = Path.Combine(root, "export.zip");
        await File.WriteAllTextAsync(export, "keep");
        await Assert.ThrowsAsync<IOException>(() => new DownloadLibrary(Path.Combine(root, "library"))
            .DownloadAsync(client, 310, 399, save: false, output: export));
        Assert.Equal("keep", await File.ReadAllTextAsync(export));
    }

    [Fact]
    public async Task ExportInsideManagedLibraryIsRejectedBeforeDownloading()
    {
        var (http, client) = Client(() => throw new Exception("Must reject before network access"));
        using var owner = http;
        var library = new DownloadLibrary(root);
        await Assert.ThrowsAsync<ArgumentException>(() => library.DownloadAsync(client, 310, 399,
            output: Path.Combine(root, "my-export.zip")));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task InvalidUpstreamPackageIsNeverSaved()
    {
        var (http, client) = Client(data: Encoding.UTF8.GetBytes("<html>server error</html>"));
        using var owner = http;
        var library = new DownloadLibrary(root);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.DownloadAsync(client, 310, 399));
        Assert.Empty(await library.ListAsync());
        Assert.Empty(Directory.EnumerateFiles(root, "*.zip"));
    }

    [Fact]
    public async Task ConcurrentInstancesReuseOneDownload()
    {
        var calls = 0;
        var (http, client) = Client(() => Interlocked.Increment(ref calls));
        using var owner = http;
        var results = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => new DownloadLibrary(root).DownloadAsync(client, 310, 399)));
        Assert.Equal(1, calls);
        Assert.Single(results, x => x.Source == "network");
    }

    public void Dispose()
    {
        // Only the exact, uniquely named temporary directory owned by this test.
        var absolute = Path.GetFullPath(root);
        var temporary = Path.GetFullPath(Path.GetTempPath());
        if (!absolute.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(absolute).StartsWith("ioddfinder-tests-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test cleanup directory.");
        if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true);
    }
}
