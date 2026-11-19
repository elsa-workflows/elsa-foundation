# PR3410 independent acceptance review — round11

Reviewed head: `f83df1b56ae3f8f41e70f7f61b9c0ad60bad74e1`.
Base/main: `0870a6740dac5e4e78d7b72a15e1dcb0177a4e5a`.
Result: **not clean — one P2 acceptance finding**. No additional reproduced runtime defect is asserted.

## A1 — Complete the actual pending nested ETL cancellation qualification

**Location:** `specs/1017-direct-bpmn-hosting/tasks.md:140` (T041 marked complete), also the resulting all-runtime-evidenced lifecycle assertion at `tasks.md:151`. The affected production boundary is `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnEmbeddingDelivery.cs:63–70`.

T041 declares affected bridge/upgrade qualification complete, but the round7 nested-cancellation cut that reopened #2898's pipeline criterion still has no actual changed-host execution with its ETL cancellation pending. This is a concrete gap in FR013/FR016/FR022 and T040/T041, not a request for broader ETL/provider/Studio acceptance and not a finding that the guard itself is still wrong.

The supported, already captured scenario is outer model A → native RunBpmn worker W1 → inner model B → native RunEtl worker W2 → the original finite HTTP ETL run. The source9508 waiting captures contain both models Running at revision1, one native obligation each, and the actual ETL Running with two accepted records, one completed HTTP partition and a sibling's retained429 wait. The required changed-host continuation is to cancel A through the supported command, let the same actual W2/ETL cancellation settle, preserve all original pins/identities/authority and inspect the real ETL terminal/remaining-work disposition before ancestor settlement and source ACK, including cold recovery.

The current whole46-host nested test does not run that scenario:

- `LegacyNestedCancellationWorkbenchTests.cs:17–22` chooses the **after-cancel** original input and explicitly asserts B was already `TerminalSettled` before the upgraded host starts.
- Lines26–36 decompress the three old stores but pass only the Runtime database to a `runtimeOnly:true` host. Lines99–100 explicitly record `etlStoresOpened=false` and the alternative pending-child order as a separate limitation.
- The current6bd `qualification/host-whole/evidence/nested-cancel/case.json.gz` repeats exactly that scope. Its outer model settles at current source, while the inner model's `updatedAt` and original ETL terminal cancellation predate the correction. The raw original after-cancel ETL database already has Status5 and a real `cancelled` terminal result at `2026-10-31T10:38:18.331801Z`.
- The actual direct/all-four-parent ETL cancellation matrices are valuable earlier-source evidence, including d3b103df0. They predate the changed nested settlement guard. The new old-fault, queued prior-scope cancellation and delayed-compensation recoveries exercise different real inputs; they do not execute this live ETL cancellation. Library/provider controls and aggregate project counts cannot supply the missing actual upgraded-host behavior.

The unchecked #2898 criterion is therefore substantively correct; it is not just stale checkbox prose. The qualification index and Implemented preparation should not imply this cut is closed until its source-bound actual receipt exists.

**Bounded correction:** restore the preserved original waiting coupled stores in an owned current standard Workbench, retain the supported published chain and deployment bindings, submit ordinary outer cancellation and capture actual native/model/ETL state, source/receipt ordering and coupled cold recovery. The initial physical read must still show the original ETL Running; a run already faulted/cancelled before that command does not establish the missing direction. An ordinary supported configuration without compute bindings can prove cancellation of the original pending pipeline with unavailable connectors, if explicitly qualified that way. It must not be described as new current-source HTTP processing/retry or active connector-effect proof. Existing meaningful guard controls may establish the adverse scheduling order separately; no new general matrix or framework is needed.

Original portable input directory:

`specs/1017-direct-bpmn-hosting/evidence/reviews/round-7/pre-correction/waiting/`

| Input | Decoded SHA256 |
|---|---|
| `waiting-elsa.sqlite.gz` | `0dbe02e70cefcf69965add6b8230fec575fa78c2e9545cce7c34b64384a2e19d` |
| `waiting-etl-runtime.sqlite.gz` | `cc0ca8a47fa2f64e8930cb73cae6809b2754af5593368955e6646304d161a2aa` |
| `waiting-etl-design.sqlite.gz` | `18771bf22cbd150d543a08699b7dfa218bb418e688b43d46f11100535516b466` |

`process.json`, `waiting-api.json`, `waiting-wire.json` and both physical model/worker observations bind the exact original chain. `pre-correction/natural-order/capture.py:348–376` supplies the existing normal outer-cancel/physical-observation portion and its preceding launcher/configuration helpers. Adapt that into a private restore runner rather than republishing the chain. `e2e-tests/etl/qualify_http_recovery.py` currently has no resume-input option and should not be advertised unchanged as that runner.

The separate natural-order family's `waiting-*.sqlite.gz` trio is also a valid pre-cancel original input, with its own process.json and pins. Its `after-cancel-*` trio is the deliberately narrower input used by the current Runtime-only regression.

## Verification and positive evidence

