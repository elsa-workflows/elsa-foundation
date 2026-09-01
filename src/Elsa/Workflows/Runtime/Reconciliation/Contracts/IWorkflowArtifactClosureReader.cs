using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Reconciliation.Contracts;

/// <summary>
/// Reads one closure envelope from a file, applying the wire-format gate (pipeline step 1).
/// </summary>
/// <remarks>
/// Split out from the source so that "where do envelopes come from" and "how is an envelope decoded" stay
/// separable: a future blob or OCI source reuses this decoder rather than re-implementing the format gate, and the
/// gate can be tested without a file system. Mirrors the design side's <c>IJsonWorkflowCatalogReader</c>.
/// </remarks>
public interface IWorkflowArtifactClosureReader
{
    /// <summary>
    /// Reads and validates the envelope at <paramref name="filePath"/>.
    /// </summary>
    /// <exception cref="Core.Exceptions.InvalidWorkflowArtifactClosureException">
    /// The file is missing, unreadable, not valid JSON, deserializes to null, or declares a
    /// <c>FormatVersion</c> this build does not know. No raw <c>IOException</c> or <c>JsonException</c> escapes.
    /// </exception>
    WorkflowArtifactClosure Read(string filePath, CancellationToken cancellationToken = default);
}
