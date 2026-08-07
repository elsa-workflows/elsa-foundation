using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.ExecutionEvidence.Services;

/// <summary>
/// Observes committed checkpoints and derives evidence from them. It contributes nothing to the commit: the enricher
/// pipeline is simply the only seam that sees every checkpoint's final change set, and this module is a QA collector,
/// so a capture failure must never surface as a workflow failure.
/// </summary>
public sealed class ExecutionEvidenceCheckpointEnricher(
    IExecutionEvidenceStore store,
    ExecutionEvidenceOptions? options = null,
    ILogger<ExecutionEvidenceCheckpointEnricher>? logger = null)
    : IRuntimeCheckpointCommitEnricher
{
    private readonly ExecutionEvidenceOptions _options = options ?? new ExecutionEvidenceOptions();

    /// <summary>Runs after every enriching contributor (existing phases are 0, 50 and 100) so the change set is final.</summary>
    public int Order => 1000;

    public ValueTask<RuntimeCheckpointCommit> EnrichAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        // Outside the try deliberately: a null commit is a broken caller contract, not a capture failure, and swallowing
        // it would return null where the committer requires a commit. RuntimeCheckpointCommitter guards this already.
        ArgumentNullException.ThrowIfNull(commit);

        try
        {
            store.Append(BuildBatch(commit));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, not a defect. Rethrowing would fault the workflow, so drop the evidence and stay quiet.
        }
        catch (Exception exception)
        {
            // Evidence is diagnostic only, so a collector defect must not fault the workflow it observes. It must not be
            // silent either: the only other symptom is a downstream test failing on absent evidence, which reads as a
            // workflow bug rather than a collector bug.
            logger?.LogWarning(
                exception,
                "Execution evidence capture failed for checkpoint {CheckpointId} of workflow execution {WorkflowExecutionId}. The evidence stream for this run is now incomplete.",
                commit.Checkpoint.CheckpointId,
                commit.Checkpoint.WorkflowExecutionId);
        }

        return ValueTask.FromResult(commit);
    }

    private ExecutionEvidenceCommitBatch BuildBatch(RuntimeCheckpointCommit commit)
    {
        var checkpoint = commit.Checkpoint;
        var workflowExecution = commit.StateChanges.WorkflowExecution?.State;
        var correlationId = workflowExecution?.CorrelationId;
        var records = new List<ExecutionEvidenceRecord>
        {
            new()
            {
                WorkflowExecutionId = checkpoint.WorkflowExecutionId,
                Kind = checkpoint.Name,
                OccurredAt = checkpoint.OccurredAt,
                CheckpointId = checkpoint.CheckpointId,
                CorrelationId = correlationId,
                Status = workflowExecution?.Status.ToString()
            }
        };

        records.AddRange(commit.StateChanges.ActivityExecutionInspections
            .Select(change => change.State)
            .OrderBy(activity => activity.ExecutionSequence)
            .ThenBy(activity => activity.ActivityExecutionId, StringComparer.Ordinal)
            .Select(activity => new ExecutionEvidenceRecord
            {
                WorkflowExecutionId = checkpoint.WorkflowExecutionId,
                Kind = ExecutionEvidenceKinds.Activity,
                OccurredAt = checkpoint.OccurredAt,
                CheckpointId = checkpoint.CheckpointId,
                CorrelationId = correlationId,
                ActivityExecutionId = activity.ActivityExecutionId,
                ActivityType = activity.ActivityType,
                AuthoredActivityId = activity.AuthoredActivityId,
                Status = activity.Status.ToString(),
                Outcomes = activity.OutcomeNames.ToArray()
            }));

        records.AddRange(commit.StateChanges.Incidents
            .Select(change => change.State)
            .OrderBy(incident => incident.CreatedAt)
            .ThenBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .Select(incident => new ExecutionEvidenceRecord
            {
                WorkflowExecutionId = checkpoint.WorkflowExecutionId,
                Kind = ExecutionEvidenceKinds.Incident,
                OccurredAt = checkpoint.OccurredAt,
                CheckpointId = checkpoint.CheckpointId,
                CorrelationId = correlationId,
                ActivityExecutionId = incident.ActivityExecutionId,
                Message = incident.Message
            }));

        return new ExecutionEvidenceCommitBatch
        {
            WorkflowExecutionId = checkpoint.WorkflowExecutionId,
            CheckpointId = checkpoint.CheckpointId,
            OccurredAt = checkpoint.OccurredAt,
            CorrelationId = correlationId,
            Terminal = workflowExecution?.Status.IsTerminal() == true,
            Records = records,
            RootVariables = CaptureRootVariables(workflowExecution?.RootVariableFrame)
        };
    }

    private IReadOnlyDictionary<string, ExecutionEvidenceVariableCapture>? CaptureRootVariables(VariableFrameState? frame) =>
        frame?.Values.ToDictionary(entry => entry.Key, entry => Capture(entry.Value), StringComparer.Ordinal);

    /// <summary>
    /// Applies every capture decision — redaction, external references, explicit nulls, truncation — so that the store
    /// only ever compares comparands. The comparand encodes the disposition alongside the value, so a variable moving
    /// from a value to null, or from inline to external storage, still registers as a write even though neither state
    /// carries an inline value to compare.
    /// </summary>
    private ExecutionEvidenceVariableCapture Capture(ValueEnvelope envelope)
    {
        // Content identity is derived once, up front, from whatever the envelope actually holds — never from a prefix,
        // a length, or a field that only some dispositions populate. Every silent-lost-write defect in this module has
        // been a comparand that failed to distinguish two genuinely different values, so this is the one invariant:
        // different content, different comparand.
        var content = DescribeContent(envelope);

        if (_options.RedactSensitiveValues && envelope.Policy.IsSensitive)
            return Withheld(ExecutionEvidenceValueDisposition.Sensitive, content);

        switch (envelope.Presence)
        {
            case ValuePresence.Absent:
                return Withheld(ExecutionEvidenceValueDisposition.Absent, content);
            case ValuePresence.ExplicitNull:
                return Withheld(ExecutionEvidenceValueDisposition.Null, content);
        }

        if (envelope.ExternalReference is not null)
        {
            return new ExecutionEvidenceVariableCapture
            {
                Disposition = ExecutionEvidenceValueDisposition.External,
                Comparand = $"External:{content}"
            };
        }

        if (envelope.InlineValue is not { } inline)
            return Withheld(ExecutionEvidenceValueDisposition.Absent, content);

        return content.Length > _options.MaxInlineValueLength
            ? Withheld(ExecutionEvidenceValueDisposition.Truncated, content)
            : new ExecutionEvidenceVariableCapture
            {
                Disposition = ExecutionEvidenceValueDisposition.Captured,
                Value = inline.Clone(),
                Comparand = $"Captured:{content}"
            };
    }

    /// <summary>
    /// The whole of what the envelope holds, as one string: the external locator when the value lives elsewhere, the raw
    /// inline JSON otherwise. Deliberately includes the external reference so that a <em>sensitive</em> value held
    /// externally still produces a distinguishing comparand.
    /// </summary>
    private static string DescribeContent(ValueEnvelope envelope) =>
        envelope.ExternalReference is { } external
            ? $"{external.StorageProfile}/{external.Locator}"
            : envelope.InlineValue?.GetRawText() ?? string.Empty;

    /// <summary>
    /// A value this module will not emit still needs a comparand that changes when the underlying value does, or
    /// rotating a secret — or rewriting an over-large payload — would produce no evidence that a write happened at all.
    /// The digest covers the entire content so it cannot collide the way a prefix or a length can, and it is a one-way
    /// hash so the withheld value itself never reaches the buffer.
    /// </summary>
    private static ExecutionEvidenceVariableCapture Withheld(
        ExecutionEvidenceValueDisposition disposition,
        string content) =>
        new()
        {
            Disposition = disposition,
            Comparand = $"{disposition}:{Digest(content)}"
        };

    private static string Digest(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
