# Serialization rule

> All **domain-payload** JSON serialization and deserialization goes through `IPayloadSerializer`
> (`Elsa.Serialization.Core`). Do not hand-roll `System.Text.Json.JsonSerializer` / `JsonDocument`
> for data that another component reads.
>
> **Knowledge role:** focused reference. Link here from gates, specs, and skills instead of
> restating the rule.

## Why

`IPayloadSerializer` ([`JsonPayloadSerializer`](../src/Elsa/Serialization/SystemText/Services/JsonPayloadSerializer.cs))
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
- **The Groundwork runtime persistence bridge**
  ([`IGroundworkRuntimeDocumentSerializer`](../src/Elsa/Persistence/Groundwork/Serialization/IGroundworkRuntimeDocumentSerializer.cs))
  — the bridge both writes and reads its runtime state documents entirely within the persistence
  layer; no other component parses their `ContentJson`. It owns a frozen `JsonSerializerOptions`
  deliberately independent of `IPayloadSerializer`: this is the *durability* format of suspended
  workflow state, frozen by a golden-fixture suite and evolved only through explicit per-kind version
  policy (see **Schema evolution** below). Adopting the mutable, startup-contributed
  converter registry of `IPayloadSerializer` would itself be an unstamped format change — the exact
  hazard this bridge's versioning exists to eliminate. All bridge serialization goes through the one
  sealed serializer service, never raw `JsonSerializer`.
- **The reconciliation content hasher**
  ([`DefaultActivityDefinitionHasher`](../src/Elsa/Activities/Design/Persistence/Core/Services/DefaultActivityDefinitionHasher.cs))
  — it needs a canonical, sorted-key serialization that `IPayloadSerializer` does not produce, and only
  the SHA-256 of that JSON is ever persisted (the JSON itself is never read back).

## Schema evolution (Groundwork runtime bridge)

By default, runtime state persisted by the Groundwork bridge (bookmarks, executables, execution/scheduler/
operational/control-plane/incident/durable-value state, checkpoint commits, the post-commit outbox,
the durable scheduler work queue, workflow trigger bindings)
must be able to evolve without silently breaking already-suspended workflows. The contract:

- **Per-kind integer versions, hosted in the envelope.** Each runtime document kind has a current integer
  version declared in
  [`ElsaRuntimeDocumentVersions`](../src/Elsa/Persistence/Groundwork/Serialization/ElsaRuntimeDocumentVersions.cs).
  The version is stamped into the Groundwork **envelope** `SchemaVersion` field on
  every write — never inside the content JSON and never on the domain state records, keeping
  persistence concerns out of `WorkflowExecutionState` et al. Only positive-integer document stamps are
  accepted. The storage manifest's `"1.0.0"` version is a separate Groundwork manifest/index contract and
  is not a persisted-document alias.
- **Loud enforcement on read.** The serializer parses the stamp: the current version deserializes
  directly; a version below the kind's minimum-readable boundary, an unrecognized/non-positive version, or a future version throws
  Groundwork's structured `DocumentSchemaVersionException`, naming the kind, stamp, parsed version, and
  supported range — never a silent default-valued hydrate.
