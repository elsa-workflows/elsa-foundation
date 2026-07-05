namespace Elsa.Http.Core.Models;

public sealed class ZipFileArchive(string fileName, string contentType, Stream stream, Action? onCleanup) : IDisposable
{
    public string FileName { get; } = fileName;
    public string ContentType { get; } = contentType;
    public Stream Stream { get; } = stream;

    public void Dispose()
    {
        // Dispose the stream before running cleanup: cleanup typically deletes the backing
        // temp file, which fails on Windows while the stream still holds an open handle.
        Stream.Dispose();
        onCleanup?.Invoke();
    }
}