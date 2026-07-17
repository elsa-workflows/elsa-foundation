# Extension points — Workflows.Publishing domain

This is the authoritative catalog of supported Publishing-domain replacements. Contracts live in
`Elsa.Workflows.Publishing.Core`; defaults and composition live in this API feature. The authority and failure
invariants are owned by [ADR 0043](../../../../../docs/adr/0043-publication-slots-define-start-authority.md), and
shared terms remain in the [Elsa glossary](../../../../../docs/glossary/elsa.md) and
[root glossary](../../../../../docs/glossary/root.md).

Most contracts below are on the **override** axis: one implementation owns the responsibility. Executable
compilation enrichment is the documented contributor exception. Runtime's trigger validators and observers are
separate add-don't-replace seams; see the Runtime catalog linked below.

## Overridable contracts

| Contract | Built-in default | Replace when |
|---|---|---|
| `IWorkflowExecutableCompiler` | `WorkflowExecutableCompiler` (scoped) | Compilation, artifact hashing, validation, or executable projection differs. |
| `IWorkflowTestRunStore` | `InMemoryWorkflowTestRunStore` (singleton) | Test-run results need shared/durable retention. |
| `IActivityDraftTestRunStore` | `InMemoryActivityDraftTestRunStore` (singleton) | Activity draft Test Run receipts, idempotency, and status lookup must survive restart. The Groundwork Publishing package replaces this default. |
| `IActivityDraftTestRunCancellationPolicy` | `DefaultActivityDraftTestRunCancellationPolicy` (singleton) | A host needs to suppress or further constrain cancellation while advertising the effective capability truthfully. |
| `IPublicationSlotStore` | `InMemoryPublicationSlotStore` (singleton) | Slot authority and revision CAS must survive restart. |
| `IPublicationRecordStore` | `InMemoryPublicationRecordStore` (singleton) | Publication lifecycle/audit history must survive restart. |
| `IPublicationPolicyStore` | `InMemoryPublicationPolicyStore` (singleton) | Host/workflow policy and revision CAS must survive restart. |
| `IPublicationProjectionIntentStore` | `InMemoryPublicationProjectionIntentStore` (singleton) | Projection delivery facts and retries must survive restart. |
| `IActivityPublicationReceiptStore` | `InMemoryActivityPublicationReceiptStore` (singleton) | Activity publication outcomes and idempotency bindings must survive restart or be shared across nodes. |
| `IPublicationPolicyResolver` | `PublicationPolicyResolver` (singleton) | A host needs a different policy source while preserving explicit-request precedence and safe defaults. |
| `IPublicationPreflightService` | `PublicationPreflightService` (singleton) | A host adds claim constraints beyond provider cardinality. |
| `IPublicationActivator` | `PublicationActivator` (singleton) | Authority coordination uses another transactional boundary while preserving CAS and compensation invariants. |
| `IPublicationProjectionPreparer` | `PublicationProjectionReconciler` (singleton) | Serving projections use another durable reconciliation mechanism. |

Register replacements before the feature's `TryAdd` defaults, or use `services.Replace(...)`. Persistence
packages that replace a related store family should remove and register the whole family explicitly so a host
cannot accidentally split one authority model between process-local and durable state.

The Groundwork composition also replaces `IActivityPublicationReceiptStore`. It must preserve one
immutable request fingerprint per tenant-owned idempotency key. Receipt lookup must use the
request authorization context's tenant scope and must not depend on the continued existence of the
source draft. That current operation tenant remains the receipt owner when the authorized source
resource is global. An Applied receipt is written by
`ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference,
ActivityPublicationReceipt>` in the same atomic transaction as the activity version, definition
head, template, Source Reference, layout, and dependencies. A persistence replacement must retain
that shared transaction boundary and recompute the canonical request fingerprint with
`ActivityPublicationRequestFingerprint` before accepting the receipt.

## Contract obligations

### Authority stores

- `IPublicationSlotStore` owns the unique `(WorkflowDefinitionId, SlotName)` authority and revisioned
  `TryActivateAsync` / `TryUnpublishAsync` transitions. A failed expected revision must leave the slot unchanged.
- `IPublicationRecordStore` retains publication lifecycle and failure facts and supports conditional status
  transitions.
- `IPublicationPolicyStore` stores the host policy under a null workflow ID and optional workflow overrides;
  writes are revision-checked.
- `IPublicationProjectionIntentStore` durably transitions idempotent prepare/activate/remove intents. Providers
  must preserve deterministic intent IDs, attempt state, retry timing, and failure details across restart.

The supplied Groundwork provider implements these four contracts. Compose
`services.AddGroundworkPublishingStores()` from `Elsa.Workflows.Publishing.Persistence.Groundwork`; its document
kinds, indexes, CAS behavior, and serializers are provider-neutral with respect to the API feature.

### Activity draft Test Runs

- `IActivityDraftTestRunStore` owns one receipt per deterministic
  `(OperationScope, DraftId, IdempotencyKey)` identity. Each tenant has a distinct durable operation scope and the
  explicit tenantless operation scope is distinct from every tenant scope. Operation ownership is derived from
  the caller tenant, independently of the resource tenant, so global activity drafts do not share receipts across
  tenant callers. Implementations must preserve the original request fingerprint, compare-and-swap receipt
  revisions, retain only the key hash, and keep the receipt beyond source-reference expiry so terminal and
  retention facts remain discoverable.
- `IActivityDraftTestRunCancellationPolicy` evaluates the durable receipt together with current Runtime Evidence.
  Replacements must distinguish whether cancellation is advertised from whether it is currently allowed. A
  denied or already-terminal request must not enqueue a Runtime command.
