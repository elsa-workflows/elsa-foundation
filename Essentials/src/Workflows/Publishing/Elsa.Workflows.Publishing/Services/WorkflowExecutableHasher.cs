using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Backward-compatible Publishing facade for the runtime-owned executable content hasher.
/// </summary>
/// <remarks>
/// The canonical algorithm lives in <see cref="Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher"/> so
/// export and import cannot drift onto different wire-significant hashes. This public type remains as a thin
/// facade to preserve existing constructor signatures and direct <c>new WorkflowExecutableHasher()</c> callers.
/// </remarks>
public sealed class WorkflowExecutableHasher
{
    private readonly IWorkflowExecutableHasher _inner = new Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher();

    public string ComputeHash(ExecutableNode rootActivity) => _inner.ComputeHash(rootActivity);

    /// <summary>Hashes the node tree and the value adapter pins (spec 1228 FR-028).</summary>
    public string ComputeHash(ExecutableNode rootActivity, IReadOnlyDictionary<string, string> valueAdapters) =>
        ((Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher)_inner).ComputeHash(rootActivity, valueAdapters);

    public string ComputeHash(
        ExecutableNode rootActivity,
        WorkflowExecutableInputContract inputContract,
        IReadOnlyCollection<WorkflowExecutableDependency> dependencies,
        PersistenceMode? persistenceMode = null,
        IReadOnlyCollection<RuntimeVariableDeclaration>? workflowVariables = null,
        IncidentStrategyReference? incidentStrategy = null,
        IReadOnlyDictionary<string, string>? valueAdapters = null) =>
        _inner.ComputeHash(
            rootActivity,
            inputContract,
            dependencies,
            persistenceMode,
            workflowVariables,
            incidentStrategy,
            valueAdapters);

    public string CreateArtifactId(string artifactIdPrefix, string artifactHash) =>
        _inner.CreateArtifactId(artifactIdPrefix, artifactHash);
}
