# PR #3410 — full-PR self-review round 6, acceptance lens

Verdict: **no actionable acceptance findings** at `d1449c0bff679212f8a64766a95b1c29cfadbcea` against `d481d6a5e624e35bba9fbf32033ed02aff64cbef`.

This is the sixth and final round under the existing six-round limit. Five preceding completed rounds were not clean. This report supplies one independent lens; it does not declare the other lenses clean, authorize a merge, or claim delivery on main. The authoritative start is https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6536773095.

## Scope and discussion

The review used current repository AGENTS.md, the supplied current organization protocol, the routed Studio context plan, spec1017's complete spec/plan/tasks/research/data model/hosting contract/quickstart/review index, FR001–FR022/SC001–SC006 mapping, complete changed-path inventory and the relevant production, test, migration, qualification and compatibility changes. It was not restricted to round5 repairs. Earlier complete-round findings and their original red/green/evidence dispositions were reconciled against the present implementation.

The final supplied discussion snapshot was copied to `final-inputs/` before reconciliation. Complete bodies, chronological records and per-comment hashes are retained here. All issue bodies, issue/PR discussions and supplied metadata were included, with repeated bot/CLA text recognized as repetition rather than a new owner decision.

| Surface | Unique issue comments |
|---|---:|
| Program #2892 | 289 |
| Task #2898 | 97 |
| Feature #2907 | 13 |
| Feature #2908 | 25 |
| PR #3410 conversation | 244 |
| Total | **668** |

There are **0 submitted PR reviews, 0 inline review comments and 0 review threads** in the supplied snapshot. The final refresh adds the three identical round6-start checkpoints and two CLA bot requests; it contains no new human direction, release of the current claim or new acceptance hold. The owner continuation, direct-recovery-first priority, 29 October goal, delivery-time broadcast decision, compensation logical-operation ownership and explicit product/provider deferrals were considered. Historical research, upstream #2735 delivery and current #2898 production evidence retain their different statuses.

`discussion-audit-final.json`, `discussion-*.md`, `final-inputs/discussion-snapshot.json`, `full-diff-paths.txt`, `diff-paths.txt` and `production-review.diff` preserve this scope. No other current round6 reviewer's output was read.

## Acceptance assessment

The following evidence paths are relative to `specs/1017-direct-bpmn-hosting/evidence/` at the reviewed commit. They are source-qualified receipts, not claims that every historical journey executed at the final documentation head.

