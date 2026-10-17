using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Executes a claimed, checkpoint-admitted leaf outside the workflow actor. Custom hosts can invoke this
/// service after claiming work; native state is changed only later by the serialized coordinator.
/// </summary>
public sealed class BackgroundActivityWorker(
    IPersistenceOperationScopeFactory scopeFactory,
    IOptions<BackgroundActivityWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<BackgroundActivityWorker> logger)
{
    public async Task ExecuteClaimAsync(
        PersistenceScope persistenceScope,
        BackgroundActivityWorkClaim claim,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        settings.Validate();
        using var bodyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var renewalCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        BackgroundActivityWorkClaim? currentClaim = claim;
        Task renewal = Task.CompletedTask;
        BackgroundActivityWorkTerminalResult? result = null;
        try
        {
            await using var operation = await scopeFactory.CreateAsync(persistenceScope, bodyCancellation.Token);
            var services = operation.ServiceProvider;
            var item = await services.GetRequiredService<IBackgroundActivityWorkStore>()
                .FindAsync(claim.WorkflowExecutionId, claim.WorkId, bodyCancellation.Token);
            if (item is null || item.Status != BackgroundActivityWorkStatus.Claimed ||
                item.ClaimFence != claim.ClaimFence || item.ClaimOwnerId != claim.ClaimOwnerId ||
                item.Revision != claim.Revision || item.ClaimedAt != claim.ClaimedAt || item.VisibleUntil != claim.VisibleUntil ||
                item.VisibleUntil <= timeProvider.GetUtcNow())
                return;
            renewal = RenewAsync();
            result = await ExecuteLeafAsync(services, item.Request, settings, bodyCancellation.Token);
        }
        catch (OperationCanceledException) when (bodyCancellation.IsCancellationRequested)
        {
            // Shutdown, cancellation or lease loss leaves the durable row to its lifecycle owner.
        }
        finally
        {
            await renewalCancellation.CancelAsync();
            await renewal;
        }

        var finalClaim = Volatile.Read(ref currentClaim);
        if (result is null || finalClaim is null || bodyCancellation.IsCancellationRequested)
            return;
        // A separate persistence scope also avoids accidentally saving changes made by user code's DI services.
        await using var receiptScope = await scopeFactory.CreateAsync(persistenceScope, cancellationToken);
        var receipt = await receiptScope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>()
            .RecordResultAsync(finalClaim, result, timeProvider.GetUtcNow(), cancellationToken);
        if (receipt.Status == BackgroundActivityWorkStatus.ResultReady)
            await receiptScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .EnqueueAsync(receipt.Request.CoordinatorWorkItem, cancellationToken);

        async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(settings.RenewalInterval, timeProvider, renewalCancellation.Token);
                    // Stopping the renewal loop on normal body completion must not cancel a provider renewal that
                    // may already have committed its claim revision. Keep the call alive until it returns so the
                    // result is recorded with the exact claim now stored by the provider.
                    await using var renewalScope = await scopeFactory.CreateAsync(persistenceScope, bodyCancellation.Token);
                    var renewed = await renewalScope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>()
                        .RenewClaimAsync(Volatile.Read(ref currentClaim)!, timeProvider.GetUtcNow(), settings.LeaseDuration, bodyCancellation.Token);
                    Volatile.Write(ref currentClaim, renewed);
                    if (renewed is not null)
                        continue;
                    await bodyCancellation.CancelAsync();
                    return;
                }
            }
            catch (OperationCanceledException) when (renewalCancellation.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                // Uncertain ownership never authorizes a result. Expiry allows another host to recover the work.
                Volatile.Write(ref currentClaim, null);
                await bodyCancellation.CancelAsync();
                logger.LogWarning("Background work {WorkId} lost claim renewal; its body was signalled to stop", claim.WorkId);
            }
        }
    }

    private static async ValueTask<BackgroundActivityWorkTerminalResult> ExecuteLeafAsync(
        IServiceProvider services,
        BackgroundActivityWorkRequest request,
        BackgroundActivityWorkerOptions settings,
        CancellationToken cancellationToken)
    {
        using var masking = ActivityFaultMasking.For(services, request.ActivityExecutionId);
        // A failed read of the immutable artifact is an infrastructure failure, not an activity outcome. Let it
        // escape so the durable claim can expire and be retried; only a successful read that finds no artifact is a
        // terminal validation fault for this admitted invocation.
        var executable = await PinnedExecutableRead.FindAsync(services, request.Executable.ArtifactId, cancellationToken);

        ActivityActivationLease? activation = null;
        BackgroundActivityWorkTerminalResult? result = null;
        Exception? failure = null;
        try
        {
            if (executable is null)
                throw new InvalidOperationException("BG-ACT-002: The pinned background executable is unavailable.");

            SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(request.CoordinatorWorkItem, request.Executable, executable.Identity);
            var node = SchedulerWorkHandlerHelpers.ResolveExecutableNode(request.CoordinatorWorkItem, executable, request.ExecutableNodeId, "BackgroundActivity");
            BackgroundActivityEligibility.Validate(node);
            if (node.ActivityContract!.SchemaFingerprint != request.ContractFingerprint ||
                request.InputSnapshot.ContractFingerprint != request.ContractFingerprint ||
                request.InputSnapshot.InvocationId != request.InvocationId)
                throw new InvalidOperationException("BG-ACT-002: Background input, invocation or activity contract pin does not match.");
            var codec = services.GetRequiredService<BackgroundActivityCompletionCodec>();
            codec.ResolveResultType(node.ActivityContract);
            var types = services.GetRequiredService<IWellKnownTypeRegistry>();
            if (!types.TryGetTypeOrDefault(node.ActivityContract.Activation.Capability, out var pinnedActivityType) ||
                !typeof(IActivity).IsAssignableFrom(pinnedActivityType))
                throw new InvalidOperationException("BG-ACT-002: The pinned background activity type is unavailable.");
            if (node.ActivityContract.DescriptorKind == typeof(ClrActivityDescriptor).FullName)
            {
                var descriptor = services.GetRequiredService<IPayloadSerializer>()
                    .Deserialize<ClrActivityDescriptor>(node.ActivityContract.DescriptorPayload);
                if (!types.TryGetTypeOrDefault(descriptor.TypeAlias, out var descriptorType) || descriptorType != pinnedActivityType)
                    throw new InvalidOperationException("BG-ACT-002: The background descriptor does not name the pinned activity type.");
            }
            var dispatch = services.GetService<IWorkflowDispatchStagingAccessor>();
            var publish = services.GetService<IPublishStimulusStagingAccessor>();
            dispatch?.Reset(request.WorkflowExecutionId, request.ActivityExecutionId);
            publish?.Reset(request.WorkflowExecutionId, request.ActivityExecutionId);
            activation = await services.GetRequiredService<IActivityActivator>().ActivateAsync(
                new ActivityActivationRequest(
                    WorkflowExecutionId: request.WorkflowExecutionId,
                    ActivityExecutionId: request.ActivityExecutionId,
                    node.ActivityContract, request.InputSnapshot, request.Attempt, Descriptor: node.Descriptor), cancellationToken);
            BackgroundActivityEligibility.Validate(activation.Activity);
            if (activation.Activity.GetType() != pinnedActivityType)
                throw new InvalidOperationException("BG-ACT-002: Activation returned a different type than the pinned background activity.");
            var transition = await activation.Activity.ExecuteAsync(new ActivityExecutionContext(
                request.WorkflowExecutionId, request.InvocationId, request.AttemptId, request.ExecutableNodeId, cancellationToken));
            if (dispatch?.TakeWorkflowDispatch(request.WorkflowExecutionId, request.ActivityExecutionId) is not null ||
                publish?.TakePublishStimuli(request.WorkflowExecutionId, request.ActivityExecutionId).Count > 0)
                throw new InvalidOperationException("VF-BG-001: Background activity bodies cannot stage coordinator-owned effects.");
            result = transition switch
            {
                IActivityCompletionTransition completion => new BackgroundActivityWorkTerminalResult(
                    BackgroundActivityWorkResultKind.Complete, completion.Outcome,
                    codec.Serialize(node.ActivityContract, completion, settings.MaximumResultBytes)),
                IActivityFaultTransition fault => new BackgroundActivityWorkTerminalResult(
                    BackgroundActivityWorkResultKind.Fault, fault: masking.Mask(fault.Fault).ToNormalized()),
                IActivityCancellationTransition cancelled => new BackgroundActivityWorkTerminalResult(
                    BackgroundActivityWorkResultKind.Cancel, cancellationReason: masking.Mask(new ActivityFault("ActivityCancelled", cancelled.Reason)).Message),
                _ => throw new InvalidOperationException("VF-BG-001: A background activity must return Complete, Fault or Cancel.")
            };
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        var disposalFailure = await ActivityActivationLeaseDisposer.TryDisposeAsync(activation);
        if (disposalFailure is not null)
            failure = failure is null ? disposalFailure : ActivityActivationLeaseDisposer.CombineExecutionFailure(failure, disposalFailure);
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is null)
            return result ?? throw new InvalidOperationException("The background activity returned no result.");
        var safe = masking.Mask(failure);
        var classification = safe as IRuntimeFaultClassification;
        var code = disposalFailure is not null
            ? "ActivityDisposalFailed"
            : string.IsNullOrWhiteSpace(classification?.FailureCode)
                ? "BackgroundActivityFaulted"
                : classification.FailureCode;
        return new BackgroundActivityWorkTerminalResult(BackgroundActivityWorkResultKind.Fault,
            fault: new NormalizedActivityFault(
                code,
                safe.GetType().FullName!, safe.Message, safe.StackTrace, isRetryable: classification?.IsRetryable ?? false));
    }
}
