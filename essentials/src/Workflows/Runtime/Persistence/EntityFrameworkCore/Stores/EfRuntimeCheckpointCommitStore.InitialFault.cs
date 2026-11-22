using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfRuntimeCheckpointCommitStore
{
    private async ValueTask StageInitialStartFaultAsync(RuntimeCheckpointCommit commit, string scope, CancellationToken ct)
    {
        if (commit.ExpectedInitialStartFault is not { } expected) return;
        var state = commit.StateChanges.WorkflowExecution?.State ?? throw new InvalidDataException("The first fault has no execution state.");
        var scopeHash = EfRelationalIdentity.Hash(scope);
        var workflowHash = EfRelationalIdentity.Hash(commit.WorkflowExecutionId);
        var workHash = EfRelationalIdentity.Hash(expected.Poison.WorkItemId);
        if (await context.WorkflowExecutionStates.AnyAsync(row => row.ScopeKeyHash == scopeHash && row.WorkflowExecutionIdHash == workflowHash, ct))
            throw new InvalidDataException("A first fault cannot replace an existing execution.");
        var row = await context.WorkflowSchedulerPoisonRecords.SingleOrDefaultAsync(row => row.ScopeKeyHash == scopeHash &&
            row.WorkflowExecutionIdHash == workflowHash && row.WorkItemIdHash == workHash, ct)
            ?? throw new InvalidDataException("The first fault requires the actual parked scheduler failure.");
        InitialStartFaultAuthority.Validate(commit, EfWorkflowSchedulerPoisonStore.ReadChecked(row, scope, commit.WorkflowExecutionId, expected.Poison.WorkItemId));
        var originScope = state.IngressOrigin?.Scope ?? state.StimulusDeliveryOrigin?.Scope ?? state.ModelWorkOrigin?.Scope;
        if (originScope?.Value != scope || state.Partition?.Value != scope)
            throw new InvalidDataException("The first fault changed its physical source scope.");
        if (state.ModelWorkOrigin is { } model)
        {
            var source = await ReadIngressCheckpointSourceAsync(model.Scope, model.SourceOutboxItemId, ct);
            RuntimeModelNativeWorkOrigin? retainedWork;
            try { retainedWork = source.Intent?.Payload?.Deserialize<RuntimeModelNativeWorkOrigin>(JsonOptions); }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            { throw new InvalidDataException("The retained model-work source has a malformed accepted origin.", exception); }
            if (source.StimulusDeliverySourceScope != model.Scope || source.OutboxItemId != model.SourceOutboxItemId ||
                source.Intent is not { } intent || intent.WorkflowExecutionId != model.ExecutionId ||
                intent.IntentId != model.SourceIntentId || !model.IsEquivalentTo(retainedWork))
                throw new InvalidDataException("The first model-worker fault differs from its physical accepted work source.");
        }
        // The poison decision and source rows participate in the same transaction and CAS as the first terminal state.
        row.SchemaVersion = RuntimeSchemaWriteVersion.For(context, RuntimeSchedulerPoisonEfModule.SchemaFamily,
            RuntimeSchedulerPoisonEfModule.SchemaVersion, row.SchemaVersion);
        row.Revision = checked(row.Revision + 1);
    }
}
