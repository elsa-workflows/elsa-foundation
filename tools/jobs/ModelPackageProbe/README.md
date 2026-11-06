# Packaged Jobs native and genuine BPMN model witness

This is a separate model-enabled PackageReference consumer. The standalone
`../verify-packaged-jobs.py` closure and forbidden-package oracle remain unchanged.
No source project or test harness is referenced by the generated consumer.

The driver freezes a full source SHA and publish-state SHA, uses the existing version calculator,
packs the complete model closure and verifies each archive's source/fingerprint. A fresh external
consumer restores exact package versions through exact local-feed mappings, restores again in
locked mode and publishes outside the source tree. Its receipt checks every deployed packed
runtime DLL against the archive and retains source, helper, handler, controller, restore, lock,
external package and complete deployment hashes. No package feed is published or mutated.

The actual existing `ControlledReportHandler` and its DTOs are copied from the immutable
`PackageProbe/ControlledReport.cs` handler span. Both models use the same `package.report/2`
support, input/result schemas and implementation identity as the standalone witness. Its two report
lines produce five units and subtotal 52.00. Entry and release files belong to this owned evidence
directory. They control an actual handler invocation, without changing its accepted semantics.

```sh
python3 tools/jobs/verify-packaged-job-models.py \
  --source-ref FULL_SOURCE_SHA --publish-state-ref FULL_PUBLISH_STATE_SHA \
  --evidence-dir /tmp/elsa-jobs-model-evidence --stage execute \
  --m2-receipt /absolute/passing-m2-all-groups/receipt.json \
  --m2-source-ref FULL_M2_SOURCE_SHA --m2-publish-state-ref FULL_M2_PUBLISH_STATE_SHA
```

`pack` and `publish` are separate construction stages. `execute` requires the complete passing raw
SQLite M2 evidence tree and explicitly selected M2 source/publish-state commits. The gate matches
all 27 source-owned case identities and their Passed states, the identical nested plan/cases/hash,
provider engine/version and cleanup, source/controller/harness hashes, complete packed/restored/
deployed package bindings, and version-computation source/fingerprint/command identities. Older
five-case M1 receipts and scalar-only promotions refuse. Retain `source`, `feed`, `deployment`,
`computation.json` and `standalone-witness` beside the package receipt. Canonical relative locations
allow relocating that complete tree without rewriting the hashed receipts' original paths.
The gate records `PRIOR_SOURCE_M2_SLICE_ONLY`; it grants no current-M3-source qualification.
The first execution profile is SQLite with
separate retained Jobs and Runtime files. It creates no database container or external credentials.
Initialization uses actual migrations once; subsequent hosts validate the schema. Exact-scope
operations run through `IPersistenceOperationScopeFactory`. The actual SQLite engine version is read.

Each producer, inspector, handler worker and result coordinator is an independent process running
the published DLL. The handler worker executes no workflow activity. The coordinator has no
registered report handler. Its ordinary native result loop and bounded resumption sweeps consume
the original waits, sources and model inputs. No execution header, bookmark, receipt, grant or ACK
is seeded. The controller stops only sessions it created and retains both stores and all receipts.

Native publication constructs a pinned actual `RunJob` or `SubmitJob` root and optional real Sequence/Flowchart containers through public immutable runtime
contracts and the real executable hasher, saves its Published source reference, then uses normal
`IWorkflowStartDispatcher`. The public CLR `[ResumeTarget]` declaration supplies the target rather
than a private Jobs constant. This direct runtime-material publication is not Design submit/publish.

BPMN publication calls `IBpmnModelRuntime.PublishAsync` and `StartAsync` for a real start event →
ServiceTask → end event model with explicit FinalFault/max1. Its service binds that closed pinned
native `RunJob` or `SubmitJob` worker with fixed useful input. The root is a `RuntimeModelExecutionState` with
protocol `elsa.bpmn/1`; the probe refuses any native workflow header for that root. Inspection uses
public BPMN graph/work/header projections and `IBpmnNativeWorkConsumer`, without parsing a private
BPMN payload. `ProcessId` supplies the logical definition identity across two material versions.

