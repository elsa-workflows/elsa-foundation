# Preparation retention validation — plan R3

**Status**: Planned checks, NOT RUN. No build/test/host command is authorized or executed by this planning packet.

## Actual prerequisite and setup

Integrate the reviewed Runtime contract/provider changes with the actual T066 owner-side issuer and transition fences named in [issuer integration](contracts/issuer-integration.md). Do not substitute a fake owner, Specialist turn store or generic PublicationRecordStore.

Prepare two production-class durable SQLite compositions: A uses the SAME owner-ledger/Runtime file via EfSharedTransaction; B uses actual independently created/verified owner and Runtime files. Use actual SourcePublicationCoordinator, EfSourcePublicationStore, WorkflowSourcePublicationAdapter and AgentSourcePublicationAdapter/real Design commit path. Compiler outputs may be controlled fixtures; operation ownership, publication receipts, Runtime/template stores and transaction boundaries may not be fake.

Each composition must execute all three journeys below. Memory/default Unsupported controls do not replace either positive. Use owned disposable stores and new processes; preserve existing data. Heavy work stays sequential/MySQL-first under the actual remaining grant; this guide allocates no host or budget.

## Journey 1 — Review survives restart/GC; zero runnable effects

Retain a real Workflow candidate with complete native/model/reusable-template dependencies and a real Agent template-only candidate. Include nested/direct template closure in provider controls without relaxing Agent's existing domain dependency rules.

Cold-restart owner and Runtime processes, then run two ordinary GC sweeps beyond staging grace with no Published/TestRun/execution roots. Compare every exact identity/incarnation/content. Attempt explicit, artifact-only legacy, direct/trigger/child/model/replay starts and runnable export/import/interest selection; expect zero created execution, command, activation, binding, runnable export or interest. Independent valid published/test/retained-execution authority still works over identical material.

Required controls:
- Delay actual operation rev0 command; canonical reject rev1 wins; release delayed commit: NO preparation root appears.
- Enter actual owner guard, attempt review/reserve/reject concurrently: transition cannot pass through Runtime acceptance; after release, stale expected revision/state conflicts.
- Replace actual material/template/hash-claim incarnation, corrupt closure/projection, expire material grants during a lock wait, forge session/frame/issuer/participant or invoke after callback: refuse with no partial acceptance.
- Replace/delete/recreate the canonical operation row with identical key/request/revision zero but a new TRUE insertion incarnation: old owner expectation refuses. An expiring review row's incarnation cannot satisfy this check.
- Race two compiled preparations; verify only the actual persisted winner's Profile/ContentFingerprint, original source attribution and typed selected roots can be retained. Change that winner under a delayed query/retain: conflict before root writes. Assert owner content fingerprint and Runtime physical selection fingerprint remain separate and unchanged.
- SAME file succeeds with one shared boundary; accidental second owner SQLite writer self-block is a failing mutation. Caller ambient/pending contexts and unknown/aliased independent files refuse.

## Journey 2 — Canonical transfer/acknowledgment recovery

Interrupt before canonical commit: preparation stays. Commit actual Workflow reference or Agent Design publication plus Runtime reference/template, lose acknowledgment and restart. Reconcile the real source operation/receipt/release, then complete under the actual current owner guard. Publication retention precedes preparation closure with no GC gap.

Lose Runtime metadata commit/release reply; query exact header and canonical operation. Do not publish a second time. Agent Runtime-only write before Design commit MUST remain pending/unknown; missing Design publication, wrong snapshot/material/scope/receipt or unavailable evidence never permits release. Test publication-source revision changes during final Runtime validation.

Advance actual Captured0 → Reviewed1 → Reserved2. Replay Retain under each current legal fence for the same accepted preparation, then verify actual domain commit while the operation is still Reserved2 and after later RecordReceipt/AssociateRelease reaches Published. AlreadyRetained must tolerate legitimate current revision advance for the same immutable binding; stale expected revisions still fail fresh writes. Initial transfer verifies actual Published protection in both phases, and the canonical decision fingerprint remains identical despite later verification revision/state. Complete with an old AcceptedOwner full stamp must not be the equality rule.

Retain an actual Agent template, capture the accepted selection/token and native template revision, then publish through the REAL Agent adapter/Design commit path. Confirm EfActivityPublicationRuntimeCommit → StagePublicationFencesAsync advances the template row revision while content/hash and row/hash-claim incarnations stay unchanged. Retry Retain with the accepted selection, then Complete with its accepted token: AlreadyRetained preserves that confirmation, and initial transfer succeeds without rewriting the accepted pins or Selection.Fingerprint. Reconciliation/replay keeps the same canonical DecisionFingerprint despite fresh revision observations. Race another revision advance inside a current attempt to prove fresh native CAS still detects it; replace content or either physical incarnation to prove refusal. Run on both actual durable topologies.

Run this on SAME and INDEPENDENT compositions. An owner guard-release failure after Runtime commit preserves Unknown and actual readback; no two-store atomicity is claimed.

## Journey 3 — Reject/interrupted cleanup/post-GC replay

Record actual source-operation Rejected CAS; interrupt release before/after Runtime commit. Restart and repeat completion. The typed metadata row distinguishes Retained versus closed even after every native/model/template member is collected.

Retain another preparation/publication sharing material; closing one never removes its roots. Replay stale retain/complete, old operation revision/state and old physical owner/header incarnation after GC. No terminal preparation resurrects. Header has no native/template anchor to recreate, and ordinary source GC never deletes it.

Confirmed terminal replay needs no material/root grants. Missing terminal evidence is not rejection; cancellation/timeout alone cannot close preparation.

Lose completion acknowledgment with the original accepted token, advance the canonical owner legitimately, collect all ordinary material/reference members, then replay completion using ClosedFrom and the current terminal token. Both confirm AlreadyReleased from the checked immutable terminal fact; neither recreates material, re-publishes, revalidates a gone anchor or changes the decision fingerprint. Changed owner/header incarnation, prepared winner, selection or impossible token refuses. Replaying Retain after closure never resurrects it.

## Provider/schema and whole-project checks after implementation

Update and qualify RuntimeArtifact metadata plus canonical owner's coordination fields in all four provider migration/designer/snapshot/upcaster/readiness chains, preserving old rows. Advertised server compositions require the same actual-owner tests on PostgreSQL/MySQL/SQL Server; not-run/skipped cases are not support.

Existing whole projects for future affected checks:

    dotnet test Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
    dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj
    dotnet test Essentials/tests/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.csproj
    dotnet test Essentials/tests/Workflows/Publishing/Tests/Elsa.Workflows.Publishing.Tests.csproj
    dotnet test Extensions/Agent/tests/Specialists/Tests/Elsa.Agent.Specialists.Tests.csproj
    dotnet test Essentials/tests/Workflows/Runtime/Reconciliation/Tests/Elsa.Workflows.Runtime.Reconciliation.Tests.csproj
    dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj

Use existing native ProviderTests for actual server engines, one at a time. No test is run during planning.

## Receipt/oracle

Retain exact source/tree, both actual provider identities/topology, issuer/participant/session provenance, canonical operation revision/state and physical incarnation/fence revision, exact domain receipts, material/header snapshots, raw process exits/logs, GC outcomes and cleanup. Bind real guard-removal mutations to stale revision, same-file participation, Agent Design evidence, template-only collection, private-frame and post-GC replay oracles.

Map actual results to SC-001–SC-005, keeping passed/failed/skipped/unknown/not-run distinct. Source/API/schema declarations or proxy tests alone cannot deliver Runtime retention or T066.
