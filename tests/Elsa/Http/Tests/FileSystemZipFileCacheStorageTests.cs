using Elsa.Http.Options;
using Elsa.Http.Services;
using Elsa.Http.Tests.Support;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Elsa.Http.Tests;

public class FileSystemZipFileCacheStorageTests : IDisposable
{
    private readonly string cacheDirectory = Path.Combine(Path.GetTempPath(), $"elsa-http-tests-{Guid.NewGuid():N}");
    private readonly FileSystemZipFileCacheStorage storage;

    public FileSystemZipFileCacheStorageTests()
    {
        Directory.CreateDirectory(cacheDirectory);
        var options = MsOptions.Create(new HttpZipFileCacheOptions { LocalCacheDirectory = cacheDirectory });
        storage = new(options, new JsonPayloadSerializer());
    }

    [Fact]
    public async Task Read_ReturnsStreamAtPositionZero_WithFullContent()
    {
        var content = new byte[] { 1, 2, 3, 4, 5 };
        await storage.Write("cached.tmp", new MemoryStream(content));

        var result = await storage.Read("cached.tmp");

        Assert.Equal(0, result.Position);
        Assert.Equal(content, ReadAllBytes(result));
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        // Deliberately no Seek: consumers such as ZipArchiveManager.LoadAsync hand this
        // stream straight to the HTTP response, which reads from the current position.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public void Dispose() => Directory.Delete(cacheDirectory, true);
}
