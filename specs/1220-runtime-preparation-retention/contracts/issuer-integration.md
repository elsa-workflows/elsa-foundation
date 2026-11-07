# Actual T066 issuer and commit integration — plan R2

**Status**: Owner-coordinated proposal, not an existing issuer. Source verified read-only at agent/codex/3519-publication-integration, commit 3b617af8c86b65d4f21ca37aa2cee35fdf257f78, tree 242f630d48bab6d6b3a90d584f3fc54d975629b4.

## Actual canonical seam

Existing owner files:

- Essentials/src/Workflows/Publishing/Core/Contracts/ISourcePublication.cs: ISourcePublicationStore/PreparationStore, coordinator and typed adapter.
- Core/Models/SourcePublication.cs: immutable request/snapshot/preparation/receipt and operation Revision+State.
- Publishing/Persistence/EntityFrameworkCore/Stores/EfSourcePublicationStore.cs: actual EF operation/head/release CAS; current Write owns and ends its transaction.
- Publishing/Elsa.Workflows.Publishing/Services/SourcePublicationCoordinator.cs: Prepare is outside ledger Write; publication commit follows reservation.
- Publishing/Elsa.Workflows.Publishing/Services/WorkflowSourcePublicationAdapter.cs: exact Runtime source-reference receipt producer.
- Extensions/Agent/src/Specialists/Design/Services/AgentSourcePublicationAdapter.cs: actual Design publication + Runtime reference/template receipt producer.
- Publishing/Persistence/EntityFrameworkCore/Services/EfSourceActivityPublicationCommand.cs: Runtime first, Activities Design commit is Agent linearization point.

Only production issuer candidate is this EF backend. Generic PublicationRecordStore and ActivityPublicationReceiptStore are not through-commit source-owner guards. ISpecialistTurnStore/turn admission generation is inference ownership and is explicitly NOT reused.

## Owner-side implementation responsibility

The actual Publishing owner implements EfSourcePublicationPreparationIssuer and binds it from the existing SourcePublicationCoordinatorFactory using its selected EfSourcePublicationStore and actual typed adapter. No runtime source-kind switch or competing owner registry.

Minimum owner-side changes, delegated through Root (not edited here):

1. Add a TRUE immutable physical IncarnationId generated on canonical operation insertion and independent FenceRevision, preserving serialized/logical operation.Revision. This is absent today; an expiring review-entity incarnation, deterministic operation key/fingerprint or revision zero is not an operation incarnation. Delete/recreate changes it; stale CAS checks it. Atomic initialization/upcast for older rows belongs to that provider.
2. Every competing operation transition, including review/reserve/reject/receipt/release association, checks/increments FenceRevision under its transaction. Keep expected logical Revision+State and immutable operation/source/preparation binding checks.
3. Issuer ExecuteFencedAsync selects topology before an owner transaction. SAME mode creates only callback scope; Join stages owner CAS in Runtime's fresh shared boundary. INDEPENDENT mode holds the real owned ledger guard through callback/Runtime commit.
4. Decision and SelectedRoots become readable only after Join's actual row guard and canonical reads. SelectedRoots come from the actual bound Workflow/Agent adapter reading the WINNING persisted prepared projection, preserving source/producer attribution. Compare its existing Profile/ContentFingerprint against the expected binding separately from Runtime's full physical Selection.Fingerprint. Never use caller-added roots, a losing local compiler result or a Runtime SourceKind policy switch. Terminal proof uses the actual typed adapter and immutable canonical receipt/rejection.
5. Expose this issuer through the selected owner contract/factory, not a caller-created lease or global policy. Actual Runtime provider qualifies the source/material/template/owner tuple.

A portable real FenceRevision increment has affected-row semantics without changing logical Revision alone. Reading a row or doing no-op update/cancellation does not prove a guard. Ledger incarnation prevents delete/recreate ABA.

The canonical owner supplies the minimal concrete adapter seam through its existing factory/store, rather than a Runtime-owned owner engine. Proposed additive owner contract members (default Unsupported for legacy adapters/providers) are:

```csharp
// Existing Publishing.Core.Contracts.ISourcePublicationAdapter:
Task<IReadOnlyList<WorkflowPreparationRoot>> SelectPreparationRootsAsync(
    SourcePublicationPreparation winningPreparation,
    CancellationToken cancellationToken = default);

// Existing Publishing.Core.Contracts.ISourcePublicationStore:
IWorkflowPreparationOwnerIssuer CreatePreparationIssuer(
    ISourcePublicationAdapter configuredAdapter);
```