- Dispatch, retry, and cancellation use stable Runtime idempotency keys. An ambiguous acknowledgement may be
  retried, but must resolve to the same Test Run and workflow execution identity.
- Status projection may expose safe codes and diagnostics, immutable artifact/source identities, and the eventual
  outer activity execution identity. It must not expose synthetic wrapper/provider payloads or raw Runtime
  exception messages.

### Policy and preflight

`IPublicationPolicyResolver` must preserve the precedence and explicit coexistence rules in ADR 0043. It returns
the resolved action, slot, policy source, and revision used by management clients.

`IPublicationPreflightService` compares candidate claims with authoritative claim sets. Replacements may add
host constraints, but must not weaken provider-declared `Exclusive` cardinality or treat shared definition or
artifact identity as an authority exemption. Trigger extraction/cardinality belongs to Runtime's contracts.

### Activation and projection reconciliation

`IPublicationActivator` coordinates prepared candidates, slot CAS, record lifecycle, old-authority preservation,
and compensation. A losing or failed candidate cannot make the old publication invisible.

`IPublicationProjectionPreparer` prepares inactive projections and supports activate, compensate, remove, and
restore. Implementations must be idempotent and publication-scoped. Derived projection notifications occur only
after the durable serving set reaches its final state; Runtime HTTP consumes the neutral
`IWorkflowTriggerIndexObserver` seam and performs a full refresh when authority changes.

## Persistence-provider checklist

A new Publishing persistence package should:

1. Implement all four authority stores plus `IActivityDraftTestRunStore` and register them as one composition unit.
2. Enforce unique slot identity and compare-and-swap revisions in storage, not only in process memory.
3. Index publication records by slot and projection intents by publication.
4. Version wire documents and provide upcasters/fixtures for every prior version.
5. Prove restart behavior, stale-revision rejection, idempotent intent replay, Test Run receipt CAS/retention, and compensation with provider tests.
6. Keep Runtime executable/reference/trigger/schedule stores in their owning Runtime persistence module; do not
   move those contracts into Publishing merely because the publish flow consumes them.
7. Persist activity publication receipts by opaque hashed operation identity and prove same-request
   replay, different-request rejection, stale-review no-write, and receipt rollback with every
   other publication document.

## Executable compilation fan-in

### `IExecutableCompilationSource`

- **Kind:** Source. Each implementation asynchronously returns one immutable `ExecutableCompilationContribution` from `GetContributionAsync(ExecutableCompilationContext, CancellationToken)` without mutating the compiled tree.
- **Context:** The source sees the resolved compile source, compiled root, request, and explicit optional tenant scope. It may return deterministic node-metadata claims and exact child artifact/node dependency claims.
- **Composition:** `ExecutableNodeMetadataEnricher` publishes the named Sequential `OnExecutableCompilationCollecting` inline event. Publishing owns the single active `CollectExecutableCompilation` handler, which resolves sources in stable type-identity order, stamps source ownership, validates the complete claim set, and appends it to the event for read-back.
- **Conflict rule:** Equal metadata or dependency duplicates are idempotent. Unequal node metadata, multiple child identities for one node, or multiple hashes for one child artifact fail with deterministically ordered owner identities. Unknown nodes, blank claims, null results, and unstamped contributions are rejected before the event result is exposed.
- **Known implementation:** `DispatchPinSource` *(cross-domain — DispatchWorkflow Design)* contributes exact pinned child metadata and dependency claims after tenant/liveness/input-contract validation. The metadata-only source adapter is retained for compatibility and is not the extension seam for new implementations.
- **Boundary:** Collection occurs after node compilation and before executable hashing. The compiler canonicalizes declared workflow inputs and exact direct dependencies into behavioral identity, then validates every reachable child graph by full artifact ID/hash before publication can activate the candidate.

## Activity-template provider contributors

| Contract | Kind and registration | Consumer | Known implementation |
|---|---|---|---|
| `IActivityTemplateProviderCompiler` | Contributor keyed by stable provider identity and manifest schema. Provider features register implementations; `IActivityTemplateProviderCompilerRegistry` rejects ambiguous ownership. | `ActivityTemplateCompiler` performs deterministic provider compilation before executable hashing. | `GraphActivityProvider` *(cross-domain — Activities Graph Design)* |
| `IActivityTemplateDependencyDiscoverer` | Contributor keyed by stable provider identity and manifest schema. Provider features register implementations; `IActivityTemplateDependencyDiscovererRegistry` resolves the exact discoverer. | `ActivityTemplateCompiler` discovers exact direct dependencies before compilation. | `GraphActivityProvider` *(cross-domain — Activities Graph Design)* |

The Activity Graph implementation and its feature registration are documented in the
[contributing-feature catalog](../../../Activities/Graph/Design/EXTENSION_POINTS.md).

## Cross-domain seams consumed by Publishing

- Runtime executable artifacts, source references, trigger extraction/indexing, projection observers, and
  recurring schedules: [Workflows Runtime extension points](../../Runtime/EXTENSION_POINTS.md).
- Design version and layout reads used by compilation: [Workflows Design extension points](../../Design/Api/EXTENSION_POINTS.md).
- Endpoint authorization/configuration: [FastEndpoints extension points](../../../Api/FastEndpoints/EXTENSION_POINTS.md).

## References

- Module behavior and endpoint surface: [README](README.md).
- Publication authority decision: [ADR 0043](../../../../../docs/adr/0043-publication-slots-define-start-authority.md).
- Repo-wide index: [root extension-point index](../../../../../EXTENSION_POINTS.md).
