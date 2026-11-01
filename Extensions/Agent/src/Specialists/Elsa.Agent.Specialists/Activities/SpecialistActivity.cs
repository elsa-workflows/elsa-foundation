using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Activities;

[Activity(IsDiscoverable = false)]
internal sealed class SpecialistActivity(SpecialistNativeBinding binding, SpecialistTurnJournal journal, SpecialistModelRunner runner) : IActivity, IActivityResult<JsonElement>,
    IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler, IRuntimeActivityCheckpointParticipant
{
    private string? stagedHandle;
    private JsonElement? acceptedResult;

    public ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context) =>
        throw new InvalidOperationException("A specialist boundary requires the native structural execution protocol.");

    public async ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(IRuntimeActivityExecutionContext context,
        IReadOnlyDictionary<string, object?> effectiveInputs, DateTimeOffset capturedAt, CancellationToken cancellationToken = default)
    {
        var identity = await binding.IdentifyAsync(context, cancellationToken);
        var node = context.ExecutableNode;
        var contract = node.ActivityContract!;
        if (node.InputBindings.Values.Any(x => x.Source == RuntimeInputBindingSource.SecretRead) ||
            context.ActivityExecutionState.InputSnapshot!.Values.Values.Any(x => x.Policy.RequiresEncryption || x.Presence == ValuePresence.Withheld))
            throw SpecialistModelRunner.Failure("specialist.value.encryption", "This specialist profile cannot dispatch withheld or encryption-required business inputs.");
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var input in contract.Inputs.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
            if (effectiveInputs.TryGetValue(input.Key, out var value)) values.Add(input.Name, value);
        var request = new SpecialistTurnRequest(identity, SpecialistNativeBinding.Descriptor(node), contract,
            JsonSerializer.SerializeToElement(values), context.ActivityExecutionState.InputSnapshot.BindingFingerprint);
        _ = runner.ValidateRequest(request);
        if (context.ActivityExecutionState.PrivateState is not null)
        {
            stagedHandle = SpecialistNativeBinding.Handle(context.ActivityExecutionState);
            _ = await journal.ReadAsync(stagedHandle, identity, cancellationToken);
        }
        else stagedHandle = await journal.StageAsync(request, capturedAt, cancellationToken);
        return [];
    }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        if (stagedHandle is null) throw new InvalidOperationException("A specialist turn must be staged before its child is scheduled.");
        context.ScheduleChildActivity(SpecialistNativeBinding.ChildNode(context.ExecutableNode).ExecutableNodeId,
            metadata: new Dictionary<string, string> { [SpecialistNativeBinding.TurnHandleMetadataKey] = stagedHandle });
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer.WithState(ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement(stagedHandle), ValueProtectionPolicy.InstanceInline)));
    }

    public async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext child)
    {
        var context = child.ParentContext as IRuntimeActivityExecutionContext ?? throw new InvalidOperationException("A specialist requires a native parent context.");
        var identity = await binding.IdentifyAsync(context, context.CancellationToken);
        var handle = SpecialistNativeBinding.Handle(context.ActivityExecutionState);
        await binding.VerifyChildCompletionAsync(context, child.CompletedChildActivityExecutionId, child.CompletedChildExecutableNodeId, handle, context.CancellationToken);
        acceptedResult = (await journal.ResultAsync(handle, identity, child.CompletedChildActivityExecutionId, context.CancellationToken)).Result;
        return RuntimeStructuralContinuation.Complete();
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("specialist.turn.failed", "The specialist turn did not complete successfully.")));

    public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(IRuntimeActivityExecutionContext context,
        IReadOnlyCollection<DurableValueState> persistedValues, DateTimeOffset capturedAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new RuntimeActivityCompletionCheckpointPreparation([], ActivityTransition.Complete(
            acceptedResult ?? throw new InvalidOperationException("A specialist cannot complete without its accepted typed result."))));
    }
}
