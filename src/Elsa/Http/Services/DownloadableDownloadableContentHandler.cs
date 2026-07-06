using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that already is a <see cref="Downloadable"/>.
/// </summary>
internal sealed class DownloadableDownloadableContentHandler : TypedDownloadableContentHandler<Downloadable>
{
    /// <inheritdoc />
    protected override Downloadable Map(Downloadable content) => content;
}