- **Clean baseline, with one explicit rolling window.** `ElsaRuntimeDocumentVersions` sets
  minimum-readable equal to current for every Runtime kind except `executableActivityTemplate`.
  Workflow executable v7 is a clean baseline that adds the compiled workflow-scope variable declarations
  (`workflowVariables`, #972) on top of v6's explicit input nullability. Executable activity
  template v2 retains v1 and upcasts the legacy nested runtime descriptor into the split consumer/schema/payload
  fields used by `ExecutableNode`. There is no Elsa upcaster interface, registry, or generic historical
  compatibility chain.
- **Groundwork owns migration capability.** A compatible in-place or rolling upgrade may deliberately retain
  an older minimum-readable version, contribute explicit Groundwork `IDocumentJsonUpcaster` steps, and keep
  every supported fixture. Groundwork's codec validates such a chain eagerly at construction. Each supported
  window must be composed explicitly rather than reintroducing an Elsa-specific codec or registry.
- **A CI fixture gate freezes the format.** Committed golden fixtures
  (`tests/Elsa/Persistence/Groundwork/Tests/Fixtures/v<n>/<kind>.json`) capture the exact content JSON
  each store writes for a canonical instance. A drift test re-serializes the canonical instance and
  compares it **semantically** (parsed and normalized, so incidental formatting differences are
  ignored) to the committed current-version fixture. Each clean-baseline kind keeps only that current
  fixture; executable activity templates retain their supported v1 and v2 fixtures. Any state-record field
  add/rename/remove/retype without a
  version bump fails the drift test.

### How to change a persisted runtime state record

For a clean-break pre-GA change, in the same change:

1. Bump that kind's current version in `ElsaRuntimeDocumentVersions`; keep its minimum-readable version at
   current.
2. Replace the kind's golden fixture with `Fixtures/v<new>/<kind>.json`; do not retain historical fixtures or
   add an Elsa compatibility shim.
3. Treat installations carrying the older generation as reset-and-republish upgrades.

For an explicitly supported compatible in-place or rolling upgrade, keep the older minimum-readable
boundary, add Groundwork `IDocumentJsonUpcaster` contributions for every required step, and retain every
supported historical fixture. An intentionally incompatible change advances the minimum-readable boundary
and documents the required persistence reset. Executable activity template v1-to-v2 is the supported rolling
window under this rule.

### Pre-GA clean-baseline reset

Every Runtime document kind except `executableActivityTemplate` currently admits only its current fixture.
`workflowExecutable` is current and minimum-readable version 6; input nullability is explicit and required.
`executableActivityTemplate` is current version 2 and minimum-readable version 1; its identity v1-to-v2
upcaster normalizes nested runtime descriptors without changing their consumer identity or payload.
`workflowExecutableSourceReference` and `workflowExecutionState` are current and minimum-readable version 4;
activity-execution state is version 4; activity-execution inspection, scheduler work items, workflow trigger
bindings, recurring schedules, and durable timers are version 2; unchanged kinds remain at version 1.
Source-reference v4 includes tenant scope; workflow-execution v4 includes dispatch nesting depth.

If an installation carries any older Runtime generation, reset the complete Runtime and Publishing Groundwork
persistence sets atomically while preserving Design and Activities data, then republish workflows before
serving traffic. Removing only selected documents is unsafe because execution state, continuations,
publication authority, and serving projections form a dependent persistence set around the same artifacts.

### Workflow-design Groundwork clean-schema boundary

The workflow-definition projection is a pre-GA clean baseline. Its portable ID lookup changed from the wide
search key to a required SHA-256 lookup projection, while the former name/description indexes were removed.
The definition unit therefore uses the versioned physical table `elsa_workflow_definitions_v2` and storage schema
version `2`; this deliberately prevents Groundwork from attempting an in-place required-column backfill or
leaving the removed indexes attached to the active table. The workflow-design envelope `SchemaVersion` remains
`"1.0.0"` because that value versions the document envelope, not this physical clean-schema boundary.

This change has no in-place migration. Before enabling a build containing this projection, discard and reprovision
the complete workflow-design Groundwork persistence set (definitions, versions, drafts, layouts, and design-operation
markers) from the current manifest, then recreate or import the workflow designs. Do not retain old definition,
version, or draft rows alongside the new definition table: they can otherwise form orphaned lifecycle records.
The reset is intentional for the unreleased pre-GA line and must be replaced by an explicit data migration before
the design schema is treated as production-compatible.

## Cross-execution stimulus routing (W7, E3-1 / E3-5)

The trigger + stimulus-routing feature (`WorkflowsRuntimeTriggersFeature`) adds one new persisted document
kind and two cross-cutting (across-execution / across-artifact) indexes. Both route through the same bridge
serializer, versioning, and fixture gate as every other runtime kind.

- **Document kind `workflowTriggerBinding` (current/minimum version 2).** A durable index entry written at **publish
  time** mapping an external stimulus identity `(stimulusType, stimulusHash)` to a start-trigger activity
  inside a *pinned, published* executable — the piece Elsa 4 was missing that made "start a workflow from
  an external event" impossible. It is indexed over the published artifact, never the mutable authored
  definition. The binding also carries its publication, slot, provider-cardinality, and prepared/active authority.
  Its sole golden fixture is `Fixtures/v2/workflowTriggerBinding.json`; v1 is rejected. Two Groundwork indexes back it:
  `by-stimulus` (keyword over `stimulusHash`, the cross-artifact fan-out used by the router to start every
  workflow waiting on a stimulus) and `by-artifact` (keyword over `artifactId`, used to replace an
  artifact's bindings on republish). Writing an unroutable published trigger (a trigger node whose stimulus
  cannot be derived) **fails the publish** rather than persisting a trigger that can never fire.
- **New `by-stimulus` index on the existing `bookmarkState` kind.** Added additively so a single stimulus
  can resume *waiting instances across executions* (E3-5 fan-in), not only within one `workflowExecutionId`.
  No version bump or upcaster is needed: the state record shape is unchanged; only a new index was declared.
- **New `by-parent-activity-execution` index on the existing `activityExecutionState` kind (#514 / #413 item 3).**
  Added additively so a composite (e.g. a `Parallel` fork/join) can read only the activity-execution states
  *directly parented by it* via `IActivityExecutionStateStore.ListByParentAsync`, instead of loading every
  activity-execution state in the workflow and filtering in memory — the join fires once per branch completion,
  so the whole-workflow read made it O(branches × workflow states). The index is a keyword over the **already
  persisted** nested field `state.parentActivityExecutionId` (Groundwork index fields are dot-paths resolved by
  walking nested JSON). No version bump or upcaster is needed: the state record shape is unchanged; only a new
  index was declared — the existing `GroundworkRuntimeDocumentFixtureTests` drift test stays green, which is the
  wire-safety proof. The manifest `SchemaVersion` stays `"1.0.0"` (it versions the storage manifest, not persisted
  document content); the Condition 7 backfill below triggers on
  the physicalized index-set change, so activity-execution states written before the index existed become visible
  through it without a re-save. The store queries the single-field parent index and then applies a defensive
  in-memory `workflowExecutionId` filter, so the full `(workflowExecutionId, parentActivityExecutionId)` semantics
  hold identically across providers without relying on parent activity-execution ids being globally unique.

### Condition 7 — added indexes backfill pre-existing documents (fixed in Groundwork preview.16)

**Previously** (Groundwork ≤ preview.10, guarded empirically by the probe test): the Groundwork SQLite
provider populated an index's physicalized projection only when a document was **written**. A document
written *before* a new index was declared was **not** retroactively backfilled into that index — not even
across a manifest version bump — so only documents saved after the index existed were visible through it,
and re-saving a document was required to make it visible.

**Fixed in Groundwork preview.16** (Groundwork PR #21): when a manifest
adds a portable index, `RelationalMaterializerBase` now backfills `groundwork_document_indexes` for
pre-existing documents (delete-then-insert inside the materialization transaction, sharing single-field
index semantics with save-time via `RelationalIndexValues.TryGetIndexValue`). A document written before the
index was declared becomes visible to the new index on the next manifest version bump — no re-save required.
The regression test `GroundworkAddedIndexBackfillRegressionTests` guards this behavior.

Consequence for the additive `bookmarkState` `by-stimulus` index: a bookmark that already existed in a
database at the moment this feature is deployed is now backfilled into the index on the manifest version
bump, so it is routable for cross-execution stimulus routing without waiting for a re-save. (Even before the
fix the impact was bounded because bookmarks are short-lived — created and consumed within a single
workflow's wait window and rewritten on the next checkpoint.) New databases are unaffected, and the
brand-new `workflowTriggerBinding` kind has no pre-existing documents.

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

A published workflow compiles to a `WorkflowExecutable` artifact that persists through the same Groundwork
bridge as every other runtime kind — the **`workflowExecutable`** document kind (current and
minimum-readable version 7 in
[`ElsaRuntimeDocumentVersions`](../src/Elsa/Persistence/Groundwork/Serialization/ElsaRuntimeDocumentVersions.cs)),
written by
[`GroundworkWorkflowExecutableStore`](../src/Elsa/Persistence/Groundwork/Stores/GroundworkWorkflowExecutableStore.cs)
over the `IWorkflowExecutableStore` seam, with one clean v7 golden fixture. Version 7 adds the compiled
workflow-scope variable declarations (`workflowVariables`, #972) to v6's pinned activity contract and
explicit input-nullability data required by typed value flow. The
`InMemory` executable store registered by `WorkflowsPublishingFeature` is a `TryAdd` default that the
Groundwork runtime-persistence feature overrides; when durable persistence is composed, publishing is durable
by construction. The `PublishWorkflowRequestHandler` saves the compiled artifact and then builds its trigger
index in the same publish flow, so a published artifact **and** its start-triggers survive a host restart.
`GroundworkWorkflowExecutableStoreTests.Published_Executable_Survives_Restart` proves this against a
file-backed SQLite database reopened with a fresh bridge instance. There is intentionally **no**
Publishing-owned executable store or manifest — a second store writing the same kind under different options
would be a wire-level format divergence, exactly the hazard the per-kind versioning above exists to prevent.