The reviewed twenty-case checkpoint is preserved separately. The extended thirty-four declared cases cover:

- Native/BPMN success and selected handler failure, retaining the useful selected result and exact
  native consumption/grant binding; a fresh inspector verifies one physical handler entry.
- Instance and definition completed-result holds. Actual handlers enter and block first, the pause
  commits, then handlers finish while paused. No grant, ACK or root advancement is allowed until
  release. Definition controls cover two distinct material versions and prove no handler rerun.
- Actual native/BPMN depth-invalid and oversized materialized input. The positive pre-admission
  fault must settle the owning root with zero Jobs admission, zero handler entry and no invented
  consumed-result binding. An early generic materialization fault cannot satisfy this case.
- Exact owner cancellation after Jobs admission, plus BPMN cancellation before helper admission.
  The selected cancellation or positive original-start retirement must settle without helper
  completion being mislabeled as model consumption.
- Helper completion before BPMN root consumption. The first deliverer stops with the actual result
  source owed and Jobs unACKed. Deletion must retain native history. A fresh coordinator consumes
  the original root input, confirms Jobs ACK and permits cohesive helper-history cleanup.
- Exact native/BPMN root-consumed/pre-ACK cuts: a public EF diagnostic observer blocks the actual
  ACK write. Fresh inspection proves consumption and pending responsibility; deletion retains
  history. Entry, Released, TimedOut and Committed records bind the context, transaction, JobId,
  process, occurrence and SHA256 of the actual EF delivery proposal. Only after the cut and retained
  deletion result does the controller issue a matching release request. The case requires both
  unchanged-proposal release and commit of that exact transaction. A timeout remains Failed even
  when a later retry acknowledges the result. No handler invocation is repeated.
- Exact post-COMMIT ACK response loss. The public EF observer correlates the actual delivery write
  and transaction, throws one controlled nontransient DbException after commit, and requires the
  identical exception at TransactionError. A new coordinator resolves retained ACK state; a marker
  or successful transport response alone is insufficient. This is store-response loss, not a claim
  about a dropped network packet.

- Real native Sequence and Flowchart roots await the same handler. A nested Sequence → Flowchart →
  RunJob control retains the exact child invocation/material association while the selected result
  remains held across two coordinator processes, then consumes that same result after release.
  Parent cancellation also reaches the actual nested wait without creating a handler attempt.
- Native and genuine BPMN independent/detached submission completes the parent before any handler
  runs. The typed `PendingAdmission` receipt is distinct from `JobAcceptance`; the probe requires
  exact canonical origin acceptance and the same JobId. No result bookmark, result grant or result
  delivery exists. The retained association still selects workflow pause scopes after completion.
- Explicit cancellation occurs after submission admission while a real native Event keeps the
  parent active, or after the closed BPMN helper completes before its root consumes that result.
  Independent work is cancelled without handler entry; detached work runs once after cancellation.
  Additional nested Sequence → Flowchart → SubmitJob cases preserve the exact leaf association.

Every planned case is retained as Passed, Failed or Not Run. Failure and cleanup failures remain
failures. `fullAcceptanceState` remains Incomplete even if this focused slice passes. The controller
also records the finite remaining plan: PostgreSQL; linked retry association;
pre-wait native cancellation; exact forged/missing-grant runtime mutations for selected
failed/timed-out/cancelled results; grant-before-pause replay; stale process overlap; authoring
publication/import; cross-tenant authorization; and the composed Studio experience. The source unit
controls for exact missing/wrong grants do not become composed-runtime evidence here.

This trusted qualification host accepts only controller-created command files. It is not a product
CLI or an HTTP service. These source-declared cases have not been compiled or executed; root owns remote qualification
after M2 and independent source review.
