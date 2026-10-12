using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Base class for handlers that turn a single strongly-typed content value into a single
/// <see cref="Downloadable"/>. Concrete handlers only implement <see cref="Map"/>.
/// </summary>
/// <typeparam name="T">The content type this handler supports.</typeparam>
internal abstract class TypedDownloadableContentHandler<T> : IDownloadableContentHandler
{
    /// <inheritdoc />
    public virtual float Priority => 0;

    /// <inheritdoc />
    public bool SupportsContent(object content) => content is T;

    /// <inheritdoc />
    public IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken)
    {
        yield return () => new(Map((T)content));
    }

    /// <summary>
    /// Maps the supported content value to a <see cref="Downloadable"/>.
    /// </summary>
    protected abstract Downloadable Map(T content);
}
