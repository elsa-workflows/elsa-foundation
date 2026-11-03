# PR 3410 full-PR round 9 correctness review

Verdict: **not clean — one P2 finding**.

Frozen reviewed head `ca1b3ea8b28a10409c58da9ff49884f6370864fd`, base `d63e18a47a2a6ca3e67a49855639d16e66f1f958`. Final live read-only GitHub read confirms both, draft/open. Shared HEAD and origin/main match those SHAs and the checkout is clean. No shared source edits, GitHub writes, pushes, or subagents. No other round9 review output was read.

## C1 / P2 — Include cancellation debt from earlier native checkpoints in terminal settlement

Primary location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnNativeWorkBoundary.cs:299-301`.

`CaptureAsync` builds a Cancelled native worker's `CancellationSources` only from `commit.PostCommitIntents` in its terminal checkpoint. A supported native scope cancellation can already have committed a descendant cancellation source while leaving the enclosing native workflow Running. A later whole-workflow Cancel excludes those already-Cancelled activities, so the terminal checkpoint contributes no new descendant intent and this inventory becomes empty. `RequireCancellationSettlementAsync` then checks no sources, accepts the native result and lets its owning BPMN model report TerminalSettled while the earlier descendant cancellation remains owed.

Concrete reproduced order:

1. A direct format2 BPMN service starts a published native RunBpmn worker with an activity boundary. Its embedded model B starts its own native-work binding.
2. An ordinary native `CancelActivityScope` cancels that boundary without changing the worker workflow from Running. The real checkpoint/outbox creates `Elsa.Bpmn.CancelEmbedded.v1` in this earlier checkpoint.
3. Real outbox delivery and model mailbox draining cancel B semantically. B is now revision2 / TerminalPendingSettlement with one `Cancellations` entry (`Settled=false`). Retrying the earlier embedded-cancel source is correctly deferred by the round7/8 settlement guards; the source remains FailedRetryable and has no deliveredAt.
4. An ordinary whole native-worker Cancel now terminalizes the enclosing workflow. The earlier child activity is already Cancelled, so it is absent from that checkpoint's cancellation candidates. The emitted authentic native result contains `cancellationSources: []`.
5. Real result delivery and the ordinary outer model mailbox accept that result. Outer A becomes revision2 / **TerminalSettled / Cancelled**, while B stays **TerminalPendingSettlement**, its cancellation entry is unsettled and the exact earlier cancellation source is still **FailedRetryable / deliveredAt=null**.

This is a cancellation-settlement inventory gap, not failure of the newly fixed embedded-cancel guard. That guard demonstrably keeps the earlier source owed; the later result simply omits the source entirely. Eventual background cleanup does not justify the earlier settled claim. Carry prior durable descendant cancellation responsibility into the terminal settlement boundary (or independently validate it there), retaining exact source identities and proof of delivery. The current settlement validator also assumes every source belongs to the terminal checkpoint, so a repair needs to reconcile that assumption rather than merely append an older ID.

## Reproduction, captured input and exact limits

The probe is `probe/Program.cs`, executed with current production Release assemblies from `tools/bpmn/NativeWorkDispatch/bin/Release/net10.0`. Every exercised BPMN/Runtime/Core/RuntimeEF DLL reports source **20bca048737cdbcceb95cc4541643077d3a307a5**; their hashes are in `prior-cancellation/products.json`. `git diff --exit-code 20bca0487 ca1b3ea8 -- Essentials/src Essentials/tests/Activities/Bpmn tools/bpmn Studio/src` passes: no delta in these source paths.

Fixture provenance is explicit:

- The starting whole Runtime/ETL stores are unmodified decompressions of the already committed actual source9508 `reviews/round-7/pre-correction/waiting` captures. All original compressed/decoded hashes and process receipt are in `original-input-manifest.json`.
- Through public material/source ports, the probe publishes a **new** native executable that reuses the captured RunBpmn node, descriptor, frozen model metadata/pin, CLR contract, resume targets and dependency inventory. Its additional `activity.definitionVersionId` metadata opens a native activity boundary, following the existing native fixture pattern. The produced activity's `ExecutionScopeId` equals its actual InvocationId; the typed CancelActivityScope command validates that exact boundary. This is a derived SDK fixture, not another normal Studio/compiler publication acceptance claim.
- The normal `IBpmnModelRuntime.PublishAsync` and StartAsync accept the new outer format2 service and create its state. The native scheduler, checkpoint committers, actual EF stores, model queues, outbox processor and real protocol drivers produce every execution, cancellation source, input receipt and terminal result. No execution state, claim, receipt, clock or queue row was manually seeded/edited. No fake store, dispatcher, driver, model payload or delivery claim is used in this probe.
- Native child work of the new embedded model has **not** been admitted. Its exact start/retirement/cancellation responsibility remains owed. This probe does not claim a new Running ETL child, HTTP effect, live Workbench restart or actual-host upgrade acceptance. Existing unrelated captured waits remain in the private store.
- Eight consistent complete SQLite backups retain before, native-scope cancel, queued embedded cancel, consumed embedded cancel, retry refusal, terminal-worker result, queued outer result, and premature outer settlement. All pass `integrity_check=ok`; compressed and decoded hashes are in `prior-cancellation/store-manifest.json`.
- In particular, **`deliver-worker-result.sqlite.gz` preserves the already accepted real queued native result before outer consumption**. This is a portable pre-fix input for a future correction and recovery proof. `drain-outer-result.sqlite.gz` retains the observed counterexample after consumption. No writer correction is made here.

Observed identities from the final capture:

- Outer model `bpmn:723068f9ada40fbde52746a0678a2f5c65e61ef6831c683f85b0098a3daaa044`: revision2 / lifecycle3 (TerminalSettled), Cancelled.
- Native worker `wfexec:start:v1:d02cb25356a4825ec03647642c84fc5028d20300f53f65d3b8ea86a6a5044794`: Cancelled.
- Embedded model `bpmn-embedded:57443f3f73b36e4b4cb155ba015275d92664c3a3637cfa4d77f7be40c2f1bcfc`: revision2 / lifecycle2 (TerminalPendingSettlement), cancellation Settled=false.
- Earlier source `commit:round9:0ce3b59c12b34687b8e8c9af19979c01:activity-scope-cancelled:14oD2dLayY2:bpmn-embedded-cancel:e8559ceb6650ed55da14d57a1e5f6fae28b2a9393e60180403bb40be41374a16`: status3 (FailedRetryable), deliveredAt null.
- Authentic physical native result payload: cancellationSources is empty. See `prior-cancellation/finding-summary.json` and `whole-worker-cancel.json`.

The initial private launcher could not locate its copied SQLite native library and failed before any scenario step. The private dependency location was corrected; successful production-port runs and their whole-store captures are separate. This setup error is not a product failure. Earlier completed uninstrumented runs remain diagnostic; the final snapshot-enabled run adds a real 1.2-second wait solely to make the existing outbox retry due, without editing its clock or state. Its retry attempted one delivery and deferred it.

## Full-PR review coverage and current checks

The review rechecked the full candidate diff and relevant execution paths beyond the round8 correction: API authorization/replay and commands; direct graph/profile validation and human/service/timer/signal/retry decisions; native starts, cancellation, typed result consumption, embedding and legacy cleanup; model routing, scheduler claims, ownership and recovery; checkpoint/CAS/receipt/outbox/fingerprint boundaries; material identity, dependency closure, root leases, retention/GC; schema stamps/upcasters and additive Runtime/retained Design migration histories; compatibility constructors; host composition, project/tier/generator wiring; narrow Studio CSS/test changes; specs and source-bound qualification limits. Prior full-round findings and current discussion were reconciled. The historical actual-host evidence corpus was not rerun as a new matrix.

Fresh context exports contain **324 PR comments,126 closing-issue comments,0 submitted reviews,0 inline comments**, and include round9 launch6546095738. Identical bodies/CLA notices were deduplicated; previous discussion remains part of review context. The audit hashes all exported PR/issue/program files in `context-audit.json`. Current org/repo AGENTS and applicable contributor/constitutional rules were read/reconciled. Settled preview/GitGuardian exceptions, wider program acceptance and declared deferrals remain separate.

Independent focused checks:

- **119 BPMN cases passed,0 failed/skipped**, Release `focused-bpmn.log` / `results/focused-bpmn.trx`. DLLs/test assembly identify **e83fc58f6fa84a7cbeb72678729aa1b78c6b1ca9**; the BPMN source/test paths are identical to the candidate.
- **50 EF model/checkpoint/admission/queue-format/migration cases passed,0 failed/skipped**, Release `focused-ef.log` / `results/focused-ef.trx`. Those prebuilt DLLs/test assembly identify **04c2b53adf7b8eeda6cdf2c879f697b867fc5ad2**. RuntimeEF production/test source is unchanged from that source. Later Core/Runtime changes are confined to unrelated trigger-index/activation-coordinator paths; these are focused persistence checks, not fresh whole-runtime or exact-head binary qualification.
- The production-port counterexample itself runs **20bca** products as detailed above, not stale Debug dependencies. The private launcher is built in its own Debug output; its referenced production DLLs come explicitly from Release.
- Changed production/Studio source `git diff --check` passes. Build-slot wrapper honored. No timeout or gate was relaxed. Exact-head CI, clean review, protected merge and resulting-main qualification remain distinct delivery gates.

No additional actionable correctness finding confirmed. Root should independently verify/adopt this C1 before changing production writers and retain the real queued-result snapshot for correction qualification.

## Root adjudication clarification

`CancelActivityScope` is an existing supported **native actor command**, implemented by `WorkflowCancelActivityScopeSchedulerWorkHandler`. It is not direct-model token alteration. The probe sends it through `IWorkflowExecutionActorProvider.GetAgentAsync` and the actor's ordinary `EnqueueAsync`; the queued command is claimed and the normal planner/checkpoint path writes the cancelled state and its outbox. The selected produced activity has `InvocationId == ExecutionScopeId` and satisfies the real typed command constructor and handler checks. Its workflow remains Running after that command, exactly as this native operation specifies. The subsequent whole-worker Cancel uses the same ordinary actor route. No command was written by SQL or by directly saving a manufactured execution.

The minimal ownership-preserving repair is to make the existing native terminal-settlement inventory retain the worker's **already committed** descendant cancellation responsibilities as well as intents contributed by this terminal checkpoint, and verify those exact durable sources before accepting its Cancelled result. Keep their original IDs, scopes, invocation bindings and delivered proofs; do not mark them delivered or replace them with assumed equivalent new intents. The same-checkpoint restriction in `RequireCancellationSettlementAsync` must be updated consistently. A due-now outbox query is insufficient because prior debt can be delayed, FailedRetryable, or currently claimed. If exact prior IDs cannot be obtained from existing retained state, a narrow qualified read capability is preferable to dropping that responsibility or recreating a second cancellation authority. This is a fix direction for root review, not an implemented correction or a demand for a new general abstraction.

The retained `deliver-worker-result.sqlite.gz` is the concrete test input: consume its already queued authentic result before allowing B's original cancellation to settle. Current code accepts and settles A; corrected code should keep A/input/source responsibility pending, then normal delivery should settle B and allow the original result to converge. Keep the already-settled/native ordinary-order controls and actual-host scope separate.
