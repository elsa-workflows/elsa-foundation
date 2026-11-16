# PR3410 complete-PR round8 acceptance review

Verdict: **NOT CLEAN — one P2 finding.**

Reviewed head: `e72641e6714768987f555b52ebd36dbc129959a5`.
Base: `3e93e06d9bd22763c81767ca296c8924142d030b`.
Fresh read-only GitHub API read confirms that exact remote head and draft state. The candidate checkpoint is PR comment `6545066284`; the only additional comment at the final refresh is the repeated CLA notice `6545082912`. No other round8 reviewer report was read. No repository edits, GitHub mutations, push, host interruption or subagents.

## A1 — P2: cancellation still acknowledges an old fault frame before its retained native work is settled

**Location:** `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnEmbeddingDelivery.cs:63-66`, with the incomplete check at `BpmnEmbeddingBoundary.cs:200-203`.

The terminal-result fast return remains unsafe for a supported preserved legacy frame. `RequireWorkerCancellationSettled` only checks an explicit `Cancellations` inventory. An old embedded final-fault frame can validly have `TerminalResult=Faulted`, payload format5, one retained native `Work` and **no** `Cancellations` yet. That exact frame is already an actual committed upgrade input in this PR. Its worker is physically Running with an Event bookmark.

When the native parent cancels before the old model's parent-result acknowledgment introduces cleanup, its `CancelEmbedded` delivery reaches this fast return. The new guard sees no cancellation entries and returns successfully without queuing `cancel-embedded`. The source can therefore be marked Delivered while the exact old native worker is still Running. In the supported nested A → native RunBpmn → B composition, that Delivered source satisfies A's native cancellation-result inventory and permits A to report TerminalSettled before B's remaining native work has settled. Eventual parent-ACK/startup cleanup does not make that earlier acknowledgment valid.

This is the legacy-state arm of the same settlement invariant corrected in round7, within FR005/FR013/FR016/FR017 and saved-state compatibility. It does not request a broader deferred product criterion or a new capability. Preserve the old semantic fault. When a terminal fault still retains native work, route the existing cancellation decision/cleanup path and keep cancellation delivery owed until that physical responsibility settles; simply blocking the shared parent-ACK guard on Work would need care because that ACK is itself an existing cleanup trigger.

### Reproduction and controls

- Read the **unchanged actual** input `specs/1017-direct-bpmn-hosting/evidence/reviews/round-5/acceptance/old-embedded/old-pending-parent.snapshot.sqlite.gz`, decoded SHA256 `0a5ea64058dcdb950fdda9e5b792264ceec2f6ca19fa9398a7733f6c62ea097e`.
- Physical immutable SQLite read: integrity ok, model TerminalPendingSettlement, format5, Faulted result, Work1, no cancellation inventory; exact retained worker Running with one bookmark. See `fault-cancel-probe/original-state-summary.json`.
- The private compiled boundary probe deserializes the original persisted model using the actual EF Runtime serializer and passes `BpmnDirectExecutionDriver.Read`. It constructs a matching claimed CancelEmbedded transport around that original invocation. The existing compiled `BpmnEmbeddingDelivery.HandleAsync` returns successfully; the settlement assertion fails. No state, identity, pin or JSON payload in the original model is rewritten.
- The same compiled path passes the actual recovered counterpart from `reviews/round-5/qualification/fault-recovery/old-pending-parent-fault/completed-runtime.sqlite.gz`: format14, Work0, all cancellation obligations settled.
- A private copy of only the delivery class with one supplemental retained-native-work guard refuses the old frame and still accepts the recovered control: **baseline 1 required failure/1 pass; control 2 passes**. This isolates the fast return; it is a causal guard control, not a production repair or recovery proof.
- Probe assembly provenance is **1cee5acd4**, not relabeled exact-head build. The implicated delivery, embedding boundary and driver source files are byte-identical at reviewed e726; their hashes/equality are in `fault-cancel-probe/source-equivalence.json`. Actual product SHA256/version are emitted by the probe. Raw outputs and exits are in `fault-cancel-results.json`; both private projects build successfully through normal dotnet PATH with no bypass.

