# PR3410 complete-PR round9 acceptance review

**Verdict: CLEAN for the scoped runtime increment. No actionable acceptance findings.**

Reviewed exact pushed head `ca1b3ea8b28a10409c58da9ff49884f6370864fd` against main `d63e18a47a2a6ca3e67a49855639d16e66f1f958`. Both the independent live API read and final refreshed context match those SHAs and include launch checkpoint `6546095738`. Eight prior full rounds remain not clean; this is the round9 acceptance lens only. No other new round9 reviewer output was read. No shared checkout edit, GitHub mutation, push, subagent or user-host interruption.

## Round8 C1/A1 is resolved

The four-line addition at `BpmnEmbeddingDelivery.cs:65-68` correctly keeps terminal embedding cancellation owed while a compatible legacy fault frame still retains native Work. It leaves the shared parent-result ACK available to introduce the existing cleanup decision, avoiding the competing-input/FIFO issue discussed in earlier rounds. It preserves the semantic Faulted result and adds no public contract, state format or capability.

The stronger actual evidence satisfies the earlier finding without requiring a new broader matrix:

- The actual old input is the original format5 pending-parent fault. Its portable pre-capture backup has identical complete schema and all rows across38 tables. The source producer restores the hash-exact old input before taking normal provider backups.
- Independent immutable reads of the pending, prematurely acknowledged and corrected-deferred whole stores show the **same model bytes, same Running worker bytes and same exact source/intent**. Pre-fix source status is Delivered while Work1 remains. Corrected status is FailedRetryable, deliveredAt null, with Work1/no cancellation inventory retained and no competing model input.
- Actual standard runtime-only Workbench recovery at source20bca consumes the existing parent ACK, retires the same legacy Work, cancels the original worker and reaches TerminalSettled/format14. The original semantic state, Faulted result, authority, scope, tenant, full executable pins, material incarnation and worker-origin address are unchanged.
- The exact original cancel source is acknowledged at `2026-10-31T15:55:26.613024+00:00`, after the worker completion timestamp `15:55:26.008923+00:00`. A second distinct-PID cold host retains a byte-identical whole Runtime database (`00c8c6e1d77c5411b660f1fea70bf32400d36b909d629e7b3874a2593937214b`). The owner-authorization404 and absence of Design storage remain explicit.
- I independently ran the new legacy guard and earlier explicit-cancellation/start-replay regressions: **5 passed, zero failed/skipped**. Their existing Release binaries identify sourcee83fc58f; those source paths are unchanged at reviewed ca1b. This is source-equivalent focused verification, not relabeled current-head CI.
- Retained TRX reconciliation confirms the meaningful pre-fix regression **1 expected failure/1 pass**, corrected whole BPMN506/506, all5 old-fault host cases, whole hosting44/44 and architecture594/594, zero skipped. The earlier missing-import compile failure remains separate and ran no tests.

This is an actual old native-parent cancellation cut and standard-host recovery. It is not a new actual nested A/ETL/Studio/other-provider run; that limit is correctly retained. The earlier round7 ordinary-order observation remains outer-Pending rather than a full cancellation proof.

## Full-PR acceptance assessment

The full diff/inventory and existing native/model material, checkpoint, selection, lifecycle, transport, operational API, embedding, legacy adaptation, retention and migration paths were considered against spec1017's five journeys,22 requirements and six success criteria. The current repair does not widen the declared profile or change the normative acceptance requirements. Released BPMN0.5.0 remains the one interpreter. Actual native/BPMN consumers use the existing mailbox, ownership/fence/queue/checkpoint/outbox authority; no native root is substituted for direct human-model execution. Native composite fault policy and #3242 remain unchanged.

