# Extension points — Workflows Publishing engine

This is the authoritative catalog of supported replacements and contribution seams owned by the endpoint-free
**publish + compile engine** (`Elsa.Workflows.Publishing`). Contracts live in
`Elsa.Workflows.Publishing.Core`; the defaults and composition documented here live in this engine feature.
The transport surface (HTTP endpoints, API capabilities), transport authorization, and the activity-draft
publish/test-run seams are owned separately by the API feature —
see [the Publishing API catalog](Api/EXTENSION_POINTS.md).

The authority and failure invariants are owned by
[ADR 0043](../../../../docs/adr/0043-publication-slots-define-start-authority.md); shared terms remain in the
[Elsa glossary](../../../../docs/glossary/elsa.md) and [root glossary](../../../../docs/glossary/root.md).

Most contracts below are on the **override** axis: one implementation owns the responsibility. Executable
compilation enrichment is the documented **contributor** (fan-in) exception.

## Overridable contracts

| Contract | Built-in default | Replace when |
|---|---|---|
| `IWorkflowExecutableCompiler` | `WorkflowExecutableCompiler` (scoped) | Compilation, artifact hashing, validation, or executable projection differs. |
| `IWorkflowActivationAuthority` *(Core — Elsa.Workflows.Runtime.Core)* | `InMemoryWorkflowActivationAuthority` (singleton) | Activation-slot authority and revision CAS must survive restart. Owned by Runtime, not Publishing. |
| `IPublicationRecordStore` | `InMemoryPublicationRecordStore` (singleton) | Publication lifecycle/audit history must survive restart. |
| `IPublicationPolicyStore` | `InMemoryPublicationPolicyStore` (singleton) | Host/workflow policy and revision CAS must survive restart. |
| `IPublicationProjectionIntentStore` | `InMemoryPublicationProjectionIntentStore` (singleton) | Existing projection-intent rows must stay readable. No built-in component writes intents (see below). |
| `IActivityPublicationReceiptStore` | `InMemoryActivityPublicationReceiptStore` (singleton) | Activity publication outcomes and idempotency bindings must survive restart or be shared across nodes. |
| `IPublicationPolicyResolver` | `PublicationPolicyResolver` (singleton) | A host needs a different policy source while preserving explicit-request precedence and safe defaults. |
| `IPublicationPreflightService` | `PublicationPreflightService` (singleton) | A host adds claim constraints beyond provider cardinality. |
| `IPublicationActivator` | `PublicationActivator` (scoped) | Authority coordination uses another transactional boundary while preserving CAS and compensation invariants, and `CompleteAsync`'s journal convergence (see Activation below). |

Register replacements before the feature's `TryAdd` defaults, or use `services.Replace(...)`. Persistence
packages that replace a related store family should remove and register the whole family explicitly so a host
cannot accidentally split one authority model between process-local and durable state.

The EF Core composition (`Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore`) replaces the four authority
stores and `IActivityPublicationReceiptStore`. It must preserve one immutable request fingerprint per
tenant-owned idempotency key; receipt lookup must use the request authorization context's tenant scope and must
not depend on the continued existence of the source draft. An Applied receipt is written by
`ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference,
ActivityPublicationReceipt>` in the same atomic transaction as the activity version, definition head, template,
Source Reference, layout, and dependencies. A persistence replacement must retain that shared transaction
boundary and recompute the canonical request fingerprint with `ActivityPublicationRequestFingerprint` before
accepting the receipt.

## Contract obligations

### Authority stores

- `IPublicationSlotStore` no longer exists. The definition-keyed ledger is `IWorkflowActivationAuthority` in
  `Elsa.Workflows.Runtime.Core`, with revisioned `TryActivateAsync` / `TryDeactivateAsync` transitions and an
  explicit `WorkflowActivationSource` ownership field. A failed expected revision must leave the slot unchanged.
  There is one ledger per engine, so the publish pipeline and artifact importer cannot double-activate a
  definition. Publishing requests activation through `IWorkflowActivationCoordinator` and retains only its
  publication record attempt journal; that journal never decides serving.
- `IPublicationRecordStore` retains publication lifecycle and failure facts and supports conditional status
  transitions.
- `IPublicationPolicyStore` stores the host policy under a null workflow ID and optional workflow overrides;
  writes are revision-checked.
