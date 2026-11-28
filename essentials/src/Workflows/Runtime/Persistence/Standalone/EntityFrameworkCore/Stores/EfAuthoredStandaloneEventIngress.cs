using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfAuthoredStandaloneEventIngress(IRuntimeIngressDeliveryStore delivery, IStandaloneTriggerStore resources,
    IPersistenceAccessContextAccessor access, StandaloneTriggerAdmission admission, IRuntimePostCommitOutboxProcessor processor,
    IRuntimePostCommitIntentDispatcher dispatcher, IEnumerable<StandaloneTriggerSourceCapability> sourceCapabilities) : IRuntimeAuthoredStandaloneEventIngress
{
    public bool SupportsAdmission => delivery is EfRuntimeIngressDeliveryStore { SupportsAuthoredEventAdmission: true } &&
        TriggerSourcePipeline.Supports(processor, dispatcher, RuntimeDeliverySourceKind.StandaloneIngress,
            RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind);

    public async ValueTask AdmitAsync(RuntimeAuthoredStandaloneEvent source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source.Scope);
        ArgumentNullException.ThrowIfNull(source.Partition);
        ArgumentNullException.ThrowIfNull(source.Authority);
        if (source.Scope.TenantId is not { Length: > 0 } tenant || source.Partition.Value != source.Scope.Persistence.Value ||
            source.Occurrence is not { Length: 64 } || source.Occurrence.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") >= 0 ||
            source.PublisherActor is not { Length: 64 } || source.PublisherActor.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") >= 0 ||
            string.IsNullOrWhiteSpace(source.EventName) || source.EventName.Contains('\0') ||
            source.Payload.ValueKind == JsonValueKind.Undefined || source.CommittedAt <= DateTimeOffset.UnixEpoch ||
            source.CommittedAt != DateTimeOffset.FromUnixTimeMilliseconds(source.CommittedAt.ToUnixTimeMilliseconds()))
            throw new ArgumentException("The committed authored Event lacks its exact scope, source or body.", nameof(source));
        access.Current.EnsureTenantScope(tenant);
        if (!SupportsAdmission || delivery is not EfRuntimeIngressDeliveryStore store)
            throw new NotSupportedException("This host does not own a physical authored Event cohort writer.");

        var descriptor = sourceCapabilities.SingleOrDefault(capability => capability.SupportsResourceAuthorization &&
            capability.WorkflowIntentKind == RuntimeAuthoredStandaloneEvent.BroadcastIntent &&
            capability.Descriptor is { TypeKey: "event", SourceFamily: RuntimeAuthoredStandaloneEvent.BroadcastFamily })?.Descriptor
            ?? throw new NotSupportedException("The composing workflow module did not register its ordinary broadcast Event source.");
        var payload = source.Payload.Clone();
        var metadata = JsonSerializer.SerializeToElement(new { eventName = source.EventName });
        var match = new RuntimeWorkflowSubscriptionSource(descriptor.TypeKey, descriptor.SchemaVersion, "eventName", source.EventName,
            descriptor.SourceFamily, descriptor.PayloadSchema, payload, metadata);
        var invocation = new StandaloneTriggerInvocationContext(source.Scope, source.Partition, source.Authority);

        await EfTriggerBoundary.RunAsync(() => store.AdmitAuthoredEventAsync(source with { Payload = payload }, ReadMatchingAsync, PrepareAsync, cancellationToken),
            source.Scope.Persistence.Value, source.Occurrence);

        ValueTask<IReadOnlyList<TriggerAggregate>> ReadMatchingAsync(CancellationToken token) =>
            WorkflowSubscriptionSourceReader.ReadMatchingAsync(resources, source.Scope, match, token);

        ValueTask<RuntimeIngressAdmissionRequest> PrepareAsync(TriggerAggregate aggregate, CancellationToken token) =>
            admission.PrepareAuthoredEventAsync(descriptor, invocation,
                new StandaloneTriggerSignal(aggregate.Trigger.Id, source.Occurrence, descriptor.PayloadSchema, payload, metadata),
                aggregate, source.CommittedAt, token);
    }
}
