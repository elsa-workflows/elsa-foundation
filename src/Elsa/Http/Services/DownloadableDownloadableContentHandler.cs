using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a downloadable stream.
/// </summary>
internal sealed class DownloadableDownloadableContentHandler : IDownloadableContentHandler
{
    public float Priority => 0;

    /// <inheritdoc />
    public bool SupportsContent(object content) => content is Downloadable;

    public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
    {
        yield return () => new((Downloadable)content);
    }
}