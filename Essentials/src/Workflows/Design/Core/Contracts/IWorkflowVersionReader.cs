using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Reads exact persisted workflow definition versions, for consumers outside the Workflows Design API (such as Weaver) that
/// may couple only to <c>.Core</c> contracts. A version is immutable: this contract offers reads only.
/// </summary>
/// <remarks>
/// This is a single-implementation replacement contract in the current application context: exactly one implementation is
/// installed, and a second registration fails startup. Consumers must fail closed when no implementation or several
/// implementations are installed. The reader makes no authority decision: a consumer decides read authority for the
/// version's definition through <see cref="IWorkflowDefinitionAuthorizer"/> before it discloses anything it read. The
/// default, in the Workflows Design API, reads through the same version details composition as
/// <c>GET versions/{versionId}</c> and the same store list as <c>GET definitions/{definitionId}/versions</c>.
/// An infrastructure failure propagates.
/// </remarks>
public interface IWorkflowVersionReader
{
    /// <summary>The exact version, or <see langword="null"/> when no persisted version has this identity.</summary>
    ValueTask<WorkflowVersionDetails?> ReadAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The identity of the definition an exact version belongs to, or <see langword="null"/> when no persisted version has
    /// this identity. It discloses nothing else, so a consumer can ask the definition's owner before it reads the version.
    /// </summary>
    ValueTask<string?> FindDefinitionIdAsync(string versionId, CancellationToken cancellationToken = default);

    /// <summary>The definition's persisted versions in the store's order; empty when it has none.</summary>
    ValueTask<IReadOnlyList<WorkflowDefinitionVersionSummary>> ListAsync(string definitionId, CancellationToken cancellationToken = default);
}