These are signatures for owner technical review, not landed public ABI. The selected EF store creates/qualifies its own issuer using its actual configured context and bound adapter. Factory invokes this on the actual selected store and binds the returned instance through Runtime's native composition; callers cannot substitute a request-supplied store/issuer. Issuer reads its expected operation stamp from the actual canonical row and checks the trusted owner source access. Actual Workflow selection returns its prepared native identity; Agent returns TemplateRoot using its prepared template identity/hash. Runtime expands/validates full model/template closure; adapter domain restrictions, including current Agent dependency restrictions, remain authoritative.

## Runtime-side responsibility

Runtime supplies the EF-free signature artifacts, scoped facade/factory and opaque session; EF participant owns actual transaction/frame validation. It reuses EfSharedTransaction for SAME mode and dedicated staged metadata writes. Ordinary root-write transaction rejection remains unchanged.

The EF-specific hook exposes actual configured context/topology and fresh shared context/Connection/Transaction. Join cannot take ownership of them. RequireActiveContext validates the native private frame, actual instances and callback; public interface properties are not authority.

Preparation metadata has no native anchor fields. Template-only is a real supported selection, not a fabricated workflow root. Runtime handles full physical closure, explicit GC/query/history and no-start/export/interest semantics.

## Concrete durable compositions required

### A — Same durable SQLite file

Use actual EfSourcePublicationStore on a configured Publishing ledger context and actual RuntimeDbContext stores against the same owned durable SQLite file, with the real Workflow adapter; repeat with the real Agent adapter/Activities Design publication.

Runtime participant creates EfSharedTransaction with both configured context types, using fresh enlisted instances. Native owner CAS, metadata acceptance and relevant Runtime fences share its sole commit. The same-file case must succeed; opening two competing SQLite write transactions is a failing mutation, not Unsupported success.

### B — Independent durable SQLite files

Use separately created/verified owner-ledger and Runtime files; actual owner issuer holds its native guard on owner file A while Runtime commits file B. Agent Design publication may be in its actual independent Design file and must be verified by the real producer. No distributed commit is asserted.

Alias/hard-link/unknown topology controls must refuse; merely unequal strings do not admit independence. Both compositions run all three journeys, stale rev0 versus canonical reject rev1, and real owner-transition exclusion through Runtime acceptance.

No universal Unsupported implementation plus proxy fixtures satisfies these positives. Additional advertised PostgreSQL/MySQL/SQL Server compositions need the same actual-owner/provider conformance and all four migration/readiness paths.

## T066 consumer call sites

- Workflow/Agent Prepare: save immutable canonical preparation projection, then retain Runtime material before returning a successful review. Crash/GC before acceptance must refuse rather than return a durable review.
- Shared coordinator Finish/Reconcile: repair actual canonical receipt/release association first; complete Runtime retention only after verified canonical publication. Lost replies read the same operation/header; never publish again as cleanup.
- Reject/Cancel and Review observing Rejected: establish actual terminal CAS, then complete Runtime rejection cleanup; interrupted source-side or Runtime cleanup resumes independently from that decision.
- Complete's canonical revision/state may be newer than AcceptedOwner's historical revision. Native issuing fence validates current expectation; Runtime verifies same immutable operation/incarnation/selection. No AdvanceOwner policy is needed.
- Actual operation progression is Captured0 → Reviewed1 → Reserved2; actual domain receipt can exist before RecordReceipt/AssociateRelease advances to Published. Current fresh writes must fence the actual legal revision/state. AlreadyRetained can confirm the same header after a legitimate advance; it never creates a missing root after Published or resurrects a terminal header.
- Verified terminal decision identity excludes later operation revision/state and observation stamps. Reserved2 plus actual domain commit and later Published must map to the same immutable publication decision/selection. Missing receipt never supplies rejection.
- Terminal metadata replay matching the actual immutable owner/incarnation/prepared binding and ClosedFrom/current terminal token confirms prior completed facts without requiring collected material/reference members or inventing a fresh owner claim. A new canonical operation incarnation cannot reuse the old result.

These owner-side changes are an explicit integration dependency of this leaf. Do not claim Runtime preparation delivered until the real producer/backend paths and both durable topologies pass. Source coordination is not a request for owner permission or whole-program acceptance.
