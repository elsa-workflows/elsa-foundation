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
/// Runtime API, reads through the same inspector as <c>GET runtime/workflows/executables/{artifactId}</c> and returns a
/// subset of that response: the identity, Artifact Hash, root, node and live-reference counts, the node tree (each node's
/// identities, type, structure kind, input bindings by name, source and sensitivity, child slots and connections), the
/// Source References and the direct dependencies. It leaves out what the endpoint also returns (the chosen reference and
/// its layout, compatibility metadata, the input contract, output captures, the BPMN projection and the retained execution
/// count); binding values and source details (a secret read's reference included) are never part of either.
/// The lookup is by artifact id; a caller that bound an Artifact Hash compares it with the returned one. An
/// infrastructure failure propagates.
/// </remarks>
public interface IWorkflowExecutableInspection
{
    /// <summary>The exact Executable, or <see langword="null"/> when no Executable has this artifact id.</summary>
    ValueTask<WorkflowExecutableInspectionDetails?> GetAsync(string artifactId, CancellationToken cancellationToken = default);
}
