using Elsa.Http.Core.Models;
using Xunit;

namespace Elsa.Http.Tests;

public class ZipFileArchiveTests
{
    [Fact]
    public void Dispose_DisposesStreamBeforeInvokingCleanup()
    {
        // The cleanup callback typically deletes the backing temp file, which on Windows
        // fails with IOException while the stream still holds an open handle. The archive
        // must therefore dispose its stream before running cleanup.
        var stream = new MemoryStream();
        var streamWasDisposedWhenCleanupRan = false;
        var archive = new ZipFileArchive("download.zip", "application/zip", stream, () => streamWasDisposedWhenCleanupRan = !stream.CanRead);

        archive.Dispose();

        Assert.True(streamWasDisposedWhenCleanupRan, "onCleanup ran while the stream was still open; Dispose must close the stream first.");
    }

    [Fact]
    public void Dispose_WithFileBackedStreamAndDeletingCleanup_DeletesFileWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(path, [1, 2, 3]);
        var archive = new ZipFileArchive("download.zip", "application/zip", File.OpenRead(path), () => File.Delete(path));

        archive.Dispose();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Dispose_WithoutCleanup_DisposesStream()
    {
        var stream = new MemoryStream();
        var archive = new ZipFileArchive("download.zip", "application/zip", stream, null);

        archive.Dispose();

        Assert.False(stream.CanRead);
    }
}
