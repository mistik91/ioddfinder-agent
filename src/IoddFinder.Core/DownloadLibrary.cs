using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IoddFinder.Core;

public sealed record PackageInfo(int VendorId, long IoddId, string Sha256, long Bytes,
    DateTimeOffset DownloadedAt, string[] Files);
public sealed record DownloadResult(PackageInfo Package, string Source, string? Path);

/// <summary>Explicitly saved packages; never a copy of the remote catalog.</summary>
public sealed partial class DownloadLibrary(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public static string DefaultDirectory => Environment.GetEnvironmentVariable("IODD_LIBRARY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IoddFinder", "library");

    public async Task<DownloadResult> DownloadAsync(CatalogClient client, int vendorId, long ioddId,
        bool save = true, bool refresh = false, string? output = null, CancellationToken ct = default)
    {
        if (vendorId is < 0 or > 65535 || ioddId <= 0) throw new ArgumentException("Invalid vendor or IODD ID.");
        if (output is not null)
        {
            var absoluteOutput = Path.GetFullPath(output);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (absoluteOutput.Equals(DirectoryPath, comparison) ||
                absoluteOutput.StartsWith(Path.TrimEndingDirectorySeparator(DirectoryPath) + Path.DirectorySeparatorChar, comparison))
                throw new ArgumentException("Export outside the managed library so cleanup cannot remove the export.");
            if (File.Exists(absoluteOutput)) throw new IOException("The export file already exists; choose a new path.");
        }
        // Transient requests never create a library directory or a lock file.
        if (!save)
        {
            var transient = await client.DownloadAsync(vendorId, ioddId, ct);
            var info = Describe(transient, vendorId, ioddId);
            if (output is not null) await ExportAsync(transient, output, ct);
            return new(info, "network", output is null ? null : Path.GetFullPath(output));
        }
        await using var libraryLock = await LockAsync(ct);
        var zipPath = FilePath(vendorId, ioddId, ".zip");
        var infoPath = FilePath(vendorId, ioddId, ".json");
        byte[] bytes;
        PackageInfo? metadata = null;
        if (!refresh && File.Exists(infoPath) && File.Exists(zipPath))
        {
            try
            {
                EnsureRegularFile(infoPath);
                EnsureRegularFile(zipPath);
                metadata = JsonSerializer.Deserialize(await File.ReadAllTextAsync(infoPath, ct), LibraryJson.Default.PackageInfo);
                if (metadata?.VendorId != vendorId || metadata.IoddId != ioddId ||
                    new FileInfo(zipPath).Length > CatalogClient.MaxZipBytes) metadata = null;
                if (metadata is not null)
                {
                    bytes = await File.ReadAllBytesAsync(zipPath, ct);
                    if (Hash(bytes) == metadata.Sha256)
                    {
                        if (output is not null) await ExportAsync(bytes, output, ct);
                        return new(metadata, "library", zipPath);
                    }
                }
            }
            catch (JsonException) { /* Invalid local metadata is replaced by a fresh package. */ }
        }
        bytes = await client.DownloadAsync(vendorId, ioddId, ct);
        metadata = Describe(bytes, vendorId, ioddId);
        await AtomicWriteAsync(zipPath, bytes, ct);
        await AtomicWriteAsync(infoPath, JsonSerializer.SerializeToUtf8Bytes(metadata, LibraryJson.Default.PackageInfo), ct);
        if (output is not null) await ExportAsync(bytes, output, ct);
        return new(metadata, "network", zipPath);
    }

    public async Task<PackageInfo[]> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(DirectoryPath)) return [];
        await using var libraryLock = await LockAsync(ct);
        var result = new List<PackageInfo>();
        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            if (!ManagedName().IsMatch(Path.GetFileName(file))) continue;
            EnsureRegularFile(file);
            var entry = JsonSerializer.Deserialize(await File.ReadAllTextAsync(file, ct), LibraryJson.Default.PackageInfo);
            if (entry is not null && Path.GetFileName(file) == $"{entry.VendorId}-{entry.IoddId}.json") result.Add(entry);
        }
        return result.OrderBy(x => x.VendorId).ThenBy(x => x.IoddId).ToArray();
    }

    public async Task<int> RemoveAsync(int? vendorId = null, long? ioddId = null, CancellationToken ct = default)
    {
        if (vendorId.HasValue != ioddId.HasValue) throw new ArgumentException("Supply both vendor ID and IODD ID.");
        if (vendorId is < 0 or > 65535 || ioddId is <= 0) throw new ArgumentException("Invalid vendor or IODD ID.");
        if (!Directory.Exists(DirectoryPath)) return 0;
        await using var libraryLock = await LockAsync(ct);
        var prefix = vendorId.HasValue ? $"{vendorId}-{ioddId}" : null;
        var files = Directory.EnumerateFiles(DirectoryPath).Where(f => ManagedName().IsMatch(Path.GetFileName(f)))
            .Where(f => prefix is null || Path.GetFileNameWithoutExtension(f) == prefix).ToArray();
        foreach (var file in files) EnsureRegularFile(file);
        foreach (var file in files) File.Delete(file);
        return files.Length;
    }

    private async Task<FileStream> LockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(DirectoryPath);
        if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The managed library directory cannot be a symbolic link or junction.");
        var lockPath = Path.Combine(DirectoryPath, ".library.lock");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(lockPath)) EnsureRegularFile(lockPath);
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline) { await Task.Delay(100, ct); }
        }
    }

    private string FilePath(int vendorId, long ioddId, string extension) => Path.Combine(DirectoryPath, $"{vendorId}-{ioddId}{extension}");
    private static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    private static PackageInfo Describe(byte[] data, int vendorId, long ioddId)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        if (zip.Entries.Count is 0 or > 1000 || zip.Entries.Any(e => e.Length > 128L * 1024 * 1024) ||
            zip.Entries.Sum(e => e.Length) > 128L * 1024 * 1024)
            throw new InvalidDataException("Package is empty or exceeds the ZIP entry/expanded-size limit.");
        if (!zip.Entries.Any(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Package contains no IODD XML files.");
        return new(vendorId, ioddId, Hash(data), data.LongLength, DateTimeOffset.UtcNow, zip.Entries.Select(e => e.FullName).ToArray());
    }

    private static void EnsureRegularFile(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Managed library files cannot be symbolic links.");
    }

    private static async Task AtomicWriteAsync(string path, byte[] data, CancellationToken ct)
    {
        if (File.Exists(path)) EnsureRegularFile(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, data, ct);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task ExportAsync(byte[] data, string output, CancellationToken ct)
    {
        // An export never overwrites an existing file, and library cleanup never visits it.
        await using var file = new FileStream(Path.GetFullPath(output), FileMode.CreateNew, FileAccess.Write);
        await file.WriteAsync(data, ct);
    }

    [GeneratedRegex(@"^\d{1,5}-\d{1,19}\.(json|zip)$", RegexOptions.CultureInvariant)]
    private static partial Regex ManagedName();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PackageInfo))]
[JsonSerializable(typeof(PackageInfo[]))]
[JsonSerializable(typeof(DownloadResult))]
public partial class LibraryJson : JsonSerializerContext;
