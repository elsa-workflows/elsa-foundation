using Elsa.Workflows.ExecutionEvidence.Models;

namespace Elsa.Workflows.ExecutionEvidence.Contracts;

/// <summary>
/// Process-local sink and query surface for execution evidence. Implementations are singletons and must be safe for
/// concurrent capture and query. This module makes no durability claim: evidence lives and dies with the process.
/// </summary>
public interface IExecutionEvidenceStore
{
    /// <summary>
    /// Records one checkpoint's evidence, atomically: either every fact in the batch lands or none does. Repeat calls
    /// for a checkpoint already recorded for that workflow are ignored, so checkpoint replay cannot fabricate duplicate
    /// facts. Also diffs <see cref="ExecutionEvidenceCommitBatch.RootVariables"/> against the previous snapshot and
    /// appends the resulting variable facts.
    /// </summary>
    void Append(ExecutionEvidenceCommitBatch batch);

    /// <summary>
    /// Returns facts for one workflow execution with a sequence greater than <paramref name="afterSequence"/>. The page's
    /// <see cref="ExecutionEvidencePage.FirstSequence"/> reveals whether the buffer caps dropped anything the caller has
    /// not already seen.
    /// </summary>
    ExecutionEvidencePage List(string workflowExecutionId, long afterSequence);

    /// <summary>
    /// Returns facts across every workflow execution carrying <paramref name="correlationId"/>, ordered by sequence
    /// within each workflow. This is the module's substitute for an evidence-session concept: a caller that wants to
    /// span child workflows correlates them instead of opening a session.
    /// </summary>
    ExecutionEvidencePage ListByCorrelation(string correlationId, long afterSequence);

    /// <summary>Drops evidence for one workflow execution, or for all of them when null.</summary>
    void Clear(string? workflowExecutionId);
}
