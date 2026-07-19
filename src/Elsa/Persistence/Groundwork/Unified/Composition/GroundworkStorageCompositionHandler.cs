using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Groundwork.Composition;

namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// Single aggregating handler for the host-selected Groundwork manifest sources. Sources are invoked
/// once in stable feature-identity order; the context is frozen only after every contribution succeeds.
/// </summary>
public sealed class GroundworkStorageCompositionHandler : IEventHandler<OnGroundworkStorageComposing>
{
    private readonly IReadOnlyList<IGroundworkStorageManifestSource> sources;

    public GroundworkStorageCompositionHandler(IEnumerable<IGroundworkStorageManifestSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var sourceCopy = sources.ToArray();
        if (sourceCopy.Any(source => source is null))
            throw new ArgumentException("Groundwork storage manifest sources cannot contain null entries.", nameof(sources));

        this.sources = Array.AsReadOnly(sourceCopy
            .OrderBy(source => source.FeatureIdentity, StringComparer.Ordinal)
            .ToArray());
    }

    public async Task Handle(
        OnGroundworkStorageComposing domainEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var declaration = await source.CreateDeclarationAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            domainEvent.Context.Add(declaration);
        }

        domainEvent.Context.Freeze();
    }
}
