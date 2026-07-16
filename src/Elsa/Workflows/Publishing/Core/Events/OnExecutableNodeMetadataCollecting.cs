using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Events;

/// <summary>Sequential compile-time fan-in event for executable-node metadata claims.</summary>
public sealed class OnExecutableNodeMetadataCollecting(ExecutableNodeMetadataContext context) : IEvent
{
    public ExecutableNodeMetadataContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>Claims written only by the owning domain's single aggregating handler.</summary>
    public ICollection<ExecutableNodeMetadataContribution> Contributions { get; } = [];
}
