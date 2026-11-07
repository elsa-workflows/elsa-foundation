# BPMN hosting contract extension points

[`IBpmnModelRuntime`](IBpmnModelRuntime.cs) is a single-provider scoped replacement port. The default internal `BpmnModelRuntime` is registered with `TryAddScoped` by `ActivitiesBpmnHostingFeature`; shell initialization requires exactly one scoped registration. [`BpmnModelDefinition` and `BpmnModelProtocol`](BpmnModelDefinition.cs) describe the immutable direct profile separately from the frozen embedded authoring format.

The actual [BPMN API](../Api/EXTENSION_POINTS.md) consumes this port for publication, start, work/completion, cancellation, service-incident inspection/retry and execution/command inspection. Optional members default for existing replacements that do not implement them: `AdmitNewExecutions` defaults to `false`; `PublishFromSourceAsync` (authored provenance), `InspectAsync`, `CancelAsync`, `ListIncidentsAsync`, `RetryServiceAsync`, `ClaimHumanAsync` and `CompleteClaimedHumanAsync` refuse with `NotSupportedException`. The optional `ActivitiesBpmnDirectPublishing` feature in `Elsa.Activities.Bpmn.Interchange` also consumes `PublishFromSourceAsync`. The API advertises `elsa.api.bpmn-claims` whenever it is composed, so a replacement without claims answers those routes with a refusal. Replacements must preserve explicit persistence scope/tenant checks, live-source/full-pin authority, retained conflict/replay outcomes and model-owned invocation/attempt identity. They must use the shared runtime's fenced durable admission/checkpoint authority and the selected released semantics rather than inventing another store or interpreter. The built-in implementation refuses actor providers outside its qualified single-node profile. [Declared formats](README.md#declared-model-formats) identify the admitted bindings.

## Native service owner reader

[`IBpmnNativeWorkConsumer`](IBpmnNativeWorkConsumer.cs) is a single-provider scoped replacement port.
`ActivitiesBpmnHostingFeature` uses `TryAddScoped` for the internal `BpmnNativeWorkConsumer` and rejects
competing registrations or lifetimes at initialization. `ResolveAsync(origin)` returns the exact runtime
owner, unmodified logical process identity, complete root/worker pins and native service policy.
`InspectAsync(origin)` returns `NotConsumed`, positively accepted exact root input, or positively terminal
exact root evidence. It authenticates original committed starts after their delivery, retained terminal
sources and input fingerprints. Missing or malformed authority cannot become consumption or terminal proof.
The optional `Elsa.Jobs.Bpmn` association source consumes this port (cross-domain).

## Native result validation contributions

[`IBpmnNativeWorkResultValidator`](IBpmnNativeWorkConsumer.cs) is a scoped fan-in contributor interface.
Register with `TryAddEnumerable(ServiceDescriptor.Scoped<IBpmnNativeWorkResultValidator, T>())`.
One aggregator, internal `BpmnDirectExecutionDriver`, invokes `ValidateAsync(context, cancellationToken)`
sequentially immediately before a live native-result input advances its interpreter state. Context carries
the exact origin/input/fingerprint, policy-safe native result and physically validated native worker.
Contributions skip workers they do not own and remain read-only. Unavailable durable evidence raises
`RuntimeSchedulerWorkUnavailableException`; it does not become a modeled result.

Exact prior-input replay, obsolete/terminal rejection and native cancellation settlement bypass this
progression contribution. A live cancelled native result still reaches it. Zero contributors preserves
existing behavior. Known implementation: `JobsBpmnResultValidator` in optional `Elsa.Jobs.Bpmn`
(cross-domain), comparing an existing Jobs continuation permission without granting or acknowledging it.

## Native result delivery contributions

[`IBpmnNativeWorkResultDeliveryContributor`](IBpmnNativeWorkConsumer.cs) is a scoped fan-in contributor
registered with `TryAddEnumerable(ServiceDescriptor.Scoped<IBpmnNativeWorkResultDeliveryContributor, T>())`.
The internal `BpmnNativeWorkDelivery` aggregator invokes `CompleteAsync(context, cancellationToken)` sequentially
after the exact root input receipt and physical terminal worker/result source are validated, before the
existing native-result source becomes Delivered. Context includes explicit Accepted/Rejected meaning,
the actual receipt, current root revision/lifecycle and independent positive terminal proof when present.

Contributors skip workers they do not own and authenticate their own durable responsibility. They may
acknowledge that responsibility in its owning store; a transient failure keeps this same result source owed.
A rejected transport receipt alone never proves consumption. Contributors must not introduce a separate
worker source that waits for root consumption: the existing result source is excluded from cancellation's
downstream-source inventory. Zero contributors preserves existing behavior. Known implementation:
`JobsBpmnResultDeliveryContributor` in optional `Elsa.Jobs.Bpmn` (cross-domain), consuming the public native
settlement authority to confirm exact Jobs ACK or positive never-admitted proof before releasing history.
