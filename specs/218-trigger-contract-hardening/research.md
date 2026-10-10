# Research: Trigger Publication Contract Hardening

## D1 — Trigger authority is layered

**Decision**: Keep the existing sequence: authored declaration or catalog fallback → compile-time executable marker → provider recognition/materialization. Runtime preflight reads only `WorkflowExecutable` and configured runtime providers.

**Rationale**: PR #621 already proved that persisting corrected CLR trigger classification into an existing same-version catalog row creates reconciliation hash conflicts. Compiler projection fixes runtime behavior while preserving catalog identity and the Design-free runtime boundary.

**Alternatives considered**: Catalog-only authority (rejected: upgrade hash conflict and stale persisted classification); provider-only authority (rejected: every trigger-capable node would be probed without explicit executable intent); CLR metadata at runtime (rejected: runtime would depend on authoring/CLR discovery rather than the published artifact).

## D2 — Every classified node has exactly one recognizing provider

**Decision**: Classify `IActivityTriggerStimulusProvider` as a sanctioned Strategy set selected by executable-node context, not as a data-contribution fan-in. Evaluate all configured strategies for each executable trigger node. Zero claims fail as unrecognized; more than one claim fails as ambiguous with sorted provider identities; one claim succeeds even when it returns zero descriptors.

**Rationale**: Current first-provider-wins behavior makes correctness registration-order-dependent and cannot explain which provider owned the node. Exact-one recognition converts a hidden DI ordering rule into an explicit contract.

**Alternatives considered**: Keep first-provider-wins (rejected: silent ambiguity); model providers as §2.6.1 data contributors (rejected: the consumer selects exactly one algorithm by node context rather than aggregating contributed data); claim the rare §2.6.5 sync-contributor exception (rejected: providers return data, so the behavior-not-data criterion fails); introduce provider priorities (rejected: encodes conflict resolution instead of detecting an invalid ownership graph); require global uniqueness of stimulus hashes (rejected: valid fan-out is provider-specific).

## D3 — Stable provider identity is additive and non-persisted

**Decision**: Expose `ProviderId` additively on the provider seam with a source/binary-compatible default derived deterministically from the provider's public CLR identity. First-party providers override explicit stable constants. Include the identifier in the preflight node outcome and failures, but do not add it to `WorkflowTriggerBinding` in Unit A.

**Rationale**: Unit A must identify the recognizing provider, including `Recognized([])`, without forcing a trigger-binding schema migration or prematurely designing Unit B diagnostics persistence.

**Alternatives considered**: Required abstract interface member (rejected: Core major break); persisted `ProviderId` on every binding (rejected: schema bump and no binding exists for intentional non-start); arbitrary provider metadata key (rejected: untyped and invisible for zero-binding outcomes).

## D4 — Add a non-persisted preflight outcome without changing existing indexing signatures

**Decision**: Add an additive preflight-evaluation surface to the existing extractor contract while preserving `Extract(WorkflowExecutable)` and `IWorkflowTriggerIndexer.IndexAsync(WorkflowExecutable)`. The default extractor produces `WorkflowTriggerPreflightOutcome`; `Extract` remains the binding-only compatibility projection. The indexer consumes the full outcome for validation and then applies the existing replacement flow.

**Rationale**: The outcome is needed for exact provider ownership and intentional non-start visibility, but neither the durable binding document nor the public publication response should change in Unit A.

**Alternatives considered**: Change `Extract` return type (rejected: major source/binary break); add a new persisted publication-status document (rejected: duplicates current registered reality); return diagnostics from `PublishedWorkflowView` (deferred to Unit B).

Descriptor duplicate validation uses the deterministic result identity: two candidates are duplicates only when `WorkflowTriggerBinding.BuildId(artifactId, executableNodeId, stimulusHash)` is equal. Different nodes or distinct stimulus hashes remain valid, preserving provider-owned multi-binding and cross-artifact fan-out.

## D5 — Pre-materialize recurring schedules locally

