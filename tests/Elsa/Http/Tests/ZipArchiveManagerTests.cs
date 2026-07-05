using System.IO.Compression;
using Elsa.Http.Core.Models;
using Elsa.Http.Options;
using Elsa.Http.Services;
using Elsa.Http.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Elsa.Http.Tests;

public class ZipArchiveManagerTests : IDisposable
{
    private readonly string cacheDirectory = Path.Combine(Path.GetTempPath(), $"elsa-http-tests-{Guid.NewGuid():N}");
    private readonly ZipArchiveManager manager;

    public ZipArchiveManagerTests()
    {
        Directory.CreateDirectory(cacheDirectory);
        var options = MsOptions.Create(new HttpZipFileCacheOptions { LocalCacheDirectory = cacheDirectory });
        manager = new(new FakeSystemClock(), new UnusedZipFileCacheStorageProviders(), options, NullLogger<ZipArchiveManager>.Instance);
    }

    [Fact]
    public async Task CreateAsync_DisposesDownloadableStreamsAfterZipping()
    {
        var firstStream = new MemoryStream([1, 2, 3]);
        var secondStream = new MemoryStream([4, 5, 6]);

        using var archive = await CreateArchiveAsync(
            new Downloadable(firstStream, "first.bin", "application/octet-stream"),
            new Downloadable(secondStream, "second.bin", "application/octet-stream"));

        Assert.False(firstStream.CanRead, "The first downloadable's stream was not disposed after zipping.");
        Assert.False(secondStream.CanRead, "The second downloadable's stream was not disposed after zipping.");
    }

    [Fact]
    public async Task CreateAsync_WritesDownloadableContentIntoZipEntries()
    {
        var content = new byte[] { 7, 8, 9 };

        using var archive = await CreateArchiveAsync(new Downloadable(new MemoryStream(content), "file.bin", "application/octet-stream"));

        using var zip = new ZipArchive(archive.Stream, ZipArchiveMode.Read);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("file.bin", entry.Name);
        using var entryStream = entry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        Assert.Equal(content, buffer.ToArray());
    }

    private Task<ZipFileArchive> CreateArchiveAsync(params Downloadable[] downloadables) =>
        manager.CreateAsync(
            downloadables.Select(Func<ValueTask<Downloadable>> (d) => () => new ValueTask<Downloadable>(d)).ToList(),
            cache: false,
            downloadCorrelationId: null,
            downloadAsFilename: "download.zip",
            contentType: "application/zip",
            CancellationToken.None);

    public void Dispose() => Directory.Delete(cacheDirectory, true);
}
