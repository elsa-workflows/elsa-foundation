namespace Elsa.Http.Core.Models;

public sealed class ZipFileArchive(string fileName, string contentType, Stream stream, Action? onCleanup) : IDisposable
{
    public string FileName { get; } = fileName;
    public string ContentType { get; } = contentType;
    public Stream Stream { get; } = stream;

    public void Dispose()
    {
        onCleanup?.Invoke();
        Stream.Dispose();
    }
}