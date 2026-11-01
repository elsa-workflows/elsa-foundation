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
    private ActivityFault? entryFault;
    private JsonElement? acceptedResult;

    public ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context) =>
        throw new InvalidOperationException("A specialist boundary requires the native structural execution protocol.");

    public async ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(IRuntimeActivityExecutionContext context,
        IReadOnlyDictionary<string, object?> effectiveInputs, DateTimeOffset capturedAt, CancellationToken cancellationToken = default)
    {
        try { return await PrepareEntryAsync(context, effectiveInputs, capturedAt, cancellationToken); }
        catch (SpecialistTurnException exception)
        {
            // Return a native decision after the ordinary entry checkpoint. A known refusal must
            // retain its safe domain code and must not schedule a model child.
            entryFault = new(exception.Code, exception.Message);
            return [];
        }
    }

    private async ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryAsync(IRuntimeActivityExecutionContext context,
        IReadOnlyDictionary<string, object?> effectiveInputs, DateTimeOffset capturedAt, CancellationToken cancellationToken)
    {
        var identity = await binding.IdentifyAsync(context, cancellationToken);
        var node = context.ExecutableNode;
        var contract = node.ActivityContract!;
        if (node.InputBindings.Values.Any(x => x.Source == RuntimeInputBindingSource.SecretRead) ||
            context.ActivityExecutionState.InputSnapshot!.Values.Values.Any(x => x.Policy.RequiresEncryption || x.Presence == ValuePresence.Withheld))
            throw SpecialistModelRunner.Failure("specialist.value.encryption", "This specialist profile cannot dispatch withheld or encryption-required business inputs.");
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var descriptor = SpecialistNativeBinding.Descriptor(node);
        var controls = SpecialistNativeControls.Read(descriptor, contract, effectiveInputs);
        foreach (var input in contract.Inputs.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (SpecialistNativeControls.Enabled(descriptor) && input.Key == Elsa.Agent.Specialists.Core.Models.SpecialistInvocationControls.InputKey) continue;
            if (effectiveInputs.TryGetValue(input.Key, out var value)) values.Add(input.Name, value);
        }
        var request = new SpecialistTurnRequest(identity, descriptor, contract,
            JsonSerializer.SerializeToElement(values), context.ActivityExecutionState.InputSnapshot.BindingFingerprint) { Continuation = controls.Continuation, EffectiveLimits = controls.Limits };
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
        if (entryFault is not null) return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(entryFault));
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
        acceptedResult = SpecialistNativeControls.Complete(SpecialistNativeBinding.Descriptor(context.ExecutableNode),
            await journal.ResultAsync(handle, identity, child.CompletedChildActivityExecutionId, context.CancellationToken,
                request => SpecialistNativeBinding.VerifyRequest(request, identity, context.ActivityExecutionState, context.ExecutableNode)));
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
