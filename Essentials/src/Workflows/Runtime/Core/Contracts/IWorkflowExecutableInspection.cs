using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Reads one exact Workflow Executable by its artifact id, for consumers outside the Workflows Runtime API (such as Weaver)
/// that may couple only to <c>.Core</c> contracts: its identity, Artifact Hash, compiled graph and Source References. An
/// Executable is immutable: this contract offers reads only.
/// </summary>
/// <remarks>
/// This is a single-implementation replacement contract in the current application context: exactly one implementation is
/// installed, and a second registration fails startup. Consumers must fail closed when no implementation or several
/// implementations are installed. The reader makes no authority decision: a consumer decides run access (the Runtime
/// read permission the executable endpoints require) before it discloses anything it read. The default, in the Workflows
/// Runtime API, reads exactly what <c>GET runtime/workflows/executables/{artifactId}</c> returns: binding values and source
/// details (a secret read's reference included) stay out, and a sensitive binding is reported as sensitive.
/// The lookup is by artifact id; a caller that bound an Artifact Hash compares it with the returned one. An
/// infrastructure failure propagates.
/// </remarks>
public interface IWorkflowExecutableInspection
{
    /// <summary>The exact Executable, or <see langword="null"/> when no Executable has this artifact id.</summary>
    ValueTask<WorkflowExecutableInspectionDetails?> GetAsync(string artifactId, CancellationToken cancellationToken = default);
}
