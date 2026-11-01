# 2908 Feature: Distributed ETL batch and continuous ingestion
## Objective
Deliver dataflow semantics using proven coordination and work-execution contracts.

## Scope and constraints
Production delivery is owner-approved in [the 18 October continuation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249319967). All four connector families, both PostgreSQL and SQL Server, finite/continuous processing and integrated Studio remain in scope. Implementation is progressively specified through the shared ordered program queue; the [current source-readiness audit](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6279805321) distinguishes existing mechanisms, historical witnesses and missing production evidence.

## Acceptance criteria

- [ ] Finite and continuous runs process two or more independently leased partitions with resumable cursors and model-owned completion/cancellation. Coordinator execution does not process one workflow per record.
- [ ] CSV and JSON Lines file ingestion/delivery have explicit positions, replay, parsing/validation and destination-idempotency behavior.
- [x] Finite paginated JSON HTTP ingestion and JSON delivery preserve continuation positions, retry/rate-limit behavior and effect-before-checkpoint replay (#3151). Continuous-run behavior remains under the first criterion.
- [ ] Both PostgreSQL and SQL Server support snapshot/watermark reads and transactional keyed writes, with real provider-specific recovery evidence. Log CDC is deferred.
- [ ] RabbitMQ JSON ingestion/delivery proves manual acknowledgements and publisher confirms around failure/replay boundaries.
- [ ] Local transform chains include mapping, filtering, validation, routing and lookups; record and byte bounds plus durable admission budgets apply before dispatch. Lease expiry does not establish a physical-memory bound. Initial distributed shuffle, windows/stream joins and general analytics are deferred.
- [ ] Prove checkpoint/cursor recovery and stale-writer rejection, no loss of accepted owed work, duplicate/conflicting results and connector-specific idempotency after effect-before-ack/checkpoint crashes. Physical effects remain at least once unless a stronger connector guarantee is demonstrated.
- [ ] Native workflow and BPMN invocation map success, business error, fault and cancellation; integrated Studio authoring/inspection exposes runs, partitions and relevant recovery state.

These are the approved feature-level obligations from the owner continuation and linked readiness audit. Finite file and HTTP child evidence is verified below; unchecked criteria retain the remaining production acceptance. Connector order, public APIs and leaf specifications are progressively elaborated before readiness; no implementation task is activated here.

## Validation
Reproducible source-bound evidence, negative/failure cases where behavior is claimed, and independent review. Record limitations explicitly. See the parent program for the full product intent and evidence policy.

## Readiness

Coordinating scope stays open/Partial. Existing #2898/PR3410 is the single implementation lane. All five round3 findings have corrections/evidence, including four actual embedded ETL-origin cancellation journeys. Final affected qualification and exact-head round4 remain due, with owner/scan/CI/protected delivery holds. Wider ETL/direct-model Studio retain separate acceptance. [Current checkpoint](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6532508249).

## Phase
M4

## Program
https://github.com/elsa-workflows/elsa-foundation/issues/2892

Affected repository: `elsa-workflows/elsa-foundation`; upstream BPMN work retains its existing owner.


<!-- program-links -->
## Parent
https://github.com/elsa-workflows/elsa-foundation/issues/2906

## Dependencies
- https://github.com/elsa-workflows/elsa-foundation/issues/2904


## Active first slice

- [x] https://github.com/elsa-workflows/elsa-foundation/issues/3109 — finite CSV-to-JSONL ETL/native/Studio journey.


PR #3111 merged as `cfff0f018bda972f1d58c022adfc4e90d6027e4c`; resulting-main verification passed on `da9a4927d98fb29eda017b4e1a74a28ee4316439` ([receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3111#issuecomment-6307901682)). The finite CSV-to-JSONL/native/Studio child is verified and its implementation claim released. Unchecked feature-level acceptance above remains open; #3151 is now verified, and continuous, SQL, RabbitMQ and BPMN obligations remain.

<!-- etl-2908-da9-closeout -->


## Verified HTTP slice

- [x] #3151 — finite paginated JSON HTTP ingestion/delivery, mixed file/HTTP connectors, native continuation and Studio. Implementation #3156 and exact-merge verification at `9462cb743` passed ([receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3156#issuecomment-6324159475)). Documentation closeout #3182 records the source snapshot; broader acceptance remains open.

## Persistence qualification boundary

Current ETL design/runtime persistence evidence qualifies SQLite. Qualification of both stores on all four first-party EF engines remains open and is distinct from PostgreSQL/SQL Server source and destination connectors. [Tracked obligation](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6323246976).


## id=6279805321 elsa-workflows-codex[bot] 2026-10-19T23:42:34Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6279805321
ETL readiness audit — production scope preserved, implementation not activated

Audited source: Foundation `b656e3a8ed8bc59d02291da4af3912fd16b7139b`. Independent read-only audit plus root source/issue review; no code, builds, claims or new leaf. #3056 remains the sole active implementation leaf.

The [owner-approved continuation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249319967) already approves finite batches and continuous integration; CSV/JSON Lines; paginated JSON HTTP ingestion/JSON delivery; PostgreSQL and SQL Server snapshot/watermark reads and transactional keyed writes; RabbitMQ JSON ingestion/delivery with manual acknowledgements and publisher confirms. It also requires partition-owned positions, bounded local mapping/filter/validation/routing/lookups, record and byte admission, destination idempotency and stale-progress rejection. Workflow/BPMN invocation, parent outcomes and Studio remain delivery requirements. These are the full end state, not optional follow-ups to a files-only prototype. Log CDC, stage shuffle, windows/stream joins and general analytics remain deferred; physical effects remain at least once unless a connector proves stronger behavior.

| Surface | Current evidence and limit |
|---|---|
| Existing `Pipelines.Core` | [PipelineBuilder](https://github.com/elsa-workflows/elsa-foundation/blob/b656e3a8ed8bc59d02291da4af3912fd16b7139b/Essentials/src/Pipelines/Core/PipelineBuilder.cs) composes per-message middleware delegates. This is real production infrastructure, but supplies no ETL run/partition/cursor/checkpoint model. |
| Native durable background work | [BackgroundActivityWork](https://github.com/elsa-workflows/elsa-foundation/blob/b656e3a8ed8bc59d02291da4af3912fd16b7139b/Essentials/src/Workflows/Runtime/Core/Models/BackgroundActivityWork.cs) pins a workflow activity invocation, attempt, executable and correlated terminal receipt. Reusable mechanisms need evidence-led extraction; these workflow-specific types do not already implement ETL semantics or byte budgets. |
| Partition and outcome witnesses | The [historical acceptance audit](https://github.com/elsa-workflows/elsa-foundation/blob/b656e3a8ed8bc59d02291da4af3912fd16b7139b/docs/reports/unified-execution/acceptance-audit.md) describes experimental WorkProof/BpmnProof, not production Elsa integration. It explicitly retains separate non-atomic stores and experimental fault/cancel runs left Running with cancelled work. Do not adopt that limitation as production behavior or count the witness as completed ETL delivery. |
| Connectors and continuous lifecycle | Bounded inspection of current `Essentials/` and `Extensions/` found no production ETL connector/run implementation meeting this matrix. A workflow HTTP activity and SQL persistence-provider support are not those connectors. Real replay, acknowledgement and checkpoint evidence is still required per connector and per SQL engine. |
| Studio | Bounded inspection of `Studio/src` and `Studio/tests` found no ETL run/partition authoring or inspection surface. Connection-selection UI is not pipeline execution. |

Dependency correction: #2904 is already CLOSED/status:done with all its bounded architecture-packet criteria checked. Its completion should not be requested again. That does not qualify production ETL or make #2908 independently pickable: the ordered program/shared queue and actual ETL design gates still apply. The older deferred/readiness wording must be interpreted through the later approved continuation.

Future first-slice gate, after queue selection: specify one ETL execution across at least two independently leased partitions, one approved source/destination pair, bounded local transforms with record/byte admission, real effect-before-checkpoint recovery, destination idempotency, stale cursor rejection and explicit parent success/business-error/fault/cancellation mapping. Choose the first connector pair during that design; this audit does not ratify a provider order or new public API. Keep cursors/checkpoints ETL-owned, and demonstrate native/BPMN interoperability before stabilizing a common contract. Reuse the actual production runtime rather than reviving the experimental kernel. Expand through every approved connector, continuous lifecycle and integrated Studio before claiming #2908 complete.

<!-- etl-production-readiness-b656e3a8 -->

## id=6281858471 elsa-workflows-codex[bot] 2026-10-20T02:23:28Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6281858471
Consolidated approved connector/partition/transform/budget/replay/Studio acceptance on the owning feature; all production criteria remain unchecked, with no new ready leaf.

Program metadata reconciliation only. #3056 / PR #3087 remains the sole implementation leaf; no source, claim, review gate or external-release change.

<!-- program-coverage-reconciliation-20261020-2908 -->

## id=6288126715 elsa-workflows-codex[bot] 2026-10-20T09:44:30Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6288126715
First production child #3109 is now claimed for specification/implementation: [claim](https://github.com/elsa-workflows/elsa-foundation/issues/3109#issuecomment-6288097403). It uses the qualified built-in host and does not depend on deferred Orleans. Finite file/native/Studio evidence will not check off the full continuous/connector/BPMN feature.

<!-- 20261020-finite-etl-child-start -->

## id=6291968816 elsa-workflows-codex[bot] 2026-10-20T13:27:34Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6291968816
#3109 / draft PR #3111 now includes the bounded durable runtime at `8a74c3c0e2b75b706dbddafc8eb0a43bf866700e`: pinned admission/preparation, independently leased/reserved partitions, fenced accepted output and terminal outbox. Integrated local gate: 180 ETL + 579 architecture cases passed, no failures/skips; real host/shell lifecycle and exact safeguard-removal/restoration evidence are recorded. Fresh CI is pending. This is 14/36 tasks, not native/Studio or two-process recovery acceptance; the parent connector, continuous-processing and BPMN obligations remain open. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/8a74c3c0e2b75b706dbddafc8eb0a43bf866700e/specs/255-finite-file-etl/evidence/finite-file-foundations.md).

<!-- 2908-runtime-checkpoint-8a74c3c0e2b7 -->

## id=6293147078 elsa-workflows-codex[bot] 2026-10-20T14:24:43Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6293147078
#3109 / draft #3111 now includes native ETL component checkpoint `73f79539a4b52f451c0af12b3d791b63923c8bc7`: ordinary typed native wait, actual-bookmark admission, cancellation, scoped result delivery and hash-bound publication pin. Root's included-boundary self-review ended clean; 235 ETL + 579 architecture cases passed, zero failures/skips, with locked restores and map/filter checks. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/73f79539a4b52f451c0af12b3d791b63923c8bc7/specs/255-finite-file-etl/evidence/native-boundary-checkpoint.md).

Fresh exact-head CI is running. The complete finite ETL/native/Studio leaf remains In Progress / Verification Partial (14/36 tasks); real composite continuation, two-process recovery, materialization and API/Studio/browser are not yet accepted. This is not parent/program completion. Existing claim continues; no actor-provider work is resumed.

<!-- 2908-native-component-73f79539a4b5 -->

## id=6293849118 elsa-workflows-codex[bot] 2026-10-20T14:56:42Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6293849118
#3109 / draft #3111 at `bca4c08cc02f75f7d5d67256f832ba501ba950b4` now proves actual durable Sequence, Flowchart and CompensationScope ETL continuation, including a later scope fault with output retained and outer continuation. Generic callback provenance loss was reproduced/fixed; landed compensation retry/uncertainty behavior is preserved.

Final local evidence: 239 ETL + 66 compensation + 569 Activity Runtime + 579 architecture cases passed, zero failures/skips; locked restores/maps/filters and source-regression controls passed. [Integrated receipt](https://github.com/elsa-workflows/elsa-foundation/blob/bca4c08cc02f75f7d5d67256f832ba501ba950b4/specs/255-finite-file-etl/evidence/native-composite-integration.md). Fresh CI is pending/queued/running as recorded on the PR. The finite ETL/native/Studio leaf remains In Progress / Verification Partial, 16/36 tasks; terminal-outcome/cancellation, materialization, two-process recovery and API/Studio/browser gates remain open. This is not parent/program completion; existing claim continues and actor-provider work remains deferred.

<!-- 2908-composite-integrated-bca4c08cc02f -->

## id=6294183522 elsa-workflows-codex[bot] 2026-10-20T15:12:08Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6294183522
#3109 / draft #3111 at `5f5633dd17c8eadbacc25245856112788375339a` completes T020's actual native terminal-outcome matrix: BusinessError/Faulted/Cancelled in Sequence, Flowchart and CompensationScope, with typed result/receipt/outbox assertions and ordinary composite semantics. **248 ETL tests passed**, zero failures/skips; maps/filter/diff checks and a source-removal regression control passed. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/5f5633dd17c8eadbacc25245856112788375339a/specs/255-finite-file-etl/evidence/native-terminal-outcomes.md).

The leaf stays In Progress / Verification Partial at 17/36 tasks. Native-initiated cancellation/pre-admission ordering is next; final materialization, two-process recovery, workflow compiler publication and API/Studio/browser remain open. Fresh CI is pending as recorded on the PR. This is not the feature/program completion and does not resume deferred actor work.

<!-- 2908-native-outcomes-5f5633dd17c8 -->

## id=6294542257 elsa-workflows-codex[bot] 2026-10-20T15:29:00Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6294542257
#3109 / draft #3111 at `50681f79d7cee86c912e9210ac69279eee133544` now proves native whole-workflow cancellation into ETL and a cancellation tombstone preceding delayed admission, across Sequence/Flowchart/CompensationScope. Fixed an actual generic scheduler cleanup omission; cancelled waits are removed in the native checkpoint. **254 ETL / 2,431 workflow-runtime / 579 architecture tests passed**, zero failures/skips; real-server replay/restart, maps/filters/diff and removal/restoration controls passed. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/50681f79d7cee86c912e9210ac69279eee133544/specs/255-finite-file-etl/evidence/native-parent-cancellation.md).

T022 stays open for genuine activity-boundary subtree cancellation/reversed ordering. 17/36 tasks; leaf In Progress / Assigned / Verification Partial. Fresh exact-head CI is pending. Materialization, two-process recovery and API/Studio product acceptance remain owed. Parent/program stay open, existing claim continues, deferred actor work does not resume.

<!-- 2908-native-parent-cancellation-50681f79d7ce -->

## id=6295465966 elsa-workflows-codex[bot] 2026-10-20T16:11:44Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6295465966
#3109 / draft #3111 at `ddb3f5a3652c7bc3c2dcc15d52a13a84f12d1fd2` completes T022: 12 actual native whole-workflow/placed-subtree cancellation cases across all three composites and both admission orderings, plus 3 placed normal-completion controls. Fixed the missing ETL published resume declaration and preserved actual placement-generated target identities in frozen state. **273 ETL / 2,436 workflow-runtime / 579 architecture passed**, zero failures/skips; final native subset28 passed; locked restore/maps/filter/diff and removal/restoration passed. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/ddb3f5a3652c7bc3c2dcc15d52a13a84f12d1fd2/specs/255-finite-file-etl/evidence/native-subtree-cancellation.md).

18/36 tasks; leaf In Progress / Assigned / Verification Partial. Fresh exact-head CI remains separate. Materialization, live permits, two-process recovery and persisted authoring/API/Studio product gates remain owed. Parent/program stay open; existing claim continues; optional actor work stays deferred.

<!-- 2908-native-subtree-cancellation-ddb3f5a3652c -->

## id=6295899974 elsa-workflows-codex[bot] 2026-10-20T16:32:56Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6295899974
#3109 / draft #3111 at `1aacfc90977d8c579ff33f0ce8ef7406db13e24b` completes T016: deterministic terminal JSONL materialization from verified manifest-accepted chunks, retained output pins, replay/conflict refusal and explicit cancelled/faulted partials. **285 ETL / 579 architecture passed**, zero failures/skips; focused filter13, digest removal/restoration and maps/filter/diff passed. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/1aacfc90977d8c579ff33f0ce8ef7406db13e24b/specs/255-finite-file-etl/evidence/terminal-materialization.md). 19/36 tasks; In Progress / Assigned / Verification Partial. Fresh CI pending. Previous Studio head failed known #3081 readiness race (seed11), not an ETL source change; supporting repair is being handled in the same lane. API/Studio exposure, live permits, two-process recovery and full publication remain owed; parent/program stay open, claim6288815023 continues.

<!-- 2908-terminal-materialization-1aacfc90977d -->

## id=6296268588 elsa-workflows-codex[bot] 2026-10-20T16:51:23Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6296268588
Supporting CI repair for active #3109 / draft #3111 is pushed at `ee18ca9de7ada3ab105125771d81a2cc055d753c`. Deterministic delayed-waits red proof reproduced #3081; actual editor/form/feedback waits preserve all request/UUID assertions. Integrated shuffled seeds1/11/29 each passed2124 tests; typecheck/ESLint/maps/filter/diff passed, three root self-review passes ended clean. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/ee18ca9de7ada3ab105125771d81a2cc055d753c/specs/255-finite-file-etl/evidence/studio-readiness-repair.md). Fresh CI pending (Studio CI 39460945666: in_progress / no conclusion; CI 39460945997: in_progress / no conclusion); 19/36 tasks, program/product incomplete, one lane unchanged. Backend source unchanged from T016. Standing self-review/normal self-merge authority needs no independent reviewer; draft remains until product acceptance.

<!-- 2908-readiness-ee18ca9de7ad -->

## id=6296566127 elsa-workflows-codex[bot] 2026-10-20T17:06:50Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6296566127
Active #3109 / draft #3111 at `9dfa6993f899b916ff5bd1950a07943d392da946` completes T021 (20/36 tasks). Actual file execution/SQLite takeover preserves owed reservations and four independent local allowances until cancellation/cleanup unwind; stale acceptance refuses, successor owns its own permit and commits one matching effect. **287 ETL / 579 architecture passed**, zero failures/skips; final original-source removal2fail/byte-exact restore287pass, maps/filter/diff passed. Three root self-review passes ended clean. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/9dfa6993f899b916ff5bd1950a07943d392da946/specs/255-finite-file-etl/evidence/live-attempt-replacement.md). Fresh exact-head CI pending; prior-head checks retain separate provenance. Separate-process interruption, full publication/API/Studio acceptance remain owed. One claim/lane continues; program incomplete, Orleans deferred.

<!-- 2908-live-attempt-9dfa6993f899 -->

## id=6296811255 elsa-workflows-codex[bot] 2026-10-20T17:19:18Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6296811255
#3109 / draft #3111 at `b6cd2f27a7e38288b52d6dd460582972a6d5d127` completes T023 (21/36): two real fixture workers over shared SQLite/files, owned predecessor exit after publication and survivor higher-fence same-effect replay/one acceptance. **288 ETL / 579 architecture passed**, zero failures/skips; focused3/locked restore/maps/filter/diff passed, three root self-review passes clean. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/b6cd2f27a7e38288b52d6dd460582972a6d5d127/specs/255-finite-file-etl/evidence/two-process-recovery.md). Fresh exact-head CI pending; no independent approval gate. Full interruption/recovery matrix, persisted publication/API/Studio acceptance remain owed; one claim/lane and draft/product gate continue. Program incomplete; Orleans deferred.

<!-- 2908-process-b6cd2f27a7e3 -->

## id=6297309289 elsa-workflows-codex[bot] 2026-10-20T17:44:38Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6297309289
#3109 / draft #3111 `51302776b4040f68477094cc832a3fdc9fd3ac15` adds actual source/JSONL staging interruption and full replacement publication before acceptance, plus SQLite stale-delivery fencing. **291 ETL/579 architecture passed**, zero failures/skips; locked restore/maps/filters/diff passed. Bypassing composed services explicitly fails both midpoint cases; exact restoration passes. [Evidence](https://github.com/elsa-workflows/elsa-foundation/blob/51302776b4040f68477094cc832a3fdc9fd3ac15/specs/255-finite-file-etl/evidence/staged-write-recovery.md). T024 partial, 21/36 unchanged; full persisted publication/API/Studio and remaining recovery acceptance owed. Sole claim/lane continues; Orleans deferred; program incomplete.

<!-- 2908-staging-51302776b404 -->

## id=6297854256 elsa-workflows-codex[bot] 2026-10-20T18:12:50Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6297854256
#3109 / draft #3111 `3164c1721f587548c0b499d7c44083de1848ad06` completes T024 (**22/36**): actual process read/reservation/staging/publication/committed-cursor/result-claim interruption, conflicting output refusal and separate SQLite/native replay proofs. **297 ETL/604 activity-runtime/202 compensation/579 architecture passed**, zero failures/skips; CI restore graphs/maps/filters/diff and three rebuilt native REST journeys passed. [Matrix](https://github.com/elsa-workflows/elsa-foundation/blob/3164c1721f587548c0b499d7c44083de1848ad06/specs/255-finite-file-etl/evidence/interruption-matrix.md); [new-main lock/provenance integration](https://github.com/elsa-workflows/elsa-foundation/blob/3164c1721f587548c0b499d7c44083de1848ad06/specs/255-finite-file-etl/evidence/ci-merge-lock-integration.md). Fresh CI pending; PR stays draft. Next T018 real persisted compiler fan-in/runtime-only proof; picker/API/Studio and wider T025 acceptance remain owed. Overall claim/one lane continues; program incomplete; Orleans deferred.

<!-- 2908-recovery-3164c1721f58 -->

## id=6298505632 elsa-workflows-codex[bot] 2026-10-20T18:44:52Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6298505632
#3109 / draft #3111 `e558abc37204e6eb930c5df3729d7f61061f7a59` adds the genuine persisted parent publication/compiler proof for Sequence and Flowchart, then executes retained ETL v1 after v2 publication in a runtime-only provider with the ETL design database offline. **299 ETL/579 architecture passed**, zero failures/skips; both cases fail under omitted production pin contribution and byte-exact restoration passes299. CI restore graphs/maps/filters/diff passed. [Evidence and limits](https://github.com/elsa-workflows/elsa-foundation/blob/e558abc37204e6eb930c5df3729d7f61061f7a59/specs/255-finite-file-etl/evidence/persisted-parent-publication.md). **22/36; T018 partial** until picker/catalog. Next scoped authoring/published-version APIs; product/program incomplete, PR draft, overall claim active. Fresh exact-head CI pending; prior native/process evidence keeps its own provenance. No independent approval gate or actor-provider prerequisite.

<!-- 2908-persisted-parent-e558abc37204 -->

## id=6299407521 elsa-workflows-codex[bot] 2026-10-20T19:26:12Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6299407521
Finite-file leaf #3109 / draft #3111 checkpoint `7fdca1f94b3d398f649e442dd245d06c624157c3` completes T011: nine scoped authoring/binding/published-version operations over actual SQLite/HTTP, with exact tuple replay including a deterministic concurrent-read race. **338 ETL / 581 architecture passed**, zero failures/skips. Removing the production replay recheck fails the expected status assertion; exact restoration passes338. Locked CI restore graphs/maps/filters/diff and three rebuilt native REST smokes passed. [Evidence and limits](https://github.com/elsa-workflows/elsa-foundation/blob/7fdca1f94b3d398f649e442dd245d06c624157c3/specs/255-finite-file-etl/evidence/authoring-api.md). **23/36; T018 picker/runtime API/Studio/ETL host-browser/provider and wider terminal acceptance remain owed.** Parent connector/BPMN/continuous obligations remain open. PR draft, overall claim active, fresh exact-head CI pending. Task-runner intent is a separate owner-recorded follow-up, not scope expansion.

<!-- 2908-authoring-api-7fdca1f94b3d -->

## id=6300713314 elsa-workflows-codex[bot] 2026-10-20T20:25:43Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6300713314
T028 complete at `b47e59b332ea4823e7cf86e5b52029ddc43fa226` in draft #3111: **24/36 finite-ETL tasks checked**.

Four runtime-only NativeEndpoints operations now expose scoped run/partition lists, safe retained run detail and idempotent cancellation. They compose independently of design services and compute workers. Signed cursors bind scope, kind, page limit and run; bounded store reads honor provider limits without changing provider keyset order. Cancellation resolves the retained invocation and returns the actual terminal outcome, including a previously settled result. Safe detail includes source/lookup binding revision, expected digest and last verified preparation identity, with no storage path, object key, worker identity or native result address. Inspection is not a fresh input-integrity check; cancellation does not reverse external effects or prove workers stopped.

Final rebuilt suite: **346 ETL tests / 583 architecture checks passed, zero failures or skips**. Removing only the cursor resource comparison causes the actual HTTP test to return OK instead of BadRequest for another run; exact restoration and subsequent final tests passed. CI Release/isolated-Debug locked restores, map regeneration/freshness, solution filters, local links and diff review passed. The earlier architecture attempt correctly refused an assembly older than the final input-identity edit; the final Release rebuild resolves that provenance issue. Root self-review checked dependency closure, provider paging, retained identities, cancellation races and the final diff; no remaining actionable finding in T028.

[Executed evidence and limits](https://github.com/elsa-workflows/elsa-foundation/blob/b47e59b332ea4823e7cf86e5b52029ddc43fa226/specs/255-finite-file-etl/evidence/runtime-api.md). T028 scope6299493547 is complete/pushed. Overall claim6288815023 and Studio scope6300345739 remain active in the same branch. T018 picker, T029/T030 Studio, T025 terminal/address/corrupt-format matrix, T027 provider and T031/T032 integrated ETL Workbench/browser acceptance remain owed. Studio preparation is deliberately not included in this commit. Prior persisted-parent publication and two-process T024 recovery are already proved with their recorded provenance. This checkpoint does not complete the leaf, parent connectors/BPMN or program. Future task-runner/pause discussion remains outside this implementation.

Fresh exact-head checks: Studio CI 39491711301: queued / no conclusion; CI 39491711294: queued / no conclusion. Previous-head green checks do not qualify this commit; cancelled or skipped jobs are not passes. Historical receipt/HTTP timeout causes remain unproved; physical effects remain at least once.

<!-- 2908-runtime-api-b47e59b332ea -->

## id=6306600605 elsa-workflows-codex[bot] 2026-10-21T02:36:38Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6306600605
Next production slice #3151 is claimed for specification and delivery: paginated HTTP input, durable JSON delivery, mixed file/HTTP pipelines, native continuation and Studio. The issue records the supported protocol, connection policy, compatibility and failure/recovery proof. Existing file v1 is preserved. No claim of completed HTTP behavior; #3109 post-merge verification remains running.

<!-- 2908-http-start-3151 -->

## id=6307906760 elsa-workflows-codex[bot] 2026-10-21T04:16:40Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6307906760
#3109 is verified on resulting-main `da9a4927d98fb29eda017b4e1a74a28ee4316439`; the child row is checked and its exact-SHA evidence is in the [PR #3111 receipt](https://github.com/elsa-workflows/elsa-foundation/pull/3111#issuecomment-6307901682). Parent #2908 acceptance remains open, and #3151 retains its full HTTP/native/Studio scope.

<!-- etl-2908-3109-closeout-da9a4927-20261021 -->

## id=6323106536 elsa-workflows-codex[bot] 2026-10-21T18:09:47Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6323106536
Finite HTTP/native/Studio child #3151 merged in PR #3156 as `9462cb743c08a515e9b8973c57110e3f44df0f7f`. [Evidence](https://github.com/elsa-workflows/elsa-foundation/pull/3156#issuecomment-6323105067). Exact reviewed-head checks passed; resulting-main verification is pending and the child is not yet verification-complete. This does not close continuous processing, SQL, RabbitMQ, JSONL input or BPMN invocation obligations. Existing finite CSV-to-JSONL completion remains separately recorded.

<!-- 2908-http-merged-pending-9462cb743 -->

## id=6323246976 elsa-workflows-codex[bot] 2026-10-21T18:16:38Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6323246976
Remaining provider qualification made explicit during successor preparation: [ADR0073 D1](https://github.com/elsa-workflows/elsa-foundation/blob/9462cb743c08a515e9b8973c57110e3f44df0f7f/docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md#d1--ef-core-is-the-only-first-party-persistence-implementation-family) names SQLite, SQL Server, PostgreSQL and MySQL for first-party durable contracts. Current [ETL runtime binding](https://github.com/elsa-workflows/elsa-foundation/blob/9462cb743c08a515e9b8973c57110e3f44df0f7f/Extensions/Etl/src/Persistence/EntityFrameworkCore/DependencyInjection/EtlRuntimeEntityFrameworkCoreRegistration.cs#L36) explicitly supports only SQLite; the design binding is likewise SQLite-only.

The delivered finite file and newly merged HTTP leaves qualify their named SQLite profile. They do not establish the remaining runtime/design persistence providers. This is distinct from PostgreSQL/SQL Server source and destination connectors: a pipeline reading a SQL database does not prove that its authoritative run store supports that database engine.

Keep all four runtime/design provider implementations, migration/scope/claim/receipt/cancellation/outbox contract tests and actual provider evidence in the parent completion audit. A local shared-file multi-process test is not cross-machine storage qualification. The next bounded provider/connector specification must make this dependency explicit rather than inheriting qualification from EF composition alone. No new leaf is activated, no current leaf is expanded, and no provider-policy exception is claimed. #3151 post-merge checks remain the sole active delivery gate.

<!-- 2908-runtime-provider-gap-9462cb743 -->

## id=6324180849 elsa-workflows-codex[bot] 2026-10-21T19:03:59Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6324180849
Finite HTTP child #3151 is verified and its delivery claim released. [Exact merge CI/Studio/CodeQL/maps and explicit skips](https://github.com/elsa-workflows/elsa-foundation/pull/3156#issuecomment-6324159475). The finite HTTP criterion is checked; finite file evidence remains recorded. Continuous processing, remaining file directions, both SQL connectors, RabbitMQ, BPMN invocation and four-provider design/runtime persistence qualification remain open. Source-record closeout #3182 is still in the program integration lane.

<!-- 2908-http3151-verified-9462cb743 -->

## id=6393884289 elsa-workflows-codex[bot] 2026-10-25T05:23:50Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6393884289
[Codex] On behalf of @sfmskywalker.

**Prepared next-spec candidate, not an activated implementation:** finite JSON Lines file input into the existing JSONL/HTTP destinations. This is a recommendation inferred from the current reuse surface, not a new product-ordering decision or claim. Keep the full #2908 acceptance open.

Source snapshot `6e677745bb828dcb1117379859068f8e735dac47`:
- [V2 declarations](https://github.com/elsa-workflows/elsa-foundation/blob/6e677745bb828dcb1117379859068f8e735dac47/Extensions/Etl/src/Core/EtlPipelineV2.cs#L26-L93) expose only CSV/HTTP JSON sources and JSONL/HTTP destinations. Typed JSON Pointer fields already exist, so JSONL input can potentially reuse extraction/transform/output rather than introduce another output format.
- [Prepared state and positions](https://github.com/elsa-workflows/elsa-foundation/blob/6e677745bb828dcb1117379859068f8e735dac47/Extensions/Etl/src/Core/Runtime/IEtlConnectorRuntimeStore.cs#L71-L100) use `EtlPreparedCsvInput` and durable file ordinal ranges. A generic prepared-file record or new persisted case needs an explicit compatibility decision; do not silently rename stored DTOs.
- [Current input builder](https://github.com/elsa-workflows/elsa-foundation/blob/6e677745bb828dcb1117379859068f8e735dac47/Extensions/Etl/src/Runtime/Elsa.Etl.Runtime/Execution/EtlConnectorInputBatchBuilder.cs#L50-L133) supports CSV snapshots and retained HTTP pages. New framing/indexing is not already delivered by that code or prior CSV/HTTP proofs.
- [ETL runtime EF registration](https://github.com/elsa-workflows/elsa-foundation/blob/6e677745bb828dcb1117379859068f8e735dac47/Extensions/Etl/src/Persistence/EntityFrameworkCore/DependencyInjection/EtlRuntimeEntityFrameworkCoreRegistration.cs#L24-L55) still rejects non-SQLite providers. Adding a SQL data connector and qualifying the ETL persistence stores on SQL Server/PostgreSQL/MySQL are separate obligations.

Before a leaf is ready, settle and specify: UTF-8/BOM and LF/CRLF/final-line/blank-line framing; accepted top-level JSON shape; malformed/overlong records versus field errors and their terminal/error-route policy; compatibility of prepared inputs; deterministic disjoint partition indexes and restart positions; accounting against existing logical record/batch/snapshot/index/output limits. Retain host-only paths/credentials, immutable source/binding revisions and digests.

The eventual vertical slice must prove a real native/Studio publication-run-inspection-continuation journey plus persisted worker replacement at reservation/output/route/acceptance boundaries, cancellation and duplicate/late work. Reuse retained output/effect identity; physical delivery remains at least once absent an independently proved stronger destination guarantee. Existing recovery tests are scaffolding, not JSONL-source evidence.

CSV output, continuous processing, SQL/RabbitMQ, BPMN invocation, additional EF providers and task-runner semantics remain distinct work. No spec branch, child issue, ready label, source edit or implementation claim is created by this research checkpoint. BPMN release #95 is now complete; its Foundation adoption still follows #2735 ownership/parity coordination.

<!-- etl-next-spec-research-20261025 -->

## id=6526361036 elsa-workflows-codex[bot] 2026-10-30T15:22:20Z https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6526361036
[Codex] On behalf of @sfmskywalker.

Existing draft PR3410 / #2898, head `bbb21b25b1a2fa40c900d4374c2a979ff21377f4`, now source-qualifies real BPMN→native RunEtl success, business error, technical fault and cancellation, including inverse cancellation ordering/recovery directly and through all four native parent shapes. It uses actual finite HTTP429/retry/output paths and durable work, not one workflow per record. [Current checkpoint](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6526294297) indexes exact sources/whole stores and limits; full-PR round2 is running. This unmerged finite integration proof does not complete upgraded-host ETL, continuous processing, SQL/RabbitMQ connectors, four-engine ETL-store qualification or integrated ETL Studio obligations. Existing delivered file/HTTP child receipts remain valid; every unchecked feature criterion stays open. After #2898 delivery, prioritize a real ETL saved-checkpoint/failure/restart/replay journey and its Studio observation, then continue the approved connector/continuous scope. No new implementation leaf or competing claim is activated here.
