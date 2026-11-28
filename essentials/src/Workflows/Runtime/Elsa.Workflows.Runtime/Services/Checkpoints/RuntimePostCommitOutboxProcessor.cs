using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed class RuntimePostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
{
    private readonly RuntimePostCommitOutboxDelivery _delivery;

    internal Func<(IRuntimeHistoryObservationWriter? Writer, IRuntimeHistoryStore? Reader)>? HistoryProvidersResolver
    {
        get => _delivery.HistoryProvidersResolver;
        init => _delivery.HistoryProvidersResolver = value;
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher)
        : this(outboxStore, intentDispatcher, TimeProvider.System)
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider)
        : this(outboxStore, intentDispatcher, timeProvider, DefaultRuntimeFaultCapturePolicy.CreateDefault())
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider,
        IRuntimeFaultCapturePolicy faultCapturePolicy)
        : this(outboxStore, intentDispatcher, timeProvider, faultCapturePolicy, workflowDispatchStore: null)
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        IWorkflowDispatchStore? workflowDispatchStore)
        : this(outboxStore, intentDispatcher, timeProvider, faultCapturePolicy, workflowDispatchStore, logger: null)
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        IWorkflowDispatchStore? workflowDispatchStore,
        ILogger<RuntimePostCommitOutboxProcessor>? logger,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null)
        : this(outboxStore, intentDispatcher, timeProvider, faultCapturePolicy, workflowDispatchStore,
            deliveryFailureProjectors: [], logger, liveDrainDeliveryAccessor, coalescingSessionAccessor,
            persistenceAccessContextAccessor: null, contributions: [])
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        IWorkflowDispatchStore? workflowDispatchStore,
        IEnumerable<IPostCommitFailureProjector> deliveryFailureProjectors,
        ILogger<RuntimePostCommitOutboxProcessor>? logger,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null)
        : this(outboxStore, intentDispatcher, timeProvider, faultCapturePolicy, workflowDispatchStore,
            deliveryFailureProjectors, logger, liveDrainDeliveryAccessor, coalescingSessionAccessor,
            persistenceAccessContextAccessor: null, contributions: [])
    {
    }

    public RuntimePostCommitOutboxProcessor(IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher, TimeProvider timeProvider, IRuntimeFaultCapturePolicy faultCapturePolicy,
        IWorkflowDispatchStore? workflowDispatchStore, IEnumerable<IPostCommitFailureProjector> deliveryFailureProjectors,
        ILogger<RuntimePostCommitOutboxProcessor>? logger, IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor, IPersistenceAccessContextAccessor? persistenceAccessContextAccessor,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> contributions, IRuntimeAdmissionLoadSignal? loadSignal = null)
    {
        _delivery = new(outboxStore, intentDispatcher, timeProvider, faultCapturePolicy, deliveryFailureProjectors,
            contributions, logger, persistenceAccessContextAccessor, workflowDispatchStore, loadSignal,
            liveDrainDeliveryAccessor is null ? null : request => DeliversInMemory(request, liveDrainDeliveryAccessor, coalescingSessionAccessor));
    }

    public bool SupportsSourceKind(RuntimeDeliverySourceKind sourceKind) => _delivery.SupportsSourceKind(sourceKind);

    public bool SupportsSubscriptionDelivery(RuntimeDeliverySourceKind sourceKind, string deliveryKind, int protocolVersion) =>
        _delivery.SupportsSubscriptionDelivery(sourceKind, deliveryKind, protocolVersion);

    public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default) => _delivery.ProcessAsync(request, cancellationToken);

    // The live-drain fast path engages only when a live drain owns this exact execution's delivery, the request targets
    // EnqueueSchedulerWork intents (every other kind stays on the durable claim path), and the drain's session has
    // stopped folding. Every drain pushes a live-drain scope beside its session. While the session folds, its overlay is
    // authoritative and delivers the continuations itself; once it stops (at a boundary that ends it, or from the start
    // on a queue that cannot delete by identity), the rest of the drain commits checkpoint by checkpoint and this path
    // delivers those continuations.
    private static bool DeliversInMemory(
        RuntimePostCommitOutboxProcessRequest request,
        IRuntimeLiveDrainDeliveryAccessor liveDrainDeliveryAccessor,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor) =>
        liveDrainDeliveryAccessor.Current is { } scope &&
        scope.AppliesTo(request.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(request.IntentKind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork) &&
        coalescingSessionAccessor?.Current is not { IsActive: true };
}
