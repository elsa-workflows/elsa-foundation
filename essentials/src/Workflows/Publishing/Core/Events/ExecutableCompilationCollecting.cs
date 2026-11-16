using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Events;

/// <summary>
/// Sequential compile-time fan-in event for immutable executable metadata and dependency claims.
/// </summary>
public sealed class ExecutableCompilationCollecting(ExecutableCompilationContext context) : IEvent
{
    public ExecutableCompilationContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>Claims written only by Publishing's single aggregating handler.</summary>
    public ICollection<ExecutableCompilationContribution> Contributions { get; } = [];
}
