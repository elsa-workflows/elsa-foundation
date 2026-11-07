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
  --evidence-dir /tmp/elsa-jobs-model-evidence --provider sqlite --stage execute \
  --m2-receipt /absolute/passing-m2-all-groups/receipt.json \
  --m2-source-ref FULL_M2_SOURCE_SHA --m2-publish-state-ref FULL_M2_PUBLISH_STATE_SHA
```

`pack` and `publish` are separate construction stages. `execute` requires the complete passing raw
M2 evidence tree for the selected provider and explicitly selected M2 source/publish-state commits. The gate matches
all 27 source-owned case identities and their Passed states, the identical nested plan/cases/hash,
provider engine/version and cleanup, source/controller/harness hashes, complete packed/restored/
deployed package bindings, and version-computation source/fingerprint/command identities. Older
five-case M1 receipts and scalar-only promotions refuse. Retain `source`, `feed`, `deployment`,
`computation.json` and `standalone-witness` beside the package receipt. Canonical relative locations
allow relocating that complete tree without rewriting the hashed receipts' original paths.
The gate records `PRIOR_SOURCE_M2_SLICE_ONLY`; it grants no current-M3-source qualification.
The default execution profile remains SQLite with separate retained Jobs and Runtime files. The
`--provider postgresql` profile compiles a distinct external consumer against the centrally pinned
Npgsql provider, requires the actual PostgreSQL M2 all-groups prerequisite, and runs the identical
54-case oracle, preserving the first 48 cases and their order. It accepts no external database or
connection string. Its owned `postgres:16-alpine` container has exact run labels, a loopback-only
port, actual image ID and PostgreSQL 16 engine evidence. Jobs and Runtime use separate databases
with verified C collation and a generated schema. An admitted aggregate cgroup parent, when supplied,
is checked against the actual container PID. Command files containing this throwaway credential are
private. After all child processes stop, both actual databases are exported as retained `pg_dump`
artifacts and hashed. Cleanup verifies labels before removing only the returned full container ID,
then proves exact-ID absence using a successful daemon query. Export or cleanup failure fails the
receipt. The selected provider assembly bytes and compile profile remain in package evidence.
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

The reviewed twenty-case checkpoint is preserved separately. The extended 54 declared cases cover:

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

- A real initial-attempt checkpoint cut creates the BPMN native helper before RunJob executes its
  body. A qualification observer forwards the actual checkpoint writer and all reader/capability
  checks, waits for its successful immediate `ActivityAttemptClaimed` commit, then blocks before
  returning to activation. A separate inspector proves the exact committed helper/attempt/pin/origin,
  zero saved bookmarks and zero Jobs. The controller kills that exact owned observer before its
  timeout, commits genuine-root cancellation from a new process, then starts a successor coordinator.
  The case requires positive normal native cancellation settlement, the same initial invocation,
  zero handler entry/Jobs admission, root `TerminalSettled`, and successful native-history deletion.
  The qualification marker is never product authority. This case must remain Failed or Not Run
  until the ordinary native cancellation path provides its exact committed pre-wait phase proof;
  missing admission state or a helper that never existed cannot satisfy it.

- Four linked manual retry controls use real native/BPMN parents, both still waiting and already
  terminal. A selected failure stays unchanged; one exact operation creates/replays a different
  canonical JobId with the original model association and no continuation. Instance and definition
  holds both apply to that independent run. Releasing one leaves it unstarted; releasing both runs
  the same useful report once and never reattaches or resurrects the old parent. The real handler's
  external failure-file condition is removed after the original failure; accepted input/pins remain fixed.
- Two continuation cuts observe the actual public provider's successful durable grant before returning
  to result dispatch. Another process verifies the same retained permission and adds both workflow
  holds. After the controller kills the exact blocked sender, a clean successor consumes that same
  already-started permission while both holds remain active, with one saved result and handler attempt.
  A timeout or any exceptional hold exit writes a permanent failure marker, including delivery-token
  cancellation. The controller refuses that marker before/after its exact kill and independently
  rechecks the saved wait, unconsumed input and owed ACK after the kill before starting its successor.
  All other public command/query methods forward to the selected provider.
- Direct native cancellation uses the real initial-claim checkpoint cut before the first saved wait,
  with no model origin. Independent inspection proves the existing invocation/attempt and zero Jobs;
  after the exact process kill, ordinary cancellation must settle that same root and permit deletion.
- Six genuine BPMN controls use actual selected Failed, TimedOut and Cancelled results. A read-only
  public result-validator observer arms an exact provider-read intervention only after live root
  validation starts. Its query decorator returns missing or wrong-result grant evidence without
  changing stored state. A separate inspector must still see the canonical permission, unaccepted
  root input, owed ACK/source and retained helper history. A clean successor must accept the original
  input and ACK the same selected result once. The timeout case pins its explicit policy in the original
  executable; it uses the same cooperative report handler. These controls qualify adverse reads at
  BPMN consumption, not forged native resume payloads before native consumption.
- Four native/BPMN awaited-owner cancellation controls cut at actual handler entry or at a retained
  completed result before continuation. The running handler observes cancellation but deliberately
  keeps running until its external barrier opens. Its actual later successful physical receipt is
  retained while the authoritative result is Cancelled. A previously selected successful result stays
  unchanged when its parent cancels before delivery. Each exact parent stays Cancelled, its result
  responsibility settles through positive terminal proof, and one physical attempt remains visible.
- Two admission response-loss controls pin a two-second ordinary recognition policy in the original
  native executable. A public dispatcher observer retains the actual committed source context; the
  real Jobs provider commits acceptance before the controller kills the producer before sender ACK.
  The independent worker finishes while that sender stays offline. Actual parent consumption and
  Jobs result ACK must then commit while the exact original admission source still remains Delivering
  and owed. After the natural recognition deadline, a fresh exact protected-origin read must equal its
  original receipt before any replay; a different ordinary key and proposal with that same origin
  must independently replay the original JobId, eliminating old-key fallback as the explanation.
  After old-source visibility expiry, a fresh process claims and dispatches the identical original outbox
  intent under a new real fence. The exact protected origin still resolves its original acceptance,
  JobId and result. The already-dispatched old context is refused at its stale fence before and after
  semantic result consumption. No recognition time, accepted policy, outbox row or model state is edited.

Every planned case is retained as Passed, Failed or Not Run. Failure and cleanup failures remain
failures. `fullAcceptanceState` remains Incomplete even if this focused slice passes. The controller
also records five remaining plans. Forged/missing-grant native resumes before consumption and stale
model-process overlap still need composed M3 controls (J36/J39/J40). Authoring publication/export/import
needs its public authored-material evidence for J35; this Runtime-only host does not substitute it.
Cross-tenant authorization belongs to the existing actual API/operations plan (J44/J45), including model
associations and exact consumer authority; it is not automatically another model CLI feature. The
composed Studio experience belongs to the separate mandatory UX plan (J49–J54), whose source already
contains authored native and genuine BPMN journeys. Existing component, prior-source M2 and unrun UX
sources support these obligations without passing any remaining journey.

This trusted qualification host accepts only controller-created command files. It is not a product
CLI or an HTTP service. These source-declared cases have not been compiled or executed; root owns remote qualification
after M2 and independent source review.
