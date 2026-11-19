# PR 3410 full-PR round 8 correctness review

Verdict: **not clean — one P2 finding**. No shared checkout edits, GitHub mutations, pushes, or subagents.

Reviewed committed/pushed head `e72641e6714768987f555b52ebd36dbc129959a5` against `3e93e06d9bd22763c81767ca296c8924142d030b`. Current cached PR head agrees; launch checkpoint `6545066284` is included. The context audit hashes the complete exported discussion, including 315 PR issue-comments, 123 closing-issue comments, zero submitted reviews and zero inline review comments, plus related program bodies/discussion. Identical exported bodies were deduplicated for reading. Existing owner exceptions, original-ordinary-order limitations, and wider acceptance deferrals remain separate.

## C1 / P2: Preserve cancellation responsibility for old terminal frames that still retain native Work

Location: `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnEmbeddingDelivery.cs:63-66` (supporting guard at `BpmnEmbeddingBoundary.cs:200-203`).

The terminal cancellation shortcut now checks `Cancellations.Any(!Settled)`, but compatible legacy format 5 fault frames can still retain an active native item in `Work` and have **no Cancellations member yet**. Therefore this guard returns successfully for the exact old pending-parent frame already committed in this PR: model revision 2, Faulted / TerminalPendingSettlement, one original native Work entry, no cancellation inventory. The complete original database proves that exact original worker is still Running. Successful return acknowledges CancelEmbedded without queueing any cancellation/cleanup decision (`queueCalls=0`).

Concrete failure order: a previously accepted model A -> native RunBpmn worker -> old faulted model B still has B's other native worker outstanding. On upgrade, cancellation of A can deliver B's CancelEmbedded before B's native-parent-settled input has converted legacy Work into cancellation debt. This shortcut acknowledges B's cancellation source; the A worker's CancellationSources can consequently all appear Delivered, allowing A to consume its Cancelled result / become TerminalSettled while B's original worker is still Running. B may later recover via the separate parent ACK, but that eventual cleanup does not justify the earlier cancellation acknowledgment. This is the same responsibility invariant as round 7, with a distinct compatible old frame that its three explicit-cancellation cases do not cover.

Keep the cancellation source owed until retained legacy native work has entered and completed the existing physical cleanup path. The result/parent-ACK path is deliberately also the old-fault cleanup trigger, so do not blindly add a Work guard to every parent-result path and create an ACK/cleanup deadlock. A cancellation-only route can enqueue/drive the already-supported cancel-embedded cleanup decision, or leave the source owed while the ordinary parent ACK does so.

## Reproduction and limits

The original accepted input existed before this review and remains byte-exact:

- `specs/1017-direct-bpmn-hosting/evidence/reviews/round-5/acceptance/old-embedded/old-pending-parent.snapshot.sqlite.gz`
- decoded SHA256 `0a5ea64058dcdb950fdda9e5b792264ceec2f6ca19fa9398a7733f6c62ea097e`; private read-only decode `old-pending-parent.snapshot.sqlite`; SQLite `integrity_check=ok`.
- Checked archived frame: `reviews/round-5/qualification/fault-recovery/old-pending-parent-fault/before.json.gz`; private unmodified extracted model `legacy-pending-fault-model.json`.
- `legacy-physical-truth.json` checks the exact outstanding native worker against the original database and observes Running.

`probe/Program.cs` passes that unchanged archived model through the compiled production `BpmnDirectExecutionDriver.Read`, `BpmnEmbeddingBoundary.RequireModelAsync`, and `BpmnEmbeddingDelivery.HandleAsync`. The delivery's source/current claim and provider ports are deliberately synthetic, following the existing round-7 boundary test pattern; the stored model frame is actual. Result `legacy-cancel-release-current.json`: `nativeWork=1`, `cancellations=null`, `queueCalls=0`, `Acknowledged`.

The causal control copies the exact candidate delivery class under a private name and adds only an early refusal for the retained original native Work at that terminal cancellation return. `legacy-cancel-release-control.json` refuses with RuntimeSchedulerWorkUnavailableException, preserving the source. This is a causal boundary control, **not a complete recovery fix or actual upgraded-host/nested-process acceptance**. No new actual-host ordering run is claimed. Root should independently verify/adopt the finding before writer changes and retain the original input when qualifying any correction.

Compiled BPMN product informational source: `1cee5acd4e7d7adb01dd467342641a108e349d60`; DLL SHA256 `3c7512b9f2d8b4d7b2368e41a1716ba5facd7d60959512547e4e04f5ff728d0c`. A direct `git diff --exit-code 1cee5acd4... e72641e67 -- Essentials/src/Activities/Bpmn Essentials/tests/Activities/Bpmn/Tests Essentials/src/Workflows/Runtime Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests` passes with no delta. Thus these exercised production/test paths are candidate-source-equivalent, not newly rebuilt at the documentation candidate head.

## Review coverage and verification

The full candidate change set was reviewed, beyond round-7 edits: direct model graph/admission/input/replay/lifecycle logic; human, timer, signal/native work and held retry; model/native start/cancel/result and RunBpmn embedding; shared routing/ownership/claim behavior; checkpoint fingerprints and physical transactions/CAS/retained receipts/outbox; material hashing/incarnation/dependency/retention/GC; native legacy compatibility; EF schema stamps/upcasters and four-provider additive migrations; retained Design migration lineage/history decoration and constructor compatibility; API authorization/commands; host composition, project/tier/generator wiring; the narrow Studio CSS/test change. Current source-bound qualification and prior full-round findings were reconciled. The full historical evidence corpus was not rerun as an actual-host matrix.

Current Release focused tests:

- BPMN direct/runtime/profile/authorization: **117 passed, 0 failed, 0 skipped**, `release-focused-bpmn.log` and `results/release-focused-bpmn.trx`.
- EF model checkpoint/admission/queue-format/migrations: **50 passed, 0 failed, 0 skipped**, `release-focused-ef.log` and `results/release-focused-ef.trx`.
- `git diff --check` over changed production/Studio source passes; shared checkout remains clean.

An initial --no-build run accidentally selected older Debug products (BPMN source eef92); its 92 BPMN + 48 EF passes are retained as historical diagnostics only and excluded from current verification. The subsequent Release checks and probe include current-source equivalence/provenance above. Machine load was high (~61 load average); the focused runs passed without timeout relaxation. The build-slot wrapper was honored for private builds/tests.

No extra finding is inferred from draft status, pending CI, settled preview-version policy, resolved GitGuardian fingerprint, first-class direct-model Studio absence, wider ETL/provider acceptance, or declared deferred work. Those are distinct scope/delivery gates.

Final read-only remote check confirms PR head e72641e6714768987f555b52ebd36dbc129959a5, GitHub-reported base 3e93e06d9bd22763c81767ca296c8924142d030b, draft/open (`final-remote-head.json`). During the review another session advanced the shared local `origin/main` ref to 0c57a8805ac3b61cccad295a9de1244f6d21fa04. This report remains scoped to the explicitly frozen 3e93 base; it does not review that later-main delta. Shared HEAD remains e726 and the working tree is clean.
