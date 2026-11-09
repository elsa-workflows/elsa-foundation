# Native Rules intent and retry checkpoint

This checkpoint extends the [native components](native-component-checkpoint.md). It does not complete T027–T032 or claim a runnable Rules native host. The delivery worker, native terminal proof validator, runtime registration, actual catalog publication, persisted Sequence/Flowchart journeys, REST acceptance and six-module package/upgrade qualification remain required.

## Implemented boundary

- `RulesIntentHandler` validates the frozen checkpoint intent and partition before addressing a Rules run. An exact retained admission settles Start even after native bookmark cleanup. Valid cancellation records a tombstone before admission when necessary. Explicit store unavailability retains the outbox responsibility. Invalid round budgets never create a synthetic Rules row: their intent remains pending until the exact consumed native receipt and final faulted resume attempt, or the matching terminal parent, is proven. Multiple nonoverlapping retry attempts for the same delivery are permitted; enqueue or duplicate-command acknowledgement is not proof.
- The Rules delivery-proof interface now receives the invocation decoded from the retained store row as well as the terminal envelope and proposed proof. The caller cannot select the authoritative tenant/partition or invocation fingerprint. The EF store still checks its delivery lease and rereads state after validation, outside the validator call's transaction.
- `RunRules.ResumeAsync` classifies explicit Rules-reader `Unavailable` results as a temporary runtime dependency condition. The native handler preserves its consumed trigger payload and private state, disposes the activation, and propagates that condition. A disposal failure still records the existing native activity fault.
- Bookmark attempt-claim checkpoints retain their source scheduler work when a consume accessor is shared. Ordinary outcome checkpoints retain the existing consumption behavior. In the default production composition the bookmark handler opens a fresh DI scope and its consume accessor differs from the outer drainer's; the outer durable claim remains responsible for acknowledgement. These are distinct paths, both covered by focused tests.
- Before a coalesced queue claims a `ResumeBookmark` FIFO head, the orchestrator flushes its existing segment and deactivates the overlay. The next claim therefore comes from the real provider. An explicitly unavailable resume releases that same live, unconsumed claim with a positive delay and stops as `Deferred`; it does not acknowledge or poison the work. Normal queue visibility/backlog discovery drives retry. Ordinary schedule work continues to coalesce.

The delayed-deferral behavior is deliberately limited to bookmark resumes. Unsupported command kinds, legacy queues without claims, already-consumed work and stale releases do not receive a false durability guarantee. They propagate failure without entering generic acknowledge/poison handling. The general task-runner remains separate follow-up scope.

## Investigation correction

The preceding checkpoint proposed inspecting poison retry. Source inspection found no current poison `RetryScheduled`/`NextRetryAt` resumption reader, and the generic fault path acknowledges before poison persistence. That mechanism is not used for this recovery guarantee. The initial concern about automatic consumption at the bookmark claim is conditional on shared consume accessors; the default fresh handler scope does not share one. The independently confirmed production gap was overlay-only claim release failing to persist delay to an already-existing durable row.

No generic poison-policy change, paired overlay/provider lease machinery, schema change, workflow configuration change, or Orleans work is included. The pre-claim boundary trades further coalescing in that drain for an explicit durable lease; no performance claim is made.

## Evidence boundaries

The native handler test drives the real resume handler and drainer through consumed-trigger recovery using in-memory stores and a newly constructed provider. The coalescing test drives the real orchestrator, queue decorators, checkpoint committer and scope factory with shared in-memory persistence. The EF queue test disposes and reopens the database adapter and verifies the stored visibility timestamp, scoped discovery and same work identity. These compose useful boundary evidence; they are not a full Rules workflow process-restart proof.

The invalid-budget intent fixture constructs native state and uses store/dispatcher doubles. The separate Rules delivery-store test checks the authoritative retained invocation. Neither is the T032 native proof-before-acknowledgement acceptance control. External effects remain at least once.

## Verification

The final intact suites passed with zero failures or skips: Rules 237, workflow runtime 2,540, activity runtime 662, focused EF scheduler/checkpoint 17, and architecture 585. The first full architecture run passed 580 and failed 5: the previous checkpoint built the Rules design assembly without loading it in the architecture project, omitted its runtime assembly, and lacked isolated Debug restore assets for the new projects. The two Rules references now load normally, their lock graph is refreshed, and the unchanged composition, scan coverage and EF dependency guards all pass. Open PRs #2825 and #2728 touch the same project only for separate Integrations modules; their patches were inspected and preserved.

Local map generation and the committed solution-filter check passed. Both findings reports were inspected: no runtime-to-design signals or missing catalog links. Existing Rules Design-to-runtime references are the publication compiler and activity/runtime contracts. Generated documentation maps remain ignored and are not staged. Workbench rebuilt successfully with existing package-pruning and Pkcs advisory warnings; no suppressions were added.

An owned rebuilt Workbench with fresh disposable SQLite databases passed the existing Sequence, Event suspend/resume and stimulus-routing REST scripts. Sequence and the resumed workflow completed with zero incidents; all three stimulus-routing cases passed. The harness stopped only its own server and removed its disposable content root. These are shared-runtime regressions, not Rules native REST acceptance.

The [log receipt](native-retry-receipt.json) records commands, raw log locations, SHA-256 digests and outcomes. The complete Rules REST/package-host gates remain open.

### Source-removal controls

1. Removing only the explicit unavailable rethrow after native resume made `Unavailable_resume_keeps_native_work_and_retries_consumed_payload_in_a_fresh_activation` fail its `StoppedOnDeferral` assertion. The source was restored byte-for-byte; the subsequent full 662-test activity runtime suite passed.
2. Disabling only the coalescing queue's `ResumeBookmark` boundary made `Resume_boundary_flushes_coalesced_work_then_durable_deferral_survives_provider_rebuild` fail its expected durable-commit assertion (expected 1, actual 0). The source was restored byte-for-byte; the subsequent full 2,540-test workflow runtime suite passed.

These are narrowly scoped retry-boundary controls, not T032 native proof-before-acknowledgement acceptance.

## Review and next integration

Root review corrected the consume-accessor assumption, limited delayed deferral to the supported bookmark command, preserved the existing disposal-failure path, and restored architecture loading coverage. The final independent read-only review found no further defect in the supported retry surface: renewal stops before release, the latest fenced claim is used, and cancellation or stale ownership never falls into generic acknowledgement/poison handling. Legacy queues retain work but cannot provide the configured claim-release delay. This is a clean component checkpoint, not a clean whole-leaf integration gate.

The next bounded slice is native result delivery and durable outcome-proof validation, followed by feature registration and real published Sequence/Flowchart journeys. T027–T032 remain unchecked. Prior four-provider, four-module package and process receipts describe their recorded immutable inputs; they do not automatically qualify this changed six-module native surface. General task-runner and Orleans work remain outside the slice.
