using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a downloadable <see cref="HttpFile"/>.
/// </summary>
internal sealed class HttpFileDownloadableContentHandler : TypedDownloadableContentHandler<HttpFile>
{
    /// <inheritdoc />
    protected override Downloadable Map(HttpFile content) => new(content.Stream, content.Filename, content.ContentType);
}
