using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a downloadable stream.
/// </summary>
internal sealed class StreamDownloadableContentHandler : TypedDownloadableContentHandler<Stream>
{
    /// <inheritdoc />
    protected override Downloadable Map(Stream content) => new(content, "file.bin", "application/octet-stream");
}
