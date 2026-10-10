using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a downloadable binary file.
/// </summary>
internal sealed class BinaryDownloadableContentHandler : TypedDownloadableContentHandler<byte[]>
{
    /// <inheritdoc />
    protected override Downloadable Map(byte[] content) => new(new MemoryStream(content), "file.bin", "application/octet-stream");
}
