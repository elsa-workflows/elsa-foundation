# PR #3410 — full-PR round 6, independent correctness review

Verdict: **one actionable P2 finding; this lens is not clean.** No repository edits, builds, commits, GitHub writes, host/product replacements, user-tab changes or sub-agents were used.

Reviewed head: `d1449c0bff679212f8a64766a95b1c29cfadbcea`  
Base: `d481d6a5e624e35bba9fbf32033ed02aff64cbef`  
Round: **6 of the existing six-round bound**, with five earlier completed rounds and no earlier clean round. This report neither starts another loop nor grants merge clearance.

## C1 — P2: Refuse an unrepresentable timer deadline before durable admission

Location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDurationTimer.cs:38-40` (with the eventual failure at `BpmnDirectExecutionDriver.cs:595-596`).

The publication/admission validation accepts any positive representable `TimeSpan`, but a positive `TimeSpan` can exceed the remaining `DateTimeOffset` range. A definition-format-4 human task with its interrupting duration boundary set to **`P3000000D`** is accepted as runnable `foundation-native-work-v1` material. Starting that material returns **HTTP 202 / Pending** after physically enqueuing the first model input. Only afterward does `Apply` add the duration to the checkpoint time and throw `BpmnModelValidationException: The duration timer deadline is not representable` with `ArgumentOutOfRangeException` as its cause.

The failed first decision creates neither a model header nor an input receipt. The accepted FIFO input remains durable and cannot ever execute at any later current date. Normal command inspection continues to report Pending. Execution inspection and the ordinary cancellation endpoint both return 404 because they require the missing model header. Thus this is an accepted, permanently unprocessable input, not a deliberate pre-admission refusal.

The normal recovery pump also reclaimed the exact original queue item after natural claim expiry: physical revision/claim token advanced **2/1 → 3/2**, while the same input remained queued and no header or receipt appeared. This matches the deterministic overflow in the unchanged driver. The first execution's error is captured in `first-host.log`; the later resumption attempt is evidenced by the physical claim transition, not by a second logged exception claim.

A **`PT1H`** definition with otherwise identical graph/bindings is the positive control: publication HTTP 200, start HTTP 201/Accepted, model revision 1/Running and a durable accepted receipt, preserved across both restarts.

Required correction: reject a duration whose derived deadline cannot be represented before treating its material/input as runnable, and ensure any already accepted input at this boundary has a supported durable disposition rather than an irrecoverable FIFO exception. Add a regression using a positive TimeSpan that overflows DateTimeOffset; existing zero/negative/TimeSpan-overflow controls do not exercise this gap.

This is directly relevant to the declared format-4 positive-duration profile and the hosting contract's requirements that malformed/unsupported new work refuse before acceptance, that a 202 represents durable admission, and that accepted work remain recoverable. The bug does not require a stored-state edit, injected clock or contract probe.

## Actual normal-host reproduction and preserved inputs

The probe used the existing **source `9c7681c56c529c8a1a4ab4d9fc91934ec07d0448` Release production Workbench** and real authenticated HTTP APIs. `git diff 9c7681c56 d1449c0bf -- Essentials/src Essentials/apps/Elsa.Workbench` is empty. The source9c Workbench hash is `a300eb660707f98f6c71d7d916b23b3aa8cd7859a2a54c668af342423c05511a`, matching the committed frontend-adoption receipt. All five recorded product hashes are unchanged after this review. This is a source-equivalent normal product journey, not an exact-d144 rebuild or a new whole-suite pass.

Exact retained failure identity:

- Model artifact: `sha256:aa0f9a8071e785dfb9aa74e0ed37c72c6874499c51aaf9af6a7a7375df5b65ea`.
- Material incarnation: `cc8292cbbb684ceea27f2aa77080ae89`.
- Source: `bpmn-source:752853514095fcf46238cd9327e1fa2126556bee2c7d305f01961fe204e009b2`.
- Execution: `bpmn:33d5f0dd245993d696e85c4e71855bc192d9bad472d7844133a5496260edba37`.
- Input: `r6-start-beyond-date-range`.
- Original queue item: `model-input:de0c5b7c30df6de909101549bcc41dc4e9704777ccfda7dc89e49ffe7166f54c`.

Evidence in this directory:

- `probe_timer_admission.py`: complete portable normal-host setup, exact submitted definitions and HTTP sequence. Copy this script to a **new empty directory** and run `python3 probe_timer_admission.py` with the pinned unchanged Release products available. It refuses to overwrite an existing `timer-host` directory.
- `timer-admission-result.json`: exact request bodies, responses, original IDs, two cold-process identities, source boundary and product hashes.
- `before-restart-runtime.sqlite.gz` and `final-runtime.sqlite.gz`: coherent original closed Runtime stores before/after first restart; integrity `ok`. These are actual accepted source9c inputs captured before any correction.
- `resume_timer_probe.py`, `timer-after-expiry-result.json`, `after-expiry-runtime.sqlite.gz`: a third normal host, natural claim expiry, recovered same queue item, repeated Pending/404 observations and closed-store capture.
- `durable-checkpoint-summary.json`, `before-restart-raw-model-and-queue.json`, `final-raw-model-and-queue.json`, `after-expiry-raw-model-and-queue.json`: immutable read-only observations. The only header and receipt belong to the PT1H control; the failing execution has neither.
- `probe-verification.json`: source equivalence, unchanged products and exited owned-process verification.
- `first-host.log`, `restarted-host.log`, `post-expiry-host.log`: raw normal-host logs.

Three successful-journey children (14821, 14869, 15385) exited normally. No preexisting host or browser tab was operated. No SQL write was used to manufacture or repair any execution, queue, receipt, claim, authority or clock. Two preliminary controller attempts failed in their stopped-store observer opening; their original `timer-host-attempt1`/`timer-host-attempt2` stores/logs remain diagnostic and are **not counted as qualification**. The final observer uses immutable read-only SQLite after the owned process exits. See `diagnostic-history.json`.

## Full-PR scope and discussion boundary

Read current local AGENTS and the supplied current org protocol, the pinned review boundary/start record, issue/PR bodies and the full discussion corpus/chronology, prior five completed-round findings/corrections, spec1017 spec/plan/hosting contracts/qualification index, current production paths and changed test coverage. The correctness trace includes:

- Native/model material identity, canonical hashes, provider capability refusal, full dependency closure, root-write retention, source publication and GC roots, including accepted inputs before their first header.
- Atomic model state/input/claim/outbox/fence/marker decisions, rejected-input unchanged-state behavior, admission CAS conditions, terminal lifecycle and immutable outcomes, scoped read/error boundaries and schema-family write/read gates.
- Registry/selection, built-in mailbox/lease reuse, queue pre-claim validation, default native routing, terminal mailbox eviction and recovery.
- Released interpreter use, supported profiles and graph/binding refusal, human/worker/timer inputs, exact attempts/replay, held retry/budget, result/cancellation settlement and legacy fault cleanup.
- Real native worker and RunBpmn admission/result/parent bridges, downstream ETL cancellation proof, first-delivery Event cohort reuse and fail-closed physical source/authority checks.
- Additive Runtime migrations, retained Design history/cumulative baseline bridge and the related provider/upgrade/legacy/Studio compatibility tests and receipts.

The pinned diff contains **7,549 changed paths**; most are retained evidence or generated assets. The source review inventory includes **104 production C#/project files excluding generated migrations** and **59 Essentials test C#/project files**, with tooling/provider/generated-schema and documentation changes considered in their affected boundaries. `production.diff`, `tests.diff`, their file manifests, `changed-source-files.txt` and `review-scope.json` retain the complete source/test review boundary.

The final supplied discussion refresh is frozen under `frozen-inputs/` and reconciled to **668 unique issue comments**: #2892 **289**, #2898 **97**, #2907 **13**, #2908 **25**, PR #3410 **244**. There are **0 PR reviews, 0 inline comments, 0 review threads**. The start comment remains `https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6536773095`. The additional post-start items are the same round6 checkpoints and CLA bot requests; no new owner hold/claim change was introduced. Repeated raw CLA/evidence text was treated as repeated discussion rather than independent behavioral proof. No other current round6 reviewer's output was inspected.

## Test status and holds

- **New normal-product counterexample:** reproduced through authenticated publication/start/inspection/cancellation and natural claim-expiry recovery; control passed. No scratch compilation or shared build output mutation occurred.
- **Whole suites:** not rerun; no new concern justified repeating the already-source-qualified whole suites. The committed source04c receipt remains **6,928 passed / zero skipped**, and source9c frontend adoption remains **116 affected frontend tests / zero skipped**, each at its declared source. This review does not relabel them as newly executed or exact-d144 CI.
- **Existing old-fault cuts, full saved Studio restart proof, API comparisons and 104 frozen baseline assets:** considered at their documented source boundaries. Historical failures/diagnostics remain failures/diagnostics. C1 supplies an additional actual accepted input that those existing positive journeys do not cover.
- **Known owner API/version-magnitude/needs:decision, deferred GitGuardian 39189012, required exact-head checks, protected merge and resulting-main verification:** unchanged; not new correctness findings and not cleared.
- First-class direct-model Studio, wider/upgraded-host ETL, non-SQLite actual-host acceptance, #3242 and Orleans/task-runner deferrals remain separate. #2735 is delivered and was not restarted.

Final source verification: HEAD remains the pinned d144 commit and `git status --short` is empty. **One actionable finding remains for root adjudication; no clean round/merge clearance is asserted.**
