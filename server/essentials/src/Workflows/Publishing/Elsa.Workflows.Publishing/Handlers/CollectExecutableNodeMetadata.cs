using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;

namespace Elsa.Workflows.Publishing.Handlers;

/// <summary>
/// Compatibility handler retained for callers that explicitly publish the superseded metadata event.
/// The Publishing feature registers <see cref="CollectExecutableCompilation"/> as its single active fan-in handler.
/// </summary>
public sealed class CollectExecutableNodeMetadata(IEnumerable<IExecutableNodeMetadataSource> sources)
    : IEventHandler<ExecutableNodeMetadataCollecting>
{
    private readonly IExecutableNodeMetadataSource[] _sources = sources
        .OrderBy(source => source.GetType().FullName, StringComparer.Ordinal)
        .ToArray();

    public async Task Handle(ExecutableNodeMetadataCollecting domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        foreach (var source in _sources)
        {
            var sourceIdentity = source.GetType().FullName ?? source.GetType().Name;
            var contributions = await source.GetMetadataAsync(domainEvent.Context, cancellationToken)
                ?? throw new ArgumentException($"Executable metadata source '{sourceIdentity}' returned null.");

            foreach (var contribution in contributions)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                domainEvent.Contributions.Add(contribution with { SourceIdentity = sourceIdentity });
            }
        }
    }
}
