# Serialization rule

> All **domain-payload** JSON serialization and deserialization goes through `IPayloadSerializer`
> (`Elsa.Serialization.Core`). Do not hand-roll `System.Text.Json.JsonSerializer` / `JsonDocument`
> for data that another component reads.
>
> **Knowledge role:** focused reference. Link here from gates, specs, and skills instead of
> restating the rule.

## Why

`IPayloadSerializer` ([`JsonPayloadSerializer`](../src/essentials/Serialization/SystemText/Services/JsonPayloadSerializer.cs))
is the single, configured contract for domain payloads: it applies the agreed naming policy
(camelCase, case-insensitive on read) and the registry of converters contributed at startup. When one
component serializes with it and another deserializes with raw `JsonSerializer` defaults, the round-trip
silently breaks (e.g. casing mismatches). Routing every domain payload through the one contract keeps
write and read symmetrical.

## What it covers

Anything persisted or handed across a component boundary as JSON:

- Entity `*Source` shadow columns (e.g. `InputsSource`, `StateSource`) — the saving/loading handlers
  already use `IPayloadSerializer`.
- The opaque activity `DescriptorPayload` — serialized on save and rehydrated on load via
  `IPayloadSerializer`; the owning runtime constructor also deserializes the descriptor through it.
- Reconciliation / import models that carry serialized values.

Inject `IPayloadSerializer` and use `Serialize` / `SerializeToElement` / `Deserialize<T>` rather than
touching `JsonSerializer` directly.

**Type identity is alias-based, everywhere.** A workflow Variable/Input/Output persists its type as a
`TypeReference { Alias, CollectionKind }` (plain data, serialized natively); the compiled-Type path
(`TypeJsonConverter`) is alias-only too; and a CLR activity's construction descriptor is a
`ClrActivityDescriptor { TypeAlias }`. Every alias resolves to a CLR type via `IWellKnownTypeRegistry`
under the shared `TypeAliasConvention` (a reserved bare alias for BCL primitives, otherwise the dotted
`Type.FullName`). No persisted shape carries an assembly name or version — the former decomposed
`TypeInformation` (namespace/assembly/version) has been removed, so a package bump never breaks
resolution or construction.

## Sanctioned exceptions

These deliberately do **not** use `IPayloadSerializer`, because the JSON never crosses a domain
boundary or the use needs options the payload serializer can't provide:

- **EF Core `ValueConverter`s** — a converter both serializes and deserializes a column within the
  persistence layer; nobody else depends on its format (e.g. the layout/validation converters).
- **HTTP boundary** — FastEndpoints request/response and `Elsa.Http` content factories serialize at the
  transport edge with their own options.
- **Expression / scripting** — JavaScript/Liquid helpers serialize within an expression's execution
  scope.
- **Custom `JsonConverter`s** — they participate in the `System.Text.Json` pipeline by definition.
- **The Runtime EF Core persistence module**
  — it writes and reads its own runtime state rows entirely within the persistence layer; no other
  component parses their payload JSON. It owns a frozen `JsonSerializerOptions` deliberately
  independent of `IPayloadSerializer`: this is the *durability* format of suspended workflow state,
  frozen by a golden-fixture suite and evolved only through EF Core migrations. Adopting the mutable,
  startup-contributed converter registry of `IPayloadSerializer` would itself be an unstamped format
  change — the exact class of drift the module exists to eliminate.
- **The reconciliation content hasher**
  ([`DefaultActivityDefinitionHasher`](../src/essentials/Activities/Design/Persistence/Core/Services/DefaultActivityDefinitionHasher.cs))
  — it needs a canonical, sorted-key serialization that `IPayloadSerializer` does not produce, and only
  the SHA-256 of that JSON is ever persisted (the JSON itself is never read back).

## Schema evolution (Runtime EF Core module)

Runtime state (bookmarks, executables, execution/scheduler/operational/control-plane/incident/durable-value
state, checkpoint commits, the post-commit outbox, the durable scheduler work queue, workflow trigger
bindings) must be able to evolve without silently breaking already-suspended workflows. The contract:

- **EF Core migrations are the mechanism.** Each module owns its own migrations set and its own
  `__EFMigrationsHistory_*` table, applied or validated on shell activation according to the module's
  `EfMigratePolicy`. See [`src/essentials/Persistence/EntityFramework/README.md`](../src/essentials/Persistence/EntityFramework/README.md)
  for the shared policy.
- **Loud enforcement on read.** A pending model change, a missing migration, or a provider mismatch fails
  shell activation rather than serving a partially readable store.