| Requirement | Assessment and principal evidence |
|---|---|
| FR001 | Direct model identity is independent of a native root; runtime-only parallel recovery and paired native embedding retain the same interpreter. `direct-recovery/runtime-only-parallel`, `embedding/parallel-native-recovery`. |
| FR002 | Released 0.5.0 semantics, closed profiles and bounded normalization are explicit; direct/embedded partial-fork and service/duration comparisons retain separate physical identities. |
| FR003 | The shared path owns material, queue, fences and commit participation; the BPMN driver evaluates released semantics and native parents keep their own policy. Human/native-service/error embedding receipts support the separation. |
| FR004 | Model state, input receipt, consumed protected claim, owed outbox and commit marker participate in the existing transaction. Physical provider/marker rollback controls are distinct from host recovery evidence. `providers/model-storage`, current whole Runtime EF/provider TRXs. |
| FR005 | Original delivery remains owed until exact consumption/settlement. `pipeline/consumed-before-ack`, successful/cancelled native-parent return and pending sibling interruption retain accepted receipts before later source ACK. |
| FR006 | Actual native admission/cancellation interleavings and held retry/cancel replacement fences distinguish stale decisions from previously committed physical delivery. Obsolete service results address the independent attempt boundary. |
| FR007 | Exact scope/execution/invocation/attempt, matching/conflicting replay and authorization-before-disclosure have API/driver boundaries and actual retry-authorization evidence. Retained publication replay preserves accepted identity. |
| FR008 | Real runtime-only and paired direct/embedded partial forks retain the accepted branch through process loss, then one join/terminal continuation. |
| FR009 | Both human/duration orders and all worker-first/timer-first/held-interruption cases remain separately observable through actual restarts. Timed sources retain original deadlines and late rejection receipts. |
| FR010 | `events/model-broadcast` separately proves later registration before first selection, fixed populated/empty cohorts, actual rearm and higher-fence source retry. Signal recovery alone is not used to claim these cuts. |
| FR011 | Human, cancel, command and held-incident operations use supported product boundaries; current malformed/ineligible input handling avoids the earlier human FIFO failure. Full task administration is not claimed. |
| FR012 | Real pinned native RunEtl workers retain ETL identities, HTTP output and retry waits across direct/embedded recovery. `pipeline/direct-http-recovery`, `pipeline/embedded-native-recovery`. |
| FR013 | Success, matching/unmatched BusinessError, technical fault and ETL-origin cancellation retain truthful typed outcomes. Four embedded ETL-API-origin cancellations complement native/model-origin and reverse-result-order evidence. Actual cancellation disposition and absence of further effects are checked, rather than inferred from BPMN finality. |
| FR014 | Held service retry creates one distinguishable attempt; old failures/results remain, budget exhaustion/final fault/cancelled incidents refuse revival. Held retry/cancel FIFO and obsolete-result evidence remain separate. |
| FR015 | Root, Sequence, Flowchart and CompensationScope retain their declared native dispositions and original typed registrations. Pending parent consumption/settlement/ACK cuts are covered. Separate #3242 remains undecided. |
| FR016 | The frozen pre-writer matrix retains 104 assets/22 whole inputs and its writer map. Its manifest is byte-identical to initial capture commit6af6df2c. Actual legacy upgrades remain separate from synthetic controls. |
| FR017 | Frozen authoring/state assets, full pins/incarnations, absent optional wire and retained migration history remain preserved; current binary API comparisons support shape compatibility without deciding magnitude. |
| FR018 | Both actual saved Studio families pass the unchanged full verifier at source04c; subsequent source9c evidence covers affected editor/export/Test Run/attached Inspector behavior. Existing embedded compatibility is not promoted to first-class direct-model Studio acceptance. |
| FR019 | Complete graph/binding/profile validation refuses new unsupported placement before acceptance; retained older supported frames/material retain their readers. The timer's delegated duration validator also enforces interrupting boundaries. |
| FR020 | Actual publication retirement/GC and admission-off/runtime-only recovery preserve accepted pins and original owed workers while fresh model admission refuses. Destructive downgrade is not claimed. |
| FR021 | Definition formats1–5, runtime/state versions, XML/BPMNDI degradation, SQLite actual-host limits and separate common-provider scope are explicit in contracts/package docs/receipts. |
| FR022 | Source/identity/raw-store/failure-control boundaries remain attached to each claim. Original red probes, incomplete repairs and observer diagnostics remain negative or excluded evidence. Current-source equivalence and portable verification do not relabel historical executions. |

SC001–SC005 have the corresponding partial-fork, race/broadcast, real-pipeline, supported-operation and actual-upgrade/Studio evidence above. SC006 and the five #2898 criteria are mapped in `qualification.md`: released/frozen compatibility; fork/races; pipeline outcomes; commands/equivalent traces; declared profile/cuts. No unsupported future journey is counted toward this PR's acceptance.

The owner goal continues beyond this PR. First-class direct-model Studio, wider/upgraded-host ETL, continuous/SQL/RabbitMQ journeys, non-SQLite actual-host upgrades, distributed placement, the independent package/agent work and final program acceptance are not inferred from this witness. The recorded Orleans/task-runner/alteration/migration deferrals remain unchanged.

## Fresh verification in this lens

