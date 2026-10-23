using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Events;

/// <summary>
/// Compatibility event retained for source compatibility. Publishing emits
/// <see cref="ExecutableCompilationCollecting"/> for active compile fan-in.
/// </summary>
public sealed class ExecutableNodeMetadataCollecting(ExecutableNodeMetadataContext context) : IEvent
{
    public ExecutableNodeMetadataContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>Claims written only by the owning domain's single aggregating handler.</summary>
    public ICollection<ExecutableNodeMetadataContribution> Contributions { get; } = [];
}