- **A CI fixture gate freezes the payload format.** The Runtime EF suites re-serialize a canonical instance
  of each persisted payload and compare it semantically against the committed expectation, so a state-record
  field add/rename/remove/retype cannot land without an explicit decision.

### How to change a persisted runtime state record

For a clean-break pre-GA change, in the same change: alter the entity and its model configuration, add the
migration for every provider the module ships, update the payload expectation, and treat installations
carrying the older generation as reset-and-republish upgrades. An intentionally incompatible change
documents the required persistence reset.

## Cross-execution stimulus routing (W7, E3-1 / E3-5)

The trigger + stimulus-routing feature (`WorkflowsRuntimeTriggersFeature`) adds one new persisted document
kind and two cross-cutting (across-execution / across-artifact) indexes. Both route through the same bridge
serializer, versioning, and fixture gate as every other runtime kind.

- **Document kind `workflowTriggerBinding` (current/minimum version 2).** A durable index entry written at **publish
  time** mapping an external stimulus identity `(stimulusType, stimulusHash)` to a start-trigger activity
  inside a *pinned, published* executable — the piece Elsa 4 was missing that made "start a workflow from
  an external event" impossible. It is indexed over the published artifact, never the mutable authored
  definition. The binding also carries its publication, slot, provider-cardinality, and prepared/active authority.
  Two indexes back it: one over `stimulusHash`, the cross-artifact fan-out used by the router to start every
  workflow waiting on a stimulus, and one over `artifactId`, used to replace an artifact's bindings on
  republish. Writing an unroutable published trigger (a trigger node whose stimulus
  cannot be derived) **fails the publish** rather than persisting a trigger that can never fire.
- **New `by-stimulus` index on the existing `bookmarkState` kind.** Added additively so a single stimulus
  can resume *waiting instances across executions* (E3-5 fan-in), not only within one `workflowExecutionId`.
  No version bump or upcaster is needed: the state record shape is unchanged; only a new index was declared.
- **New `by-parent-activity-execution` index on the existing `activityExecutionState` kind (#514 / #413 item 3).**
  Added additively so a composite (e.g. a `Parallel` fork/join) can read only the activity-execution states
  *directly parented by it* via `IActivityExecutionStateStore.ListByParentAsync`, instead of loading every
  activity-execution state in the workflow and filtering in memory — the join fires once per branch completion,
  so the whole-workflow read made it O(branches × workflow states). The index is a keyword over the **already
  persisted** parent identity. No payload change is needed: the state record shape is unchanged; only a new
  index was declared, so the existing payload drift tests stay green, which is the wire-safety proof.
  The store queries the parent index and then applies a defensive
  in-memory `workflowExecutionId` filter, so the full `(workflowExecutionId, parentActivityExecutionId)` semantics
  hold identically across providers without relying on parent activity-execution ids being globally unique.

### Stimulus START idempotency is at-least-once

Stimulus delivery is an at-least-once world (a stimulus can be delivered more than once). The router's START
path dedups **only when an `idempotencyKey` is supplied**: `IStimulusStartDeduplicator` records the key and a
repeated delivery with the same key does not start a second instance. When **no** `idempotencyKey` is
supplied the router makes **no** dedup guarantee — a duplicate delivery **may double-start**. Callers that
require exactly-once start semantics must supply a stable `idempotencyKey`. The default deduplicator is an
in-process, best-effort store (not a durable cross-node dedup ledger); its guarantee is scoped to the
process that owns it. This is documented on `IStimulusRouter`/`IStimulusStartDeduplicator` and is intentional
scope for this wave — a heavy durable dedup store was explicitly out of scope.

### Published executables are durable (DS-2, W17)

A published workflow compiles to a `WorkflowExecutable` artifact that persists through the same Runtime
EF Core module as every other runtime state, written by
[`EfWorkflowExecutableStore`](../src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfWorkflowExecutableStore.cs)
over the `IWorkflowExecutableStore` seam. Its payload carries the compiled workflow-scope variable
declarations (`workflowVariables`, #972), the pinned activity contract, and the explicit input-nullability
data required by typed value flow. The
`InMemory` executable store registered by `WorkflowsPublishingFeature` is a `TryAdd` default that the
Runtime EF Core persistence feature overrides; when durable persistence is composed, publishing is durable
by construction. The `PublishWorkflowRequestHandler` saves the compiled artifact and then builds its trigger
index in the same publish flow, so a published artifact **and** its start-triggers survive a host restart.
`RuntimeEntityFrameworkCoreEndToEndTests` proves this against a file-backed SQLite database reopened with a
fresh service provider. There is intentionally **no**
Publishing-owned executable store or manifest — a second store writing the same kind under different options
would be a wire-level format divergence, exactly the hazard the single owning module exists to prevent.
