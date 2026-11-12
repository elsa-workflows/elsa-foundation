using System.Globalization;
using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Deterministic source identities and exact immutable control payload comparison.</summary>
internal static class WorkflowControlTransport
{
    internal const string NotifyIntentKind = "Elsa.Activities.DispatchWorkflow.NotifyControl";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static string NotifyIntentId(RuntimeChildControlTarget target, long ordinal) =>
        "intent:dispatch-control:v1:" + target.DispatchId + ":" + ordinal.ToString(CultureInfo.InvariantCulture);

    internal static RuntimeChildControlTransport Create(RuntimeChildControlTarget target, RuntimeChildControlBatch batch,
        string commitId, string intentId) => new(target.DispatchId, target.ParentWorkflowExecutionId,
        target.CallingActivityExecutionId, target.ConsumerActivityExecutionId, target.ParentExecutable, commitId, intentId,
        RuntimePostCommitOutboxIdentity.CreateLogicalValue(commitId, intentId), batch);

    internal static bool Equivalent(RuntimeChildControlTransport? left, RuntimeChildControlTransport? right) =>
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, Json), JsonSerializer.SerializeToElement(right, Json));

    internal static void ValidateTarget(RuntimeChildControlTransport carried, RuntimeChildControlTarget target)
    {
        WorkflowControlProducerEnricher.ValidateOwned(carried.Batch, target);
        if (carried.DispatchId != target.DispatchId || carried.ParentWorkflowExecutionId != target.ParentWorkflowExecutionId ||
            carried.CallingActivityExecutionId != target.CallingActivityExecutionId ||
            carried.ConsumerActivityExecutionId != target.ConsumerActivityExecutionId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(carried.ParentExecutable, target.ParentExecutable))
            throw WorkflowDispatchControlPolicy.Failure("carried control target differs from admitted boundary");
    }

    internal static RuntimePostCommitIntent Notification(RuntimeChildControlTransport transport, DateTimeOffset at) =>
        new(transport.SourceIntentId, transport.Batch.ChildWorkflowExecutionId, NotifyIntentKind, at,
            transport.Batch.RootActivityExecutionId, transport.SourceIntentId,
            JsonSerializer.SerializeToElement(transport, Json));
}

internal sealed class WorkflowDispatchControlAuthorizer(IRuntimeWorkflowControlBoundary boundary,
    IPostCommitOutboxLookupStore outbox, IWorkflowDispatchStore dispatches) : IRuntimeChildControlAuthorizer
{
    public async ValueTask ValidateAsync(RuntimeChildControlTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        try
        {
            var target = await boundary.ResolveAsync(transport.Batch.ChildWorkflowExecutionId,
                transport.Batch.RootActivityExecutionId, cancellationToken)
                ?? throw WorkflowDispatchControlPolicy.Failure("source control has no admitted boundary");
            WorkflowControlTransport.ValidateTarget(transport, target);
            var source = await outbox.FindAsync(transport.SourceOutboxItemId, cancellationToken)
                ?? throw WorkflowDispatchControlPolicy.Failure("source control outbox item is missing");
            if (source.OutboxItemId != RuntimePostCommitOutboxIdentity.CreateLogicalValue(transport.SourceCommitId, transport.SourceIntentId) ||
                source.Intent.IntentId != transport.SourceIntentId)
                throw WorkflowDispatchControlPolicy.Failure("source control outbox identity differs");
            var intent = source.Intent;
            if (transport.Batch.IsTerminal)
            {
                var dispatch = await dispatches.FindAsync(target.DispatchId, cancellationToken)
                    ?? throw WorkflowDispatchControlPolicy.Failure("source terminal dispatch is missing");
                var payload = WorkflowDispatchCompletionEnricher.ValidateIntent(intent, dispatch,
                    new WorkflowDispatchIdentity(target.ParentWorkflowExecutionId, target.CallingActivityExecutionId), intent.RecordedAt);
                if (!WorkflowControlTransport.Equivalent(payload.Result.Control, transport))
                    throw WorkflowDispatchControlPolicy.Failure("source terminal control payload differs");
            }
            else
            {
                if (transport.SourceIntentId != WorkflowControlTransport.NotifyIntentId(target, transport.Batch.Ordinal) ||
                    !WorkflowControlTransport.Notification(transport, intent.RecordedAt).IsEquivalentTo(intent))
                    throw WorkflowDispatchControlPolicy.Failure("source notification payload differs");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (WorkflowControlBoundaryException) { throw; }
        catch (Exception exception) { throw WorkflowDispatchControlPolicy.Wrap(transport.Batch.ChildWorkflowExecutionId,
            transport.Batch.RootActivityExecutionId, exception); }
    }
}
