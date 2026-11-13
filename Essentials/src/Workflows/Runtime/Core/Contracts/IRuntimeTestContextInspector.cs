using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider read-only inspection of supplied setup against one exact native candidate.
/// Inspection grants no execution authority and never creates a test scope, entry or runtime state.
/// Admission and queued start must independently resolve and validate their trusted entry/setup.
/// </summary>
public interface IRuntimeTestContextInspector
{
    ValueTask<RuntimeTestContextPreparation> InspectAsync(RuntimeTestContextInspectionRequest request,
        CancellationToken cancellationToken = default);
}
