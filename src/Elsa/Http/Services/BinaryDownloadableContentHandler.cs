using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a downloadable binary file.
/// </summary>
internal sealed class BinaryDownloadableContentHandler : IDownloadableContentHandler
{
    public float Priority => 0;

    public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
    {
        yield return () => new(GetDownloadable(content));
    }

    /// <inheritdoc />
    public bool SupportsContent(object content) => content is byte[];

    /// <inheritdoc />
    private static Downloadable GetDownloadable(object content)
    {
        var bytes = (byte[])content;
        var stream = new MemoryStream(bytes);
        var fileName = "file.bin";
        var contentType = "application/octet-stream";
        return new(stream, fileName, contentType);
    }
}