I reviewed the complete PR acceptance boundary against current spec1017/plan/tasks/contracts, the full base-to-head inventory, production/test changes and prior-round history. Coverage includes model material/state/receipts, shared dispatch/ownership/drain routing, API authorization/replay, native and embedded workers/results/cancellation, timers/retry/events, artifact closure/retention, old wire/readers, retained migration history, actual legacy and Studio journeys, and the declared profile/provider/rollback limits. Generated snapshots and historical raw results were treated as generated/evidence material, not fresh executions. The latest corrections received fresh source/physical checks; earlier positive results retain their declared sources.

Current root/Studio AGENTS, org protocol blob `f1a8cd800abdee9adc012637143222df912cfe50`, both constitutions and the contributor checklist were consulted. Framework §2.24 and Elsa E2.8-versioning/ModelX/E2.9 retain their draft/provisional status. Owner review-cap, repeated-verification, scoped preview and scan decisions remain settled. No current round11 peer output was read.

Discussion was reconciled using the refreshed cache:359 PR,136 #2898,331 #2892,17 #2907 and30 #2908 comments, zero submitted/inline reviews. The new issue/program checkpoints duplicate the PR checkpoints. The cache's exact head/base and latest round11 launch `6548607681` were checked before completion. `discussion-index.json` hashes every input. No Github mutation was performed.

Fresh independent checks:

- Physical lookup **26/26**, zero skips, covering typed provider/I/O/unrequested cancellation, unchanged requested cancellation, genuine earlier child debt, all retained statuses/delays, physical projection validation and page traversal. `prior-outbox.trx` / `prior-outbox.log`.
- Native-boundary **3/3**, zero skips: the exact original queued delayed-compensation result plus old-fault cancel/start-replay controls. `native-boundary.trx` / `native-boundary.log`.
- Strict current-source Studio verifier: **two families, four completed new runs, nine original observations, twelve whole stores**, retained native material counts84/92 and unchanged frozen authoring. The unchanged corruption controls pass **13/13**. Direct-model Studio acceptance correctly remains false. `studio-verifier.json` / `studio-controls.log`.
- I independently inspected all twelve current Studio screenshots, including the saved original run views, both publish dialogs, all four new completed runs and both retained post-restart views. Original3.0.0 Published14oPzUXgL8A/Test14oQ4rSsWUQ and layout4.0.0 Published14oQbQjgWkZ/Test14oQhd3HgrB agree with the full stored API observations. The layout TestRun keeps its existing linear inspection presentation; authored layout/XML remains preserved.
- Manifest hashes verified: frozen104, original round7 capture52, authentic pre-fix delayed-compensation10, current qualification364 and current Studio66 asset references. **62 whole-store references /48 distinct databases** pass readonly integrity checks. `asset-audit.json` / `audit_assets.py`.
- Physically audited the original b519 queued-result whole database against both current standard-host cold pairs. Old worker, exact result intent/payload, semantic state, authority, full pins, material and26 original checkpoint markers remain unchanged. Model revision2/pending becomes revision3/settled; result gets its ordinary receipt, native queues empty. The obsolete retry remains byte-identical/Pending and future at completion observation, not rewritten. Distinct PIDs49306/49574 and66775/66852 preserve byte-identical whole pairs c2e086… and c7e6da… respectively. Exits137 are process-loss evidence, not graceful exits. `delayed-physical-audit.json` / `audit_delayed.py`.
- Inspected the actual original nested waiting and after-cancel ETL tables and paired model observations. `nested-original-cuts.json` records the Running input versus already-cancelled input distinction underlying A1.

I confirmed the tested RuntimeEF/Core/BPMN DLL hashes equal the actually qualified6bd products (`tested-product-identity.json`). Every one of the369 files changed after6bd is under this feature's docs/evidence; production/test/build inputs are unchanged through frozen f83. Main0870 is an ancestor. Checkout remains clean at f83.

Retained source6bd TRX counters were independently parsed:509 BPMN,1816 RuntimeEF,3090 Runtime,46 hosting, selected actual-host1 and594 architecture all executed/passed with zero skips. API15, frontend2825/browser69, build/type/lint/maps/tiers receipts retain that source; these were inspected, not rerun as whole suites. Lint55 warnings, locked-restore94 warning lines and the earlier shared-EF opt-in NotExecuted remain distinct. Earlier setup failures, anonymous export/auth diagnostic, browser diagnostic/retry, quarantines and cancellations are not converted to passes.

Private review diagnostics: the first targeted .NET filter named a filename rather than its partial class and matched no tests; it is retained separately as `prior-outbox-unmatched-filter.*`, not counted as a pass. The first private physical audit mistakenly equated the model receipt's acceptedAt with the later observation timestamp; the corrected audit checks chronological ordering/future eligibility and does not infer those timestamps are identical. `git diff --check` reports whitespace in retained raw historical DOM/log/report evidence; this is not adopted as a runtime acceptance defect.

No new actual host/browser journey, broad test repeat, other-provider host, current-head CI or resulting-main verification was performed by this review. Current source-qualified Studio and delayed-compensation proof remain valid. A1 does not reopen delivered #2735 or require first-class direct-model Studio, wider ETL/provider/cluster acceptance, Orleans/task-runner/alteration or the separate native-fault-policy decision. T045–47 review/required-CI/protected merge/resulting-main remain separate gates; no merge authorization is implied.