- **28 portable verifier invocations passed, 0 failed.** These cover the full current source04c Studio bundle; partial-fork embedding; human/service timer orders; held retry/cancel/budget and competing-command recovery; signal delivery/broadcast/pending interruption; admission-off rollback; real direct/embedded HTTP success, business error, technical fault; consumption-before-ACK and successful/cancelled parent-return cuts; all four ETL-originated native-parent cancellations; and all five inverse-cancellation cases. The commands, unabridged stdout and stderr are retained in this directory.
- **7,842 declared evidence asset references verified**, including **157 archive members**. All declared hashes checked by the audit match. The initial loose-file-only diagnostic reported the archive members as absent; direct archive-member verification resolved every candidate. That initial diagnostic remains retained and is not called a product failure.
- Eight source04c whole-suite TRXs independently reconcile to **6,928 executed Passed results, zero failed/skipped**: BPMN492, Runtime3083, Runtime EF1790, Architecture594, native providers212, standard host41, Publishing61 and Design655. These are retained executed results, not fresh .NET runs in this lens.
- The reviewed head has identical Git trees/blobs to source04c for `Essentials`, `Extensions`, relevant build/dependency contracts, `tools/bpmn`, both BPMN/ETL e2e tool directories and tier registration. `source-equivalence-current-head.json` records exact object identities. Source9c's partial frontend smoke is not used as a replacement for source04c's full restart proof.
- Four old-fault cases preserve exact original model identity, authority, pins/incarnation, interpreter snapshot and terminal Faulted result; clear owed work; consume actual retirement/native result and parent ACK as appropriate; and remain identical after another real process loss. The original pending-parent input still decodes to `0a5ea64058dcdb950fdda9e5b792264ceec2f6ca19fa9398a7733f6c62ea097e`. `old-fault-observation-audit.json` records the comparisons. This audit reads retained output; it is not another fresh host execution.
- **16 closed stores** (8 corrected old-fault Runtime stores and 8 source9c coupled stores) pass fresh immutable read-only SQLite integrity checks. Both source9c saved JSON/XML exports are byte-identical to source04c, original execution views remain stable and the actual Test Runs are Completed. The full source04c Studio verifier separately checks its twelve stores, four new runs, nine original observations and84/92 retained native-material records.
- **16 local screenshots inspected:** all twelve source04c saved-editor/publication/run/restart images and all four source9c editor/attached-Inspector images. Run Kind, exact execution identity, completed state and retained diagrams agree with their scoped receipts. Source9c attached Inspectors show the completed Delay and evaluated one-second duration.
- Product diff whitespace check passes. Whole-diff `git diff --check` returns2 for whitespace in verbatim raw DOM/diagnostic logs only; `full-diff-check.log` retains it. The original evidence is not normalized to make this check green.

No whole .NET suite was repeated, no shared build product was changed, and no new host, browser tab or external fixture was started. No SQL execution-state/queue/receipt/authority write was performed. Repository status remained clean, and no GitHub/claim/issue/PR mutation occurred. The audit scripts accept `ELSA_REVIEW_REPOSITORY` (or run from the reviewed checkout) and keep outputs beside themselves.

## Findings and delivery boundary

**Actionable findings: none. Remaining Low acceptance notes: none.**

Two examined candidates did not survive: the apparent non-interrupting-duration admission gap is refused by `BpmnDurationTimer.Duration`; the apparent missing historical evidence paths are intact declared tar members. Neither is reported as a bug. Broader operations/provider/Studio work is explicitly outside this PR's claimed completion and was not converted into a new finding.

This verdict leaves the existing owner API/version-magnitude/needs:decision, deferred GitGuardian39189012, exact required checks, protected merge and resulting-main gates open. The supplied final-head check snapshot has queued checks and skipped/neutral statuses; it is not all-green CI. T041/T043 evidence and T044's Implemented preparation are compatible with the same-implementation-PR lifecycle rule; shipped status becomes true only on merge. #2898/#2907/#2908/#2892 remain open/Partial until their respective delivery/acceptance work is actually complete.
