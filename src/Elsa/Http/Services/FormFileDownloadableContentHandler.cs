using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a downloadable stream.
/// </summary>
internal sealed class FormFileDownloadableContentHandler : IDownloadableContentHandler
{
    public float Priority => 0;

    /// <inheritdoc />
    public bool SupportsContent(object content) => content is IFormFile;

    public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
    {
        yield return () => new(GetDownloadable(content));
    }

    /// <inheritdoc />
    private static Downloadable GetDownloadable(object content)
    {
        var file = (IFormFile)content;
        var stream = file.OpenReadStream();
        var fileName = Path.GetFileName(file.FileName);
        var contentType = file.ContentType;
        return new Downloadable(stream, fileName, contentType);
    }
}