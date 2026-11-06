# Preparation retention research — plan R3

**Status**: Concrete proposal for review; no implementation/runtime qualification.

## R1 — Typed unanchored retention document

**Decision**: Store one typed preparation-root document in the existing canonical source-reference backend, in a new RuntimeArtifact metadata table. Its root/closure arrays use existing executable/model identities and template ID/hash identities plus real physical observations. It has no WorkflowExecutableSourceReference, ArtifactId anchor or DefinitionId/version fields.

**Reason**: The R1 reuse of ordinary reference projections was invalid for a directly selected template. Templates do not contain native definition identity. A permanent terminal document must also survive after every material row is gone.

**Alternatives**: Fake native fields/wrappers invent material. Relaxing all ordinary reference nullability/discriminators spreads change across existing readers. A new model store duplicates authority. One typed metadata document inside the existing provider preserves material authority and needs no member-reference table.

## R2 — Actual owner, not a turn or generic publication journal

**Decision**: Consume the real T066 SourcePublicationCoordinator/ISourcePublicationStore/PreparationStore and typed Workflow/Agent adapters in local integration 3b617af8c86b65d4f21ca37aa2cee35fdf257f78/tree242f630d48bab6d6b3a90d584f3fc54d975629b4. This is unqualified/unpublished source, not delivered capability.

**Reason**: Generic PublicationRecordStore has lifecycle CAS but is not this owner. ISpecialistTurnStore has genuine admission generation/leases for inference turns, not publication. Borrowing either would invent policy. Current canonical publication operation already has optimistic Revision+State; those are the expected generation/state fence, not an exclusive worker claim.

**Delta**: Publishing owner implements a real EF issuer and transition participation. Physical ledger IncarnationId/FenceRevision are new coordination fields, independent of serialized operation.Revision. All operation updates must honor the actual row guard. Runtime does not acquire/take over publication ownership.

Actual operation insertion incarnation is absent. Add a true insertion value and delete/recreate CAS; do not borrow expiring review-entity incarnation or infer identity from deterministic key/request/revision zero. AcceptedOwner is historical: current legal Captured0/Reviewed1/Reserved2/Published progression is verified by the canonical owner without requiring whole-stamp equality.

## R3 — Actual same-physical participation

**Decision**: Reuse EfSharedTransaction.BeginAsync and its fresh shared contexts. Runtime's dedicated participant owns begin/commit/disposal. The issuer Join stages operation revision/state/incarnation/FenceRevision CAS using the fresh enlisted owner context.

**Reason**: Holding an owner SQLite write transaction while an independent Runtime writer targets the same file self-blocks. Existing Runtime RootWrites rejects existing/enlisted/ambient transactions. Do not relax that guard; introduce the explicit staged preparation path instead.

**Details**: The actual helper requires one relational provider, exact nonempty equal connection strings, one instance per concrete type and public own-options constructors. Deduplicate an identical configured context instance once; reject unrelated duplicate types the helper cannot distinguish. Verify exact fresh-context/shared Connection/Transaction identities and actual native tuple; textual mismatch never authorizes independent fallback. BeginOperationAsync gives a non-owning module handle; rollback/incomplete participation marks the whole owner rollback-only. Save enlisted changes and finish every module operation before the sole commit; helper refuses unsaved changes or active participation.

## R4 — Actual independent stores

**Decision**: A genuine owner native guard remains held through Runtime commit/disposal only when physical independence is proved. Initial positive proof uses two separately created durable SQLite files and actual owner/Runtime production classes.

**Reason**: Different connection strings can alias the same file. Native identity/creation evidence is needed; aliases/unknown compositions refuse. Owner commit-loss after Runtime acceptance is reconciled from actual metadata and canonical owner; no distributed atomicity is claimed.

## R5 — Lease callback and decision lifetime

**Decision**: Root manager callback contains issuer callback, owned/shared Runtime transaction, staging, commit and disposal. The manager releases grants only after both callbacks end. Template-only uses no empty root-manager call.

Opaque OwnerSession is minted by the Runtime coordinator only inside the configured issuer callback. Core construction is internal with same-domain Runtime friend access; Publishing creates its own lease, not sessions. Native provider checks actual private frame/session/issuer/participant instances and callback lifetime.

Decision and SelectedRoots are live lease-backed values: they refuse until Join successfully stages the real owner guard and performs actual canonical reads. StageComplete cannot consume an early copied receipt, unjoined lease, mismatched frame or fake EnsureCurrent implementation.

SelectedRoots must come from the actual typed adapter's winning persisted prepared envelope. Its Profile/ContentFingerprint remains the existing owner fingerprint; Runtime recomputes a separate full physical selection/closure/incarnation fingerprint under Join. No source-kind switch, losing producer DTO or authored/prepared fingerprint rewrite is permitted.

R3 correction: actual EfActivityPublicationRuntimeCommit calls StagePublicationFencesAsync, whose native template fence increments row.Revision even when material content and row/hash-claim incarnations are unchanged. Therefore TemplatePin.Revision is only a historical observation. Stable selection comparison/fingerprinting projects identities/content hashes/incarnations and excludes mutable CAS/lease observations; every attempt refreshes/checks fresh native CAS observations under its fence without changing accepted pins, fingerprint or token. Stable canonical decision hashing uses that same stable selection fingerprint and excludes mutable verification observations. Keeping revision in whole-record/JSON identity would incorrectly reject real Agent publication replay/transfer; removing fresh CAS validation would instead weaken fencing. No C# shape change is needed.

## R6 — Real terminal producers

**Decision**: Workflow completion verifies exact committed source-reference publication. Agent completion verifies its actual Activities Design publication and matching Runtime reference/template, using actual adapter FindCommittedAsync. Runtime-only writes are not Agent publication.

Prepare returns reviewable material only after canonical projection is saved and Runtime retention accepted. Canonical Finish/Reconcile first repairs receipt/release state, then completes retention under a current operation fence. Rejection uses actual Rejected state. Missing/unavailable evidence keeps retention; no implicit failure or extra publication.

The actual domain receipt may precede RecordReceipt/AssociateRelease while the operation remains Reserved2. That receipt and the later Published state produce one stable decision fingerprint, excluding mutable verification revision/state. Initial transfer checks actual Published protection. Terminal AlreadyReleased replay checks permanent immutable metadata plus ClosedFrom/current token after material GC and does not re-require a gone anchor.

## R7 — Query, GC and executable semantics

**Decision**: Explicit preparation queries page checked metadata in the actual scope. GC unions retained typed native/model/template closure in initial and final guarded reads. Template-only/nested templates are protected independently of execution roots. Terminal metadata remains but retains no material.

The reference-less legacy fallback must query retained AND terminal preparation history: material known only as preparation is not implicitly runnable when ordinary references are absent. Published/TestRun requiredScope allowlist runs before keyed replay/material/effects. Existing valid independent authority still works; export/import and effective interest eligibility remain Published-only.

## R8 — Schema and implementation ownership

**Decision**: R1's no-DDL proposal is superseded before migration work. Add one Runtime metadata table under RuntimeArtifact. Publishing owner adds its own physical coordination fields in PublishingLedger. Preserve existing rows/material hashes, allocate actual chain versions at implementation, and update all four provider chains/readiness/upcasters.

**Reason**: These are metadata changes in canonical backends, not another model/owner store. No schema number, current issuer support or compiled public API is claimed. All three positive journeys on real same/independent SQLite compositions are delivery gates; universal Unsupported plus test doubles is incomplete.

## Outcome

All four plan R1 findings have concrete R2 dispositions. R2 acceptance/standards and held API reviews were reported clean; R3 addresses the remaining correctness finding above and awaits focused review. Public signatures remain unchanged in the held C# design artifacts; source/owner dependencies and real composition are explicit. No new owner policy, architecture waiver or requirements change is requested.
