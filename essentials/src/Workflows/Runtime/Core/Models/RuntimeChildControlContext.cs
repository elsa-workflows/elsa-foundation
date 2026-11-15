using System.Collections.ObjectModel;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Callback data for one exact call's contiguous prefix. The runtime validates source authority before invocation; constructing this data grants none.</summary>
/// <remarks>An immutable callback envelope containing a live parent handle, not a durable or deeply immutable snapshot.
/// Record copying keeps that handle and the readonly collection snapshots. Equality grants no authority or deduplication.</remarks>
public sealed record RuntimeChildControlContext
{
    public RuntimeChildControlContext(IRuntimeActivityExecutionContext parentContext, string callingActivityExecutionId,
        string callingExecutableNodeId, string? callingIterationId, IReadOnlyDictionary<string, string> childSchedulingMetadata,
        IReadOnlyList<RuntimeChildControlTransport> batches, IReadOnlyCollection<string>? completionOutcomeNames = null)
    {
        ArgumentNullException.ThrowIfNull(parentContext);
        RuntimeChildControlValidation.Text(callingActivityExecutionId, nameof(callingActivityExecutionId));
        RuntimeChildControlValidation.Text(callingExecutableNodeId, nameof(callingExecutableNodeId));
        if (callingIterationId is not null) RuntimeChildControlValidation.Text(callingIterationId, nameof(callingIterationId));
        ArgumentNullException.ThrowIfNull(childSchedulingMetadata);
        ArgumentNullException.ThrowIfNull(batches);
        var snapshot = batches.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null))
            throw new ArgumentException("A child-control callback requires a nonempty contiguous prefix.", nameof(batches));
        var first = snapshot[0];
        var target = new RuntimeChildControlTarget(first.ParentWorkflowExecutionId, first.CallingActivityExecutionId,
            first.ConsumerActivityExecutionId, first.ParentExecutable, first.Batch.RootActivityExecutionId,
            first.Batch.ConsumerKey, first.Batch.ConsumerVersion);
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(parentContext.PinnedExecutable, target.ParentExecutable) ||
            target.ParentWorkflowExecutionId != parentContext.WorkflowExecutionId || target.ConsumerActivityExecutionId != parentContext.ActivityExecutionState.InvocationId ||
            target.CallingActivityExecutionId != callingActivityExecutionId)
            throw new ArgumentException("A child-control callback must match its exact parent and calling invocation.", nameof(batches));
        for (var index = 0; index < snapshot.Length; index++)
        {
            var item = snapshot[index];
            if (!target.MatchesAddress(item) || index > 0 &&
                (snapshot[index - 1].Batch.IsTerminal || snapshot[index - 1].Batch.Ordinal == long.MaxValue || item.Batch.Ordinal != snapshot[index - 1].Batch.Ordinal + 1))
                throw new ArgumentException("Child-control batches must be one exact stream's contiguous prefix, ending at any terminal barrier.", nameof(batches));
        }
        var outcomes = (completionOutcomeNames ?? []).ToArray();
        if (outcomes.Any(string.IsNullOrWhiteSpace) || outcomes.Distinct(StringComparer.Ordinal).Count() != outcomes.Length ||
            snapshot[^1].Batch.IsTerminal != (outcomes.Length > 0))
            throw new ArgumentException("Only a terminal prefix carries its ordinary local leaf completion outcomes.", nameof(completionOutcomeNames));
        ParentContext = parentContext;
        CallingActivityExecutionId = callingActivityExecutionId;
        CallingExecutableNodeId = callingExecutableNodeId;
        CallingIterationId = callingIterationId;
        ChildSchedulingMetadata = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(childSchedulingMetadata, StringComparer.Ordinal));
        Batches = Array.AsReadOnly(snapshot);
        CompletionOutcomeNames = Array.AsReadOnly(outcomes);
    }

    public IRuntimeActivityExecutionContext ParentContext { get; }
    public string CallingActivityExecutionId { get; }
    public string CallingExecutableNodeId { get; }
    public string? CallingIterationId { get; }
    public IReadOnlyDictionary<string, string> ChildSchedulingMetadata { get; }
    public IReadOnlyList<RuntimeChildControlTransport> Batches { get; }
    /// <summary>Ordinary local call-leaf outcomes when the prefix ends terminal; these do not rewrite the called workflow's status.</summary>
    public IReadOnlyCollection<string> CompletionOutcomeNames { get; }

    /// <summary>Keep the live execution handle and authored control data out of incidental diagnostics.</summary>
    public override string ToString() => nameof(RuntimeChildControlContext);
}
