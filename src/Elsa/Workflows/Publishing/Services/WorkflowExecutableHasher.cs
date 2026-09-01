using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Backward-compatible Publishing facade for the runtime-owned executable content hasher.
/// </summary>
/// <remarks>
/// The canonical algorithm lives in <see cref="Elsa.Workflows.Runtime.Services.WorkflowExecutableHasher"/> so
/// export and import cannot drift onto different wire-significant hashes. This public type remains as a thin
/// facade to preserve existing constructor signatures and direct <c>new WorkflowExecutableHasher()</c> callers.
/// </remarks>
public sealed class WorkflowExecutableHasher
{
    private readonly IWorkflowExecutableHasher _inner = new Elsa.Workflows.Runtime.Services.WorkflowExecutableHasher();

    public string ComputeHash(ExecutableNode rootActivity) => _inner.ComputeHash(rootActivity);

    public string ComputeHash(
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies,
        WorkflowExecutableCheckpointCadence? checkpointCadence = null,
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables = null,
        IncidentStrategyReference? incidentStrategy = null) =>
        _inner.ComputeHash(
            rootActivity,
            inputContract,
            dependencies,
            checkpointCadence,
            workflowVariables,
            incidentStrategy);

    public string CreateArtifactId(string artifactIdPrefix, string artifactHash) =>
        _inner.CreateArtifactId(artifactIdPrefix, artifactHash);
}
