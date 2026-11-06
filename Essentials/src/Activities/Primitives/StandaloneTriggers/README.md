# Standalone Event integration

`StandaloneEventTriggerAuthoringFeature` supplies configuration metadata independently of the Event execution adapter.

The [operator guide](../../../../../docs/standalone-triggers.md) owns Event resource/API examples, mappings, grants, serving-slot behavior and recovery instructions. The implementation described here still requires composed producer, storage, restart and Studio qualification.

## Composition and ordinary Events

Select `StandaloneEventTriggerAuthoring` for type `event`, schema `1`, with configuration `{ "eventName": "OrderReceived" }`. It installs no Event source. `ActivitiesPrimitives` supplies the existing `PublishEvent` activity and ordinary Event adapter; Runtime combines its captured standalone subscription cohort with ordinary embedded starts and saved waits. A local Event keeps its existing same-execution semantics and does not start standalone subscriptions. Ordinary wait correlation does not filter new subscription starts.

For workflow-origin broadcast, the publisher checkpoint records an owed send. The later physical source/cohort commit selects matching enabled resources and freezes recipients, mappings, serving versions and grants before recipient effects. The producer checkpoint is not itself subscriber acceptance. Eligible recipients proceed independently; refused subscriptions are separate from accepted responsibility.

The direct BPMN target reads tenant authority from either the standalone source arm or the saved ordinary workflow cohort, and still requires the actual committed seal and ordinary model actor/queue. The complete real PublishEvent-to-BPMN host journey remains pending qualification; target metadata or a pure preparation test alone does not qualify dispatch from this path.

## Application-owned Event producer

An external module uses the public Core port through `AddStandaloneEventIngress<TSource>`. It submits to one explicit resource ID; this port does not discover all resources matching an `eventName`. The module provides a scoped `IStandaloneTriggerSourceContext<TSource>` from its actual authenticated/background boundary. Its `GetAsync` supplies current trusted persistence/tenant scope, partition and actor. It must explicitly report `SupportsResourceAuthorization = true` and implement `AuthorizeAsync(trigger,signal,ct)` using the application's normal resource/source policy. The default policy refuses. This policy runs against the actual scoped resource before returning a replay or preparing captures, including after pause or deletion. Never copy context authority from the submitted signal or treat metadata as invocation authority.

The registration below is part of the application's ordinary Runtime service composition. `OrdersEventContext` is an application implementation of that trusted context contract, not a built-in service:

```csharp
using Elsa.Activities.Primitives.StandaloneTriggers;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;

var payloadSchema = new ValueTypeDescriptor("Contoso.OrderReceived", schemaVersion: 1);
services.AddScoped<IStandaloneTriggerSourceContext<OrdersEventProducer>, OrdersEventContext>();
services.AddStandaloneEventIngress<OrdersEventProducer>(payloadSchema, "Contoso.Orders");
services.AddScoped<OrdersEventProducer>();
```

The application producer can inject the typed port and submit a detached signal:

```csharp
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

public sealed class OrdersEventProducer(IStandaloneTriggerIngress<OrdersEventProducer> ingress)
{
    public ValueTask<StandaloneTriggerIngressResult> SendAsync(
        string triggerId, string occurrenceId, string orderId, CancellationToken cancellationToken)
        => ingress.AdmitAsync(new StandaloneTriggerSignal(
            triggerId,
            occurrenceId,
            new ValueTypeDescriptor("Contoso.OrderReceived", schemaVersion: 1),
            JsonSerializer.SerializeToElement(new { orderId }),
            JsonSerializer.SerializeToElement(new { eventName = "OrderReceived" })), cancellationToken);
}
```

`OrdersEventContext.AuthorizeAsync` should delegate to the host's ordinary source/resource authorization service; knowing a trigger ID is insufficient. Choose a unique stable source family per producer. Different families can share the Event type; registering the same family for two producer CLR types fails. The Runtime factory's parameterless `Create<TSource>()` resolves the registered keyed descriptor and scoped context; callers cannot supply replacements.

The schema must match the installed producer descriptor exactly. Choose a stable occurrence ID for retries, preserve payload/metadata/source identity and retain the returned sanitized acknowledgement. It contains admission ID, replay/status, accepted association IDs and safe refusal codes/counts; it exposes no captures, pins, grant material, source fingerprints or seals. Changed submitted intent under an accepted key conflicts. Runtime selects, validates and durably captures recipients through the selected physical provider. `StandaloneTriggerAdmissionRefusedException` means a known complete refusal accepted no work; a transport/provider exception may need reconciliation with the same key under current authorization.

This example assumes the host already composes Runtime, the selected protected storage family, permitted published targets, enabled resource/associations and resource-owned invocation grants. Registering a typed port does not create those resources, install an HTTP endpoint, grant producer authority or create another queue/pump. Independent compiled-consumer and actual provider qualification remains required.

## Cross-domain contributions

`EventTriggerTypeSource` implements Runtime Core's `IStandaloneTriggerTypeSource`; the single `CollectTriggerTypes` handler feeds `StandaloneTriggerTypesInitializing`. `EventTriggerConfigurationValidator` implements scoped `ITriggerConfigurationValidator`; the single `ValidateTriggerConfiguration` handler invokes it through `StandaloneTriggerConfigurationValidating`. These authoring contributions do not install Event intake or alter broadcast, embedded starts or wait resumption.

`ActivitiesPrimitivesFeature` separately contributes the canonical `Elsa.Event.Broadcast` workflow-source capability beside its actual PublishEvent handler/decoder. Activation requires the selected physical provider to support that handler's combined saved cohort. This installs no fake external producer port. Authoring metadata alone does not enable a resource.

`AddStandaloneEventIngress<TSource>` delegates to Core's `AddStandaloneTriggerIngress<TSource>` with type/schema `event/1` and a module-owned source family (default `Elsa.Event.External`). Both submission/context contracts live in Core; source modules do not depend on concrete Runtime admission or physical stores. See the [owning contract catalog](../../../Workflows/Runtime/Elsa.Workflows.Runtime/EXTENSION_POINTS.md#standalone-trigger-authoring-and-source-boundaries).

See [spec 1217](../../../../../specs/1217-standalone-triggers/contracts/transports.md) for the Runtime source and delivery contract.
