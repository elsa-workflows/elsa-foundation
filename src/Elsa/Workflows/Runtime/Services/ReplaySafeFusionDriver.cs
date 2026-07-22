using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The D1 fused-span driver (ADR 0047 D1 / spec 123). Invoked from the <c>ScheduleActivity</c> handler's terminal
/// continuation point when the target is a ReplaySafe leaf inside a live coalescing burst, it runs the <b>start</b> and
/// <b>invoke</b> stages inline in the same dispatch instead of round-tripping their work items through the scheduler
/// queue + drain loop. The schedule handler has already committed the intent-free <c>ActivityScheduled</c> checkpoint
/// and hands over the <c>StartActivity</c> work item; the driver runs the extracted start stage core (committing the
/// intent-free <c>ActivityStarted</c> checkpoint), then dispatches the retained <c>InvokeActivity</c> work item through
/// the <b>existing, unchanged</b> invoke handler inline — whose terminal commit (completion / suspension / fault /
/// child-scheduling) flows through the normal overlay outbox exactly as today. No new command kinds, no new
/// crash-recovery mechanism: fusion only changes dispatch locality inside the burst (research §5, §8.1).
/// </summary>
public sealed class ReplaySafeFusionDriver
{
    private readonly RuntimeReplaySafeFusionOptions _options;
    private readonly WorkflowStartActivitySchedulerWorkHandler _startHandler;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRuntimeCoalescingSessionAccessor? _coalescingSessionAccessor;
    private readonly RuntimeSchedulerDispatchDiagnostics? _diagnostics;

    public ReplaySafeFusionDriver(
        RuntimeReplaySafeFusionOptions options,
        WorkflowStartActivitySchedulerWorkHandler startHandler,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IServiceProvider serviceProvider,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null,
        RuntimeSchedulerDispatchDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(startHandler);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _options = options;
        _startHandler = startHandler;
        _schedulerWorkQueue = schedulerWorkQueue;
        _serviceProvider = serviceProvider;
        _coalescingSessionAccessor = coalescingSessionAccessor;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Whether a fresh <c>ScheduleActivity</c> targeting <paramref name="node"/> should fuse: the toggle is on, the node
    /// is a ReplaySafe typed leaf (never an intrinsic — those keep their durable pre-activation boundary), and a live
    /// coalescing session owns this execution (fusion is a burst-only locality optimization; outside a burst the
    /// spec-109 carrier / Immediate path stands and every hop is discrete).
    /// </summary>
    public bool ShouldFuse(string workflowExecutionId, ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return _options.Enabled
            && node.IntrinsicKind is null
            && node.ActivityContract?.SideEffectProfile == SideEffectProfile.ReplaySafe
            && _coalescingSessionAccessor?.Current is { } session
            && session.AppliesTo(workflowExecutionId);
    }

    /// <summary>
    /// Continues a fused span past the already-committed <c>ActivityScheduled</c>: runs the start stage inline, then
    /// dispatches the invoke stage inline. If the start stage declines to fuse (not a fresh Scheduled ReplaySafe leaf —
    /// e.g. a redelivery), the <c>StartActivity</c> work item is enqueued so the discrete chain handles it; the item is
    /// never dropped and never enqueued on the successful fused path.
    /// </summary>
    public async ValueTask ContinueFusedSpanAsync(RuntimeSchedulerWorkItem startWorkItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startWorkItem);

        var invokeWorkItem = await _startHandler.ExecuteFusedStartAsync(startWorkItem, _serviceProvider, cancellationToken);
        if (invokeWorkItem is null)
        {
            // Fallback: not a fresh Scheduled ReplaySafe leaf. Hand the StartActivity item to the overlay queue so the
            // discrete StartActivity handler processes it — discrete-equivalent, nothing dropped (research §4).
            await _schedulerWorkQueue.EnqueueAsync(startWorkItem, cancellationToken);
            return;
        }

        _diagnostics?.RecordFusedSpan();

        // Dispatch the invoke stage through the existing, unchanged handler inline (research §8.1). Its terminal commit
        // (completion + parent-completion intent, or suspension / fault / child-scheduling) flows via the overlay outbox
        // exactly as the discrete path — D1 does not fuse the completion cascade.
        var invokeHandler = ResolveHandler(invokeWorkItem);
        await invokeHandler.HandleAsync(invokeWorkItem, cancellationToken);
    }

    private IWorkflowSchedulerWorkHandler ResolveHandler(RuntimeSchedulerWorkItem workItem)
    {
        // Mirror WorkflowSchedulerDrainer.FindHandler: a real (non-fallback) handler wins over any
        // IFallbackWorkflowSchedulerWorkHandler (e.g. MissingActivityInvocationSchedulerWorkHandler, which faults),
        // so the fused invoke dispatch resolves the same handler the drainer would have dispatched discretely.
        var handlers = _serviceProvider.GetServices<IWorkflowSchedulerWorkHandler>().ToArray();

        foreach (var handler in handlers)
            if (handler is not IFallbackWorkflowSchedulerWorkHandler && handler.CanHandle(workItem))
                return handler;

        foreach (var handler in handlers)
            if (handler is IFallbackWorkflowSchedulerWorkHandler && handler.CanHandle(workItem))
                return handler;

        throw new InvalidOperationException(
            $"No workflow scheduler work handler accepted the fused '{workItem.CommandKind}' work item '{workItem.WorkItemId}'.");
    }
}
