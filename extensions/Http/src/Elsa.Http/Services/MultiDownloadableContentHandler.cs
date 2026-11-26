using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a list of downloadable objects.
/// </summary>
public sealed class MultiDownloadableContentHandler(IServiceProvider serviceProvider) : IDownloadableContentHandler
{
    public float Priority => 0;

    /// <inheritdoc />
    public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
    {
        var collectedDownloadables = new List<Func<ValueTask<Downloadable>>>();
        var enumerable = (IEnumerable)content;

        // Order by Priority ascending so that lower-Priority handlers are tried first, per the
        // IDownloadableContentHandler contract ("Providers with lower priority are tried first")
        // and EXTENSION_POINTS.md. Without this, selection followed DI-registration order, so a
        // custom handler could not reliably override a built-in one for the same content type.
        var allHandlers = serviceProvider.GetServices<IDownloadableContentHandler>().OrderBy(x => x.Priority).ToList();
        foreach (var item in enumerable)
        {
            var handler = allHandlers.FirstOrDefault(x => x.SupportsContent(item))
                          ?? throw new InvalidOperationException($"Could not find downloadable content handler for item type '{item.GetType()}'");

            var downloadables = handler.GetDownloadablesAsync(item, cancellationToken);

            collectedDownloadables.AddRange(downloadables);
        }

        return collectedDownloadables;
    }

    /// <inheritdoc />
    public bool SupportsContent(object content) => content is IEnumerable and not string and not byte[];
}