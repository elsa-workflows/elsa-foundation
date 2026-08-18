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
| `IWorkflowActivationAuthority` *(Core — Elsa.Workflows.Runtime.Core)* | `InMemoryWorkflowActivationAuthority` (singleton) | Activation-slot authority and revision CAS must survive restart. **Owned by the runtime, not publishing** — see the note under Authority stores. |
| `IWorkflowActivationCoordinator` *(Core — Elsa.Workflows.Runtime.Core)* | `WorkflowActivationCoordinator` (scoped) | Never, from publishing. **Runtime-owned**, `TryAdd`ed here only so a standalone publishing host can construct the publish handler. The complete activation lifecycle is documented in the [Runtime catalog](../Runtime/EXTENSION_POINTS.md); publishing requests activation and must not hold a parallel copy of the sequence. |
| `IWorkflowExecutableHasher` *(Core — Elsa.Workflows.Runtime.Core)* | `WorkflowExecutableHasher` (scoped) | Never, from publishing. **Runtime-owned** (spec 151, FR-B-010) and `TryAdd`ed here for the same standalone-composition reason: the compiler depends on it, and only `AddWorkflowRuntime()` would otherwise register it. Changing the canonical payload moves every artifact id in existence. |
| `IWorkflowArtifactClosureFactory` | `WorkflowArtifactClosureFactory` (scoped) | The exported closure must be assembled from another store topology. Scoped because it reads the source-reference reader, which durable providers register as scoped. |
| `IPublicationRecordStore` | `InMemoryPublicationRecordStore` (singleton) | Publication lifecycle/audit history must survive restart. |
| `IPublicationPolicyStore` | `InMemoryPublicationPolicyStore` (singleton) | Host/workflow policy and revision CAS must survive restart. |
| `IPublicationProjectionIntentStore` | `InMemoryPublicationProjectionIntentStore` (singleton) | Projection delivery facts and retries must survive restart. |
| `IActivityPublicationReceiptStore` | `InMemoryActivityPublicationReceiptStore` (singleton) | Activity publication outcomes and idempotency bindings must survive restart or be shared across nodes. |
| `IPublicationPolicyResolver` | `PublicationPolicyResolver` (singleton) | A host needs a different policy source while preserving explicit-request precedence and safe defaults. |
| `IPublicationPreflightService` | `PublicationPreflightService` (singleton) | A host adds claim constraints beyond provider cardinality. |
| `IPublicationActivator` | `PublicationActivator` (scoped) | Authority coordination uses another transactional boundary while preserving CAS and compensation invariants. |
| `IPublicationProjectionPreparer` | `PublicationProjectionReconciler` (scoped) | Serving projections use another durable reconciliation mechanism. |

Register replacements before the feature's `TryAdd` defaults, or use `services.Replace(...)`. Persistence
packages that replace a related store family should remove and register the whole family explicitly so a host
cannot accidentally split one authority model between process-local and durable state.

The Groundwork composition (`Elsa.Workflows.Publishing.Persistence.Groundwork`) replaces the four authority
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

- **`IPublicationSlotStore` no longer exists.** Publishing does not own activation authority (spec 151,
  FR-B-006). The definition-keyed ledger is `IWorkflowActivationAuthority` in
  `Elsa.Workflows.Runtime.Core`, with revisioned `TryActivateAsync` / `TryDeactivateAsync` transitions and an
  explicit `WorkflowActivationSource` ownership field; a failed expected revision must leave the slot
  unchanged. There is **one ledger per engine**, so the publish pipeline and the artifact importer cannot
  double-activate a definition. Publishing requests activation through `IWorkflowActivationCoordinator`,
  which owns the complete lifecycle, and retains only compilation, publication policy and its
  `IPublicationRecordStore` attempt journal — that journal is never consulted to decide serving.
- `IPublicationRecordStore` retains publication lifecycle and failure facts and supports conditional status
  transitions.
- `IPublicationPolicyStore` stores the host policy under a null workflow ID and optional workflow overrides;
  writes are revision-checked.
- `IPublicationProjectionIntentStore` durably transitions idempotent prepare/activate/remove intents. Providers
  must preserve deterministic intent IDs, attempt state, retry timing, and failure details across restart.