**Decision**: `RecurringTriggerScheduleIndexer` computes the complete Timer/Cron schedule set in memory before calling the inner trigger indexer. Provider/calculator errors and missing future occurrences fail at this stage. After successful preflight, the existing binding replacement runs, followed by replacement with the already-materialized schedule set.

**Rationale**: Today the decorator mutates bindings first, then discovers schedule failures. Reordering pure materialization closes validation-driven partial mutation with no new Core abstraction or store contract.

**Alternatives considered**: Generic provider-neutral projection requirements/candidates in Runtime Core (rejected: speculative abstraction for one current projection); new cross-store unit of work (rejected: publication-wide transactionality is explicitly out of scope); compensate/rollback after failure (rejected: unsafe under concurrency and unsupported by store APIs).

## D6 — Exhausted Cron is invalid for a start trigger

**Decision**: A Cron start trigger whose expression has no future occurrence fails preflight with artifact, node, provider, and expression context. It is not logged-and-skipped.

**Rationale**: A successful publication with a start trigger that can never be scheduled violates the no-silent-unroutable target. Intentional non-start must be expressed through recognized-empty semantics, not an exhausted schedule.

**Alternatives considered**: Warning plus zero schedule (rejected: current silent-success class); persist an exhausted schedule (rejected: it can never fire); treat exhaustion as intentionally non-starting (rejected: conflates authored activation with invalid schedule materialization).

## D7 — Preserve persisted shapes in Unit A and republish for corrected classification

**Decision at spec 090 delivery**: Do not change catalog rows, executable wire shapes, trigger-binding documents, or recurring-schedule documents in Unit A. The executable shape current at that delivery remained readable; corrected classification and preflight behavior applied to newly published artifacts. Any implementation-discovered need for a durable shape change reopened this decision and required explicit Groundwork versioning.

**Rationale**: This is the smallest compatibility-preserving unit and follows the repository's content-addressed artifact and schema-evolution rules.

**Alternatives considered**: Mutate catalog execution type (rejected by PR #621 evidence); retroactively rewrite executable artifacts (rejected: separate migration policy); persist preflight outcomes (rejected: Unit B concern).

**Superseded persistence boundary**: Later persistence work intentionally made every Runtime Groundwork kind
current-only before GA: minimum-readable equals current, only the current fixture is retained, and no Elsa
upcaster is registered. `workflowExecutable`, `workflowExecutableSourceReference`, and
`workflowExecutionState` are version 4 and reject versions 1 through 3 before deserialization. Executable v4
includes the reusable-activity input contract and direct dependency snapshot; source-reference v4 includes
tenant scope; workflow-execution v4 includes dispatch nesting depth. The safe upgrade operation
is to atomically reset the complete Runtime and Publishing Groundwork persistence sets while preserving Design
and Activities data, then
republish workflows before serving traffic. This supersedes only D7's executable/source-reference/workflow-execution readability
assumption; the catalog immutability and republish decisions remain in force.

## D8 — Semantic safety, not cross-store transactionality

**Decision**: Guarantee that all deterministic/provider-owned validation finishes before trigger or schedule replacement. Do not claim rollback for infrastructure failures after writes begin, and do not reorder executable/source-reference persistence in `PublishWorkflowRequestHandler`.

**Rationale**: Current stores expose delete and per-item save operations without a shared transaction/CAS boundary. A truthful semantic guarantee is reviewable; a simulated rollback would be concurrency-unsafe.

**Alternatives considered**: Batch replace on both stores (rejected: still not atomic across stores); snapshot and restore (rejected: races with concurrent republish); publication-wide transaction (explicitly outside approved scope).

## As-built confirmation

D1–D8 were implemented without reopening a decision during spec 090. Compatibility tests confirmed the
durable and executable shapes then in scope were unchanged; provider/extractor compatibility confirmed the
additive Runtime Core surface; and the full verification evidence is recorded in
[quickstart.md](quickstart.md). The later current-only pre-GA clean break documented under D7 is a
separate persistence decision and is now the operative executable/source-reference/workflow-execution contract.
