# Contract — Persisted Descriptor Shape (design domain, opaque)

The design domain knows a descriptor as exactly two values and **never** deserializes it to a concrete type.

## Persisted columns on `ActivityDefinitionVersion`

| Column | Type | Rule |
|---|---|---|
| `DescriptorType` | `string` (write-once) | The descriptor type's `FullName` (e.g. `Elsa.Primitives.Models.TypeInformation`, `Elsa.Workflows.Primitives.Models.WorkflowIdentity`). Registry key at runtime. Replaces `ImplementationKind`. |
| `DescriptorPayloadSource` | `string?` (write-once) | Serialized JSON of the descriptor. Opaque to design. Replaces `ImplementationDescriptorPayload`. |

Entity also exposes `[NotMapped] JsonElement DescriptorPayload` — a round-trippable view of `DescriptorPayloadSource`. Replaces the old `[NotMapped] IImplementationDescriptor`.

**Read contract**: `IActivityDefinitionVersion` exposes **both** `string DescriptorType` and `JsonElement DescriptorPayload` (decided — not domain-shadows). The loading handler therefore must hydrate `DescriptorPayload` from `DescriptorPayloadSource`. Exposing a `JsonElement` keeps the descriptor opaque (BCL type, no descriptor-type dependency) and does not touch §E2.2.

Write-once immutability enforced provider-side via `PropertySaveBehavior.Throw` (EF Core), defined as a provider-agnostic invariant in `.Persistence.Core` (G28).

## Handlers (no type resolution)

- **Saving**: `DescriptorPayloadSource = serialize(DescriptorPayload)`. `DescriptorType` already populated by the reconciler. No `.Kind` derivation, no descriptor-type knowledge.
- **Loading**: `DescriptorPayload = parse(DescriptorPayloadSource)` as `JsonElement`. **No** `IImplementationDescriptorRegistry` lookup; no cast to a concrete descriptor type. (`ActivityDescriptorDeserialisationException` removed.)

## Reconciliation model (`ActivityVersionReconciliationModel`)

- `string DescriptorType` (was `ImplementationKind`) — supplied **explicitly** by each source.
- `object ImplementationDescriptor` — the descriptor object, or a `JsonElement` for the JSON source.
- Reconciler maps: `entity.DescriptorType = model.DescriptorType`; `entity.DescriptorPayload = SerializeToElement(model.ImplementationDescriptor)`. No per-kind branch (SC-004).

## JSON catalog file format change

Each entry's `implementationKind` field → `descriptorType`. Otherwise unchanged. The descriptor object stays free-form JSON (bound to `JsonElement`).

## Runtime read path (round-trip)

Execution path reads `(DescriptorType, DescriptorPayloadSource→JsonElement)` + author args → `IActivityFactory.Create(descriptorType, payload, inputs, outputs)`. Only the owning runtime constructor ever materializes the concrete descriptor type.