**Limit:** this is a source-equivalent transport-boundary counterexample using an actual old whole-store model, plus a physical Running-worker observation. It is **not** a new actual nested Workbench/ETL cancellation run and does not claim an observed orphaned ETL pipeline. The earlier round7 ordinary-order capture remains outer-Pending, not a full cancellation pass. The current pending-input recovery test remains a separate positive.

## Full-scope acceptance coverage considered

Read the repository AGENTS and fresh org protocol cache, spec1017 spec/plan/tasks/contracts and current qualification lineage, the complete change inventory and the relevant direct/native/material/checkpoint/dispatch/retention/migration/API/legacy paths. All PR/issue comments were indexed and the discussion's substantive owner directions, prior seven full-round findings/corrections, issue bodies and related2892/2907/2908 scope were considered. Supplied PR reviews and inline comments are empty. Comment corpus indexing and deduplicated extraction remain private alongside this report. Fresh remote head/checkpoint read is retained.

The finding above does not negate the following verified source-bound positives:

- **Actual current saved Studio:** independently ran the unchanged strict verifier over sourceb7's current bundle: both families, four completed new runs, twelve closed coupled stores, nine original-run observations,84/92 original materials/incarnations, exact authoring/layout/XML and complete pins. Independently ran all **13** current-bundle final controls: pass, zero failure/error/skip. Inspected all **12** current screenshots, including both publication dialogs, original runs, distinct Published/Test Run details and post-restart views. This remains existing embedded Studio compatibility; directModelStudioAcceptanceProved=false.
- **Parallel recovery:** independently verified four actual direct/native parent pairs, eight completed models,24 whole stores, preserved partial branches and eight single logical joins; exact traces with only the declared local-handle normalization.
- **Actual ETL duration matrix:** independently verified all15 direct/Root/Sequence/Flowchart/CompensationScope worker-first/timer-first/held-interruption journeys and135 stores. The selected finite HTTP pipeline's durable cancellation/remaining work and preserved terminal truth are checked separately from physical at-least-once effects.
- **Actual embedded ETL-origin cancellation:** independently verified all four native-parent case manifests; four passes,24 whole coupled stores. These remain their original sourced3b receipts, not current-head reruns.
- **Held retry:** independently verified all five actual journeys/45 stores, one distinguishable retry and retained old fault evidence. Budget/authorization/obsolete-result arms have separate linked receipts; this older verifier's false fields are not reinterpreted as passes.
- **Broadcast:** independently verified the original populated/later-registration/rearm/empty cohort captures,18 whole stores. Delivery-time selection remains distinct from publication time.
- **Admission-off rollback/retention:** independently verified five actual cases/25 stores, unused control collection, retained accepted work with Design/new admission disabled. No destructive binary/schema downgrade or other-provider hosting is inferred.
- **Immutable assets:** independently rehashed all104 original frozen assets,174 latest-main bundle assets and17 round7-correction assets, including declared decoded hashes: zero mismatches. Actual old fault and recovered control were separately read immutably.
- **Current affected host gate:** parsed the retained source8e9 whole-host TRX:43 Passed, zero Failed/NotExecuted, including the original impossible timer/direct recovery, all six legacy families, current template lineage and original pending nested cancellation. Relevant backend source remains unchanged in the later candidate integration. This review did not rerun the15-minute suite or relabel its provenance.
- The latest Studio/frontend/browser/API gate counts retain their executing sources. Earlier failed auth attempts, failed list/canvas runs, discarded separator hypothesis, cancellation/configuration diagnostics, pool-stress NotExecuted, historical optional probes and prior CI failures stay distinct.

## Delivery and scope limits

Draft, exact-head required CI, clean review, protected merge and resulting-main verification remain separate gates. This review never treats pending CI/draft as an acceptance finding. Owner-approved16-round bound, scoped4.0 preview exception and incident-specific public-fingerprint false-positive resolution remain settled. Existing2735 is delivered; broader first-class direct-model Studio,2907/2908/2892 acceptance, non-SQLite actual-host/clustered proof and Orleans/task-runner/alteration deferrals are unchanged. No broad new matrix is requested by A1.

Evidence directory: `/tmp/elsa-2898-review8-acceptance`.