| Acceptance | Evidence disposition |
|---|---|
| SC001 own direct identity, partial fork and equivalent embedding | Actual runtime-only start/wait/restart/resume/complete and the four paired direct/native parent journeys remain source-bound; current44-host qualification includes the direct and all paired partial-fork cases. No native workflow/activity/bookmark carrier is inferred for the direct human model. |
| SC002 human/timer, worker/timer and broadcast orders | The actual human duration,15-case finite-service duration and saved first-delivery recipient-cohort receipts retain their exact versions and declared normalization. Late obsolete input, rearm/later listeners and empty cohorts are distinguished from simultaneous-commit fixtures. Current host cases retain both human-duration orders and signal cancellation. |
| SC003 real recoverable ETL and truthful outcomes | Real finite HTTP/RunEtl success, matched/unmatched business error, technical fault, held retry, pipeline-originated cancellation and native/model interruption receipts remain distinct. The new legacy transport/recovery proof closes the outstanding cancellation-ACK gap. Physical effects retain their at-least-once limit; no broad ETL completion or other-provider host proof is inferred. |
| SC004 supported operator commands and identities | Existing actual authenticated human completion, cancellation, incident inspection/retry, replay/conflict/obsolete-result and authorization-before-disclosure receipts remain valid within the minimal API. Full task administration/operator-console UX remains outside this leaf. |
| SC005 actual old-store upgrade, authoring and Studio | All104 frozen original assets rehash, all22 original stores pass integrity checks. Actual old-store/native-parent/migration/template cases are included in current44-host qualification. Existing saved Studio remains sourceb7 and is independently reverified below. |
| SC006 evidence mapping and scope | The current source-bound index and round8 correction ledger map the acceptance without promoting historical library/synthetic/provider fixtures into actual-host proof. T041/T043 are evidenced; T044 prepares Implemented for merge. T045-T047 remain open for author-loop integration, required checks, protected merge and resulting-main verification. |

I rechecked the entire discussion corpus inventory:324 PR comments,126 issue2898 comments,321 program2892 comments,17 issue2907 comments and30 issue2908 comments,438 distinct bodies. The prior eight round reports/corrections and current owner directions are historical inputs; the latest launch is included. Submitted reviews and inline comments are empty. Related issue bodies retain their unchecked broader acceptance and settled deferrals. The complete discussion index is in `context/complete-discussion-index.json`.

## Independent verification and provenance

- Rehashed **104 baseline +15 actual pre-fix transport +6 corrected transport +98 correction qualification asset references**, including declared decoded hashes: zero mismatches. Checked integrity of all46 archived SQLite references, correctly unwrapping lossless double-gzip packaging where declared. Counts are asset/store references, not a claim of46 distinct databases.
- Independently compared the physical original/pending/bad-ACK/deferred/completed/retained cancellation stores and immutable pins. Raw results: `actual-source-physical-audit.json`, `immutable-pin-audit.json`, `original-input-binding.json`.
- Independently reran the unchanged strict saved-Studio verifier: **two families, four completed new runs, twelve whole coupled stores, nine original-run observations,84/92 original materials/incarnations, frozen authoring and exact exports/layout/full pins preserved**. `directModelStudioAcceptanceProved=false` remains false. All six Runtime cuts contain zero model executions and inputs, confirming this internal model-embedding repair does not participate in those native compatibility journeys. The full Studio tree is unchanged. My prior round's inspection of the same twelve sourceb7 screenshots and13 passing final controls remains source-bound; I do not claim a new live browser run here.
- Verified directly with Git that Essentials, Extensions, Studio and tools/bpmn are unchanged from qualified20bca to reviewed ca1b. Main d63e's research/templates are adopted without relabeling execution evidence. The binary/API/provider/frontend/browser receipts retain their original sources; an internal method-body correction does not invent a fresh15-comparison API run.
- Current host TRX44 includes the new parentCancellation case and the original direct/timer/nested/template cases. I did not repeat the21-minute whole host run. The independent five focused guard cases are recorded in `embedding-guard-regressions.trx` and its log, with actual binary hashes/source strings in `focused-test-product-provenance.json`.

One initial audit assertion compared a SQLite provider backup byte-for-byte with its source and failed. That was an overly strong file-equivalence assumption, not a product failure. The complete schema and all rows match across38 tables; the actual second-cold-host whole-file equality separately passes. This diagnostic is retained in `original-input-binding.json` and is not counted as a passing initial check.

## Remaining limits

This clean acceptance lens is not a merge or broader-program approval. Draft, green required checks on the exact reviewed head, the integrated clean author loop, normal protection and resulting-main verification remain delivery gates. No current-head green CI or merge is claimed here. Owner-approved16-round bound, scoped computed4.0 previews and incident-specific public-fingerprint resolution remain settled.

Existing2735 is delivered. First-class direct-model Studio, broader2907 operations, wider2908 ETL, overall2892/agent/independent-model acceptance, non-SQLite upgraded-host/clustered proof and Orleans/task-runner/alteration deferrals retain their existing status. Earlier failed auth attempts, unconfirmed loop cause, failed/cancelled browser/configuration diagnostics, optional/NotExecuted/quarantined evidence and prior CI failures are not relabeled green.

Private evidence/report directory: `/tmp/elsa-2898-review9-acceptance`.