- `IPublicationProjectionIntentStore` durably transitions idempotent prepare/activate/remove intents. Providers
  must preserve deterministic intent IDs, attempt state, retry timing, and failure details across restart. It has
  no writer: its only one, `PublicationProjectionReconciler`, had not been registered since activation moved to the
  runtime coordinator and was removed (#2193). The contract and its EF table remain so existing rows stay readable;
  removing them is a pending schema decision.

The supplied EF Core provider implements these four contracts. Compose
`services.AddPublishingEntityFrameworkCore(...)`; its tables, indexes, CAS behavior, and serializers are
provider-neutral with respect to this engine feature.

### Policy and preflight

`IPublicationPolicyResolver` must preserve the precedence and explicit coexistence rules in ADR 0043. It returns
the resolved action, slot, policy source, and revision used by management clients.

`IPublicationPreflightService` compares candidate claims with authoritative claim sets. Replacements may add
host constraints, but must not weaken provider-declared `Exclusive` cardinality or treat shared definition or
artifact identity as an authority exemption. Trigger extraction/cardinality belongs to Runtime's contracts.

### Activation

`IPublicationActivator` coordinates prepared candidates, slot CAS, record lifecycle, old-authority preservation,
and compensation. A losing or failed candidate cannot make the old publication invisible.

Publishing has no projection seam of its own. `PublicationActivator` hands each candidate to Runtime's
`IWorkflowActivationCoordinator`, which prepares the trigger bindings and recurring schedules, switches them over
after the slot compare-and-swap, notifies observers, and compensates a failed step
([Workflows Runtime extension points](../Runtime/EXTENSION_POINTS.md)). The slot transition commits before the
projections switch. If a process dies between the two, the coordinator completes that activation before the slot's
next activation, and `CompleteInterruptedActivationsStartupTask` completes it at the next shell start. Unpublish
completes nothing: it turns off every activation that serves the slot, whatever the slot's history, and retires the
slot's publication record and every other `Active` record of the slot from the status it is in, a `Candidate` included. Derived
projection notifications occur only after the durable serving set reaches its final state; Runtime HTTP consumes the
neutral `IWorkflowTriggerIndexObserver` seam and performs a full refresh when authority changes.

The publication journal follows the slot the same way
([elsa-workflows/elsa-foundation#2223](https://github.com/elsa-workflows/elsa-foundation/issues/2223)). The slot
transition commits before the journal is written, so a process that dies in between leaves the slot's publication a
`Candidate` and the one it replaced `Active`, whether it died before the projections switched or after.
`IPublicationActivator.CompleteAsync(definition, slot)` first completes the slot's activation through the coordinator.
When the coordinator reports the activation its completion replaced, the completion retires that publication's record
whatever its source reference's state, as `ActivateAsync` retires the record its activation replaced: the runtime
reports the activation it switched off even when it could not retire that one's reference (#2251), and the completion
that later retires a leaked reference reports it too, whether or not the slot's publication still lags. Once that
activation serves (the slot still names it at the revision completion read, and its source reference is
live), it retires every other `Active` publication of the slot whose source reference the runtime has retired, then
marks the slot's publication `Active` with an activation time; a `Retired` publication the slot names again, after a
failed replacement handed the slot back, is marked `Active` the same way, and retired again if the slot has moved
on by the time it is marked. Marking the slot's publication active is the
last write, here and in `ActivateAsync`, so a process that stops part way leaves it lagging for the next completion.
Every write is a status compare-and-swap, so completions are idempotent and concurrent ones apply each transition
once. The journal is left alone when completion fails or when the slot is empty or owned by another source, and the
slot's publication is left alone when it has failed or its reference is retired: none of those serves. It runs:

- before every `ActivateAsync`, so a replacement retires the publication it replaced from `Active`;
- in `PublishWorkflowRequestHandler` when a same-version republish finds the slot's publication not yet `Active`. The
  republish answers once it is; one that cannot be completed is refused with the completion's failure code (HTTP 409),
  never reported as published;
- at shell start, in `CompleteInterruptedPublicationsStartupTask` (`[Order(5)]`, every node), for every slot publishing
  owns among those the runtime's pass visits (the `OccupiedActivationSlots` service), so a designer-published workflow heals
  without another publish.

`ActivateAsync` never journals a second active record for an activation it did not mint. The coordinator answers
`AlreadyActive` without minting anything when the slot already serves the candidate's artifact, which a same-version
publish that lost a race, or that preflighted before the winner moved the slot, reaches. When the slot names the
candidate itself (a retry of an interrupted publish) the candidate is marked `Active` as usual. When it names another
publication, the candidate is recorded `Failed` with `artifact_already_serving` and the result is a success carrying the
publication the slot names, once completion has brought that record to `Active`: the same answer a same-version republish
gets from the handler's early return (`WasCreated` false), and what the caller asked for, since the artifact serves. When
that record cannot be confirmed active, or another source owns the slot (`slot_owner_conflict`), the candidate is failed
and the result is a failure. Implementers of `IPublicationActivator` keep this: a successful result's `Publication` may
therefore be a record other than the candidate, and the publish handler reads the view from it.

One residual: when a completion publishing did not run, such as the runtime's own shell-start pass, switched the
replaced activation off, and its source reference still could not be retired when the slot's publication was marked
`Active` (a reference-store failure the Runtime catalog's operator recovery describes), that replaced publication stays
`Active` in the journal. Nothing reported it to publishing, and nothing lags afterwards to send completion back to it.
A later completion publishing runs that retires the reference reports it, and retires the record then. It serves
nothing, and slot views and the publish-on-reconcile check read the publication the slot names, so it shows only in the
slot's record history.

## Persistence-provider checklist

A Publishing persistence package that backs the engine's authority state should:

1. Implement the three Publishing-owned authority stores (`IPublicationRecordStore`,
   `IPublicationPolicyStore`, `IPublicationProjectionIntentStore`) plus `IActivityPublicationReceiptStore` and
   register them as one composition unit. Activation is not among them: it is `IWorkflowActivationAuthority`,
   backed by the Runtime EF Core store family, and a Publishing persistence package must neither implement
   nor register it.
2. Compose Runtime persistence that enforces unique activation-slot identity and compare-and-swap revisions
   in storage, not only in process memory; do not recreate that authority inside Publishing.
3. Index publication records by slot and projection intents by publication.
4. Version wire documents and provide upcasters/fixtures for every prior version.
5. Prove restart behavior, stale-revision rejection, idempotent intent replay, and compensation with provider
   tests.
6. Keep Runtime executable/reference/trigger/schedule stores in their owning Runtime persistence module; do not
   move those contracts into Publishing merely because the publish flow consumes them.
7. Persist activity publication receipts by opaque hashed operation identity and prove same-request replay,
   different-request rejection, stale-review no-write, and receipt rollback with every other publication
   document.

The supplied EF Core provider (`AddPublishingEntityFrameworkCore(...)`) composes these together with the API
feature's `IActivityDraftTestRunStore`.

## Executable compilation fan-in

### `IExecutableCompilationSource`

- **Kind:** Source. Each implementation asynchronously returns one immutable `ExecutableCompilationContribution`
  from `GetContributionAsync(ExecutableCompilationContext, CancellationToken)` without mutating the compiled tree.
- **Context:** The source sees the resolved compile source, compiled root, request, and explicit optional tenant
  scope. It may return deterministic node-metadata claims and exact child artifact/node dependency claims.
- **Composition:** `ExecutableNodeMetadataEnricher` publishes the named Sequential `ExecutableCompilationCollecting`
  inline event. This engine owns the single active `CollectExecutableCompilation` handler, which resolves sources
  in stable type-identity order, stamps source ownership, validates the complete claim set, and appends it to the
  event for read-back.
- **Conflict rule:** Equal metadata or dependency duplicates are idempotent. Unequal node metadata, multiple child
  identities for one node, or multiple hashes for one child artifact fail with deterministically ordered owner
  identities. Unknown nodes, blank claims, null results, and unstamped contributions are rejected before the event
  result is exposed.
- **Known implementation:** `DispatchPinSource` *(cross-domain — DispatchWorkflow Design)* contributes exact pinned
  child metadata and dependency claims after tenant/liveness/input-contract validation.
- **Boundary:** Collection occurs after node compilation and before executable hashing. The compiler canonicalizes
  declared workflow inputs and exact direct dependencies into behavioral identity, then validates every reachable
  child graph by full artifact ID/hash before publication can activate the candidate.

### `IExecutableNodeMetadataSource` (compatibility)

- **Kind:** Source, retained for source/binary compatibility while contributors migrate to
  `IExecutableCompilationSource`. `ExecutableNodeMetadataEnricher` publishes the paired
  `ExecutableNodeMetadataCollecting` event, and this engine owns the single active `CollectExecutableNodeMetadata`
  handler. New contributors register the generalized compilation source instead.

## Activity-template provider contributors

| Contract | Kind and registration | Consumer | Known implementation |
|---|---|---|---|
| `IActivityTemplateProviderCompiler` | Contributor keyed by stable provider identity and manifest schema. Provider features register implementations; `IActivityTemplateProviderCompilerRegistry` rejects ambiguous ownership. | `ActivityTemplateCompiler` performs deterministic provider compilation before executable hashing. | `GraphActivityProvider` *(cross-domain — Activities Graph Design)* |
| `IActivityTemplateDependencyDiscoverer` | Contributor keyed by stable provider identity and manifest schema. Provider features register implementations; `IActivityTemplateDependencyDiscovererRegistry` resolves the exact discoverer. | `ActivityTemplateCompiler` discovers exact direct dependencies before compilation. | `GraphActivityProvider` *(cross-domain — Activities Graph Design)* |

The Activity Graph implementation and its feature registration are documented in the
[contributing-feature catalog](../../Activities/Graph/Design/EXTENSION_POINTS.md).

## Cross-domain seams consumed by the engine

- Runtime executable artifacts, source references, trigger extraction/indexing, projection observers, and
  recurring schedules: [Workflows Runtime extension points](../Runtime/EXTENSION_POINTS.md).
- Design version and layout reads used by compilation: [Workflows Design extension points](../Design/Api/EXTENSION_POINTS.md).
- Design reconciliation completion (`WorkflowVersionsReconciled`), subscribed by the engine's
  `PublishReconciledWorkflowVersions` for publish-on-reconcile (spec 147):
  [Workflows Design Reconciliation extension points](../Design/Reconciliation/EXTENSION_POINTS.md).

## References

- Engine behavior and composition: [README](README.md).
- Transport surface, transport authorization, and activity-draft seams: [Publishing API catalog](Api/EXTENSION_POINTS.md).
- Publication authority decision: [ADR 0043](../../../../docs/adr/0043-publication-slots-define-start-authority.md).
- Repo-wide index: [root extension-point index](../../../../EXTENSION_POINTS.md).