The supplied Groundwork provider implements these four contracts. Compose
`services.AddGroundworkPublishingStores()`; its document kinds, indexes, CAS behavior, and serializers are
provider-neutral with respect to this engine feature.

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

### Portable artifact export

`IWorkflowArtifactClosureFactory` builds the portable `WorkflowArtifactClosure` for one Published workflow
definition version (FR-B-010): the pinned executable, its complete transitive dependency closure, the exporting
engine's `Published`-scope source references, and the trigger bindings those artifacts own.

- **Destination-agnostic, deliberately.** It returns the closure model and nothing else — never bytes, never a
  file name, never an HTTP concept. Encoding belongs to Runtime's `IWorkflowArtifactClosureSerializer` and
  delivery to `IWorkflowArtifactExportTarget`, so one producer serves a download, a folder writer, a blob push,
  or a test that never leaves memory. Anything transport-shaped on this contract makes the export path
  un-reusable by exactly the callers FR-B-010a exists for.
- **Published-only (FR-B-011).** `TestRun`-scope references are expiring, tied to a `WorkflowTestScope`, and
  carry `draft:`-prefixed version ids. They describe a snapshot that exists only on the engine that minted it,
  so they are never a valid export subject.
- **The closure is complete or it does not exist.** An envelope whose dependency edges resolve only because the
  *importing* store happens to already hold a child is a broken export, so a dependency the exporting store
  cannot produce is a hard failure here rather than a silently thinner file.
- **Failure modes are distinguishable by exception type**, because a transport binding this contract maps them
  to different responses. All four live in `Elsa.Workflows.Publishing.Exceptions` under the abstract
  `WorkflowArtifactClosureException`: `WorkflowArtifactClosureSourceNotFoundException` (unknown version, or a
  Published reference pointing at an artifact the store no longer holds),
  `WorkflowArtifactClosureNotPublishedException` (exists but never published — deliberately distinct from
  "unknown"), `IncompleteWorkflowArtifactClosureException` (unresolved dependencies, carried as structured data
  so a caller can name them), and `WorkflowArtifactClosureCycleException` (store corruption — no
  content-addressed compiler can produce a back edge). A replacement must preserve that taxonomy.

**No `IWorkflowArtifactExportTarget` is registered by this engine.** The destination seam is **fan-in**, and its
first implementation — the `download` target — is contributed by the transport feature, which is where a
destination belongs. An engine composed without one still resolves an empty `IEnumerable<>`, so the absence is a
composition fact rather than a missing dependency. Contribute a target through the
[Publishing API catalog](Api/EXTENSION_POINTS.md#portable-artifact-export-targets).

## Persistence-provider checklist

A Publishing persistence package that backs the engine's authority state should:

1. Implement the three publishing-owned authority stores (`IPublicationRecordStore`,
   `IPublicationPolicyStore`, `IPublicationProjectionIntentStore`) plus `IActivityPublicationReceiptStore` and
   register them as one composition unit. **The activation slot is not among them**: it is
   `IWorkflowActivationAuthority`, backed by the *runtime* Groundwork store family, and a publishing
   persistence package must neither implement nor register it.
2. Enforce unique slot identity and compare-and-swap revisions in storage, not only in process memory.
3. Index publication records by slot and projection intents by publication.
4. Version wire documents and provide upcasters/fixtures for every prior version.
5. Prove restart behavior, stale-revision rejection, idempotent intent replay, and compensation with provider
   tests.
6. Keep Runtime executable/reference/trigger/schedule stores in their owning Runtime persistence module; do not
   move those contracts into Publishing merely because the publish flow consumes them.
7. Persist activity publication receipts by opaque hashed operation identity and prove same-request replay,
   different-request rejection, stale-review no-write, and receipt rollback with every other publication
   document.

The supplied Groundwork provider (`AddGroundworkPublishingStores()`) composes these together with the API
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
- The import counterpart that consumes the envelopes this engine produces: [Workflows Runtime Reconciliation catalog](../Runtime/Reconciliation/EXTENSION_POINTS.md).
- Publication authority decision: [ADR 0043](../../../../docs/adr/0043-publication-slots-define-start-authority.md).
- Repo-wide index: [root extension-point index](../../../../EXTENSION_POINTS.md).
