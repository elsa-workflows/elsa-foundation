# Publication Slots Define Start Authority

Status: accepted (2026-07-13); **superseded in part** by
[ADR 0069](0069-activation-ownership-is-explicit-and-runtime-owned.md) (2026-08-18) — see Superseded in part
below.

Related decisions: ADR 0038 (content-addressed executable identity), ADR 0039 (layout on source
references), and ADR 0040 (reference- and execution-derived artifact lifetime).
Plan of record: `specs/092-domain-owned-apis/`.

## Superseded in part (2026-08-18)

Two mechanisms decided here were replaced by
[ADR 0069](0069-activation-ownership-is-explicit-and-runtime-owned.md), which is the standing decision on
activation ownership and is where a reader should go; it was implemented under
[spec 151](../../specs/151-executable-artifact-reconciliation/spec.md). The original text is retained below as
the historical record rather than rewritten, following
[ADR 0042](0042-elsa-foundation-ships-only-groundwork-persistence-implementations.md#superseded-framing-retained-for-provenance).
The authority model itself — the slot as the sole start authority, and its revisioned compare-and-swap — is
unchanged; what moved is where that ledger lives and how projection delivery is driven.

**1. The durable projection-intent ledger is withdrawn.**
[Projection intent makes cross-store activation durable](#projection-intent-makes-cross-store-activation-durable)
decided that Publishing records durable `PublicationProjectionIntent` entries, delivered idempotently and
converged afterwards by a reconciler. That is no longer the delivery mechanism, and none of it exists:
`IPublicationProjectionPreparer` and its `PublicationProjectionReconciler` default were deleted, along with
`IPublicationProjectionIntentStore`, the `PublicationProjectionIntent` model and the `publishingProjectionIntent`
storage unit. **What replaced it:** `IWorkflowActivationCoordinator` in `Elsa.Workflows.Runtime.Core` owns the
prepare → CAS → activate → notify sequence and its compensation, in **both** lifecycle directions — publishing's
unpublish handler calls `DeactivateAsync` instead of driving a retraction of its own. The sequence is in-process
and its recovery is the next request rather than a replayed intent, so there is no delivery record to converge.
The reason for the change is the reason the ledger existed: two components had to know the same ordering
invariant independently, and they drifted apart. Consequently the "projection-intent models" named under
Consequences, and the `PendingProjection` state and reconciler in that section, no longer describe the system.

**2. The slot ledger moved out of Publishing.** `IPublicationSlotStore` was deleted rather than relocated
(spec 151, FR-B-006). The definition-keyed ledger is now `IWorkflowActivationAuthority` in
`Elsa.Workflows.Runtime.Core`, with an explicit ownership field, so one engine has exactly one activation ledger
that the publish pipeline and the artifact importer share. Slot semantics are as decided here; only the owning
domain and the contract name changed. See the
[Publishing engine catalog](../../src/Elsa/Workflows/Publishing/EXTENSION_POINTS.md) and the
[Runtime catalog](../../src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md).

## Context

Publishing currently appends a Published source reference and indexes trigger bindings for each
publish. If an HTTP-triggered workflow is published at `/foo`, edited to `/bar`, and published
again, both artifacts can remain eligible to start new executions. Elsa needs that side-by-side
behavior, but making it the implicit result of ordinary publishing is surprising and unsafe.

Artifact identity cannot express publication intent. Executables are immutable and content-addressed,
so two publications may intentionally share an artifact while differing in lifecycle, slot, policy, or
serving state. A source reference records provenance and retention but is not by itself a sufficient
authority for accepting new work. Trigger bindings, schedules, and in-memory HTTP route tables are
serving projections; allowing any of them to become independent authority permits partial failures and
restart drift.

Publishing also crosses stores that may not share one transaction. Compilation, source-reference
creation, trigger indexing, recurring-schedule projection, and HTTP route projection can fail at
different points. A replacement must never disable the current publication because preparation of its
candidate failed, and a successful response must not conceal a partially activated candidate.

## Decision

### A publication slot is the sole start authority

Publishing owns a `PublicationSlot` identified by `(WorkflowDefinitionId, SlotName)`. A slot selects
zero or one active `PublicationRecord`. Only the publication selected by an authoritative slot may
contribute trigger bindings or recurring-start schedules visible to new-start routing.

Neither an executable artifact, a source reference, a trigger binding, a schedule, nor an in-memory
route independently grants start authority. They carry or project the publication identity selected
by the slot.

A publication record has a unique publication ID even when it reuses an existing content-addressed
artifact. It records the target slot, Design version, artifact, source reference, expected slot
revision, lifecycle status, and audit timestamps. Publication history remains append-only except for
controlled lifecycle transitions.

### Default publication replaces; named slots coexist explicitly

Every workflow definition has the conventional slot name `default`. An ordinary publish request with
no explicit slot resolves to the default slot and replaces its current authority.

Publishing side by side requires an explicit, meaningful named slot. Distinct named slots may remain
active for the same definition when their trigger claims satisfy cardinality rules. Implementations do
not generate an implicit side-by-side slot name, and repeated ordinary publishes do not accumulate
unnamed active publications.

Soft-deleting or restoring a Design definition does not change its publication slots. Studio may offer
a coordinated user flow, but Design deletion and Publishing unpublish remain separate operations.

### Policy resolution is deterministic

Publishing resolves intent in this precedence order:

```text
explicit publish request > per-workflow publication policy > host publication policy
```

The safe host default is replacement of the `default` slot. A per-workflow policy may require an
explicit slot or select another declared default, but it cannot silently turn an ordinary publish into
unlabelled coexistence. The resolved action, slot, and policy source are returned by preflight so a
management client can display the actual effect before confirmation.

### Trigger providers declare cardinality

Every start-trigger provider declares one of:

- `Exclusive`: one authoritative publication in a shell may claim the normalized stimulus identity.
- `FanOut`: multiple authoritative publications may intentionally receive the same stimulus.

HTTP endpoint triggers are Exclusive. Event and timer providers may declare FanOut when their delivery
semantics support it.

Preflight derives publication-scoped trigger claims and reports added, removed, retained, and
conflicting claims. An Exclusive candidate is checked against all other authoritative slots in the
active shell. The only excluded claimant is the old publication being replaced in the candidate's own
slot. Sharing a workflow definition or executable artifact is not an exemption.

Trigger bindings and recurring schedules carry `PublicationId` and `SlotId`. Their identities and
store operations are publication-scoped so two named slots may point to the same artifact without
collapsing into one lifecycle.

### Activation is prepared first and committed with compare-and-swap

Publishing performs a replacement in this order:

1. Resolve policy and target slot.
2. Compile and validate the candidate executable.
3. Derive claims and preflight conflicts against current authoritative slots.
4. Save or reuse the immutable artifact in a staged, creation-grace-protected state.
5. Prepare every required serving projection without exposing it to routing.
6. Compare-and-swap the slot using the revision observed by the attempt.
7. As one logical activation, select the candidate, create its live Published source reference, retire
   the replaced publication and reference, and switch serving projection visibility.

Every successful activation or unpublish increments the slot revision. Concurrent attempts from the
same observed revision have at most one winner. Conflict validation is repeated or protected inside
the activation boundary so concurrent Exclusive claims cannot both activate after passing stale
preflight reads.

If compilation, validation, preparation, activation, or compare-and-swap fails, the old publication
remains authoritative and its serving projections remain active. A failed candidate is recorded as
failed or cleaned up; it never partially displaces the current authority.

Unpublish clears slot authority and retires its publication through the same revisioned transition.
Restore is a new authority transition: it rechecks artifact availability, policy, trigger conflicts,
and projection preparation before compare-and-swap. It is not implemented by merely clearing a
retirement timestamp.

### Projection intent makes cross-store activation durable

When the slot store and every serving projection cannot share a transaction, Publishing records
durable `PublicationProjectionIntent` entries. An intent identifies the publication, projection kind,
operation, delivery status, and bounded retry diagnostics.

Intent delivery is idempotent by publication, projection kind, and operation, and is safe to replay
after process restart. Prepared candidate projections remain invisible. Until every required
preparation succeeds, the candidate is `PendingProjection` and the prior slot publication remains
authoritative. The API may report pending status; it must not report successful activation early.

The final slot compare-and-swap and the visibility switch must be atomic from a workflow starter's
perspective. A reconciler repairs or removes orphaned prepared projections and converges delivered
intent with slot authority. Projections always converge toward the slot; they never override it.

### Source references record publication provenance and lifetime

A Published `WorkflowExecutableSourceReference` carries its `PublicationId` and `SlotId`. It is live
publication provenance only while it is not retired and its publication is active. Publishing owns
creation, retirement, and restoration of these references. Runtime may expose them as read-only
executable provenance.

TestRun references remain outside publication slots: they identify a persisted Design version or
draft snapshot, expire according to test-run policy, and do not grant production start authority.

Retiring or replacing a publication does not delete its executable. Existing workflow executions
continue with their pinned artifact. In accordance with ADR 0040, garbage collection retains the
union of artifacts named by live source references and artifacts pinned by retained workflow
execution records. Completed, faulted, suspended, canceled, and running executions protect their
artifact for as long as their execution record is retained.

## Invariants

1. A `(WorkflowDefinitionId, SlotName)` pair identifies exactly one slot.
2. A slot selects zero or one active publication, and a publication is active in at most one slot.
3. Only the selected active publication contributes new-start routing projections.
4. Ordinary publishing replaces `default`; intentional coexistence uses explicit named slots.
5. An Exclusive stimulus has at most one authoritative claimant per shell; FanOut claims may coexist.
6. A failed or losing candidate leaves prior slot authority and serving behavior unchanged.
7. Publication identity and lifecycle never change immutable artifact identity.
8. Source references and serving projections reflect slot authority; they do not create authority.
9. Definition soft deletion does not implicitly unpublish.
10. Replaced publications do not affect executions already pinned to their artifact.

## Considered Options

- **Keep append-only publishing as the default.** Rejected because ordinary edits unintentionally
  preserve old triggers and make publication intent implicit.
- **Delete the old executable during replacement.** Rejected because artifacts may be shared and
  retained executions must continue to resume and remain inspectable.
- **Use artifact or definition identity as authority.** Rejected because neither distinguishes named
  publication intent, especially when content-addressed artifacts are reused.
- **Best-effort index the candidate and retire the old publication afterward.** Rejected because
  failure exposes duplicate, missing, or partially switched routing.
- **Make every trigger Exclusive.** Rejected because event and timer providers may intentionally
  support deterministic fan-out.
- **Require a distributed transaction across all projections.** Rejected as a universal requirement
  because supported providers may span transactional boundaries; durable intent and reconciliation
  preserve the invariant without imposing one storage technology.

## Consequences

Ordinary Elsa publishing regains intuitive replacement semantics while explicitly supporting the
long-requested side-by-side scenario. Studio and other supported clients must preflight publication,
show the resolved slot/action, require a name for coexistence, and surface Pending or Failed states.

Publishing gains durable slot, publication, policy, trigger-claim, and projection-intent models plus
compare-and-swap persistence. Trigger and schedule stores gain publication-scoped operations, and
providers must declare cardinality. Persistence providers need unique slot identities, revisioned
writes, indexes, serialization versions, and restart-safe intent processing.

The activation path is more involved than append-only indexing, and temporarily prepared artifacts or
projections may require reconciliation cleanup. In return, there is one auditable source of truth for
which executable may start new work, concurrent publishers have a deterministic winner, partial
failure cannot silently displace production behavior, and old executions remain safe.
