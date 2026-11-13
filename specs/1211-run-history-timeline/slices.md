# Delivery slices and resumption ledger

Parent: [#3849](https://github.com/elsa-workflows/elsa-foundation/issues/3849). Status: **In progress**. This ledger records implementation units; it does not reduce the [specification](spec.md) or turn supporting slices into delivery acceptance.

## Owner decisions

- [5 November](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6645438505): end-to-end delivery, including qualification, review, merge and resulting-main verification.
- [9 November](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6739905865): close the approximately 40k-line #3850 without prejudice; preserve its branch/evidence and resubmit roughly 3k-line stacked PRs, one spec slice per PR in task order.
- [11 November](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6794241516): Inputs lands first; Timeline's Data tab and history API build on #4108 rather than the closed #3881 branch.
- [12 November](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6837576668): resume remaining work after reconciling what landed.

These decisions authorize multiple PRs for this one feature, superseding the old single-PR plan. Use `Part of #3849` without an automatic closing keyword on every slice, including the final delivery PR. Explicitly close the parent only after resulting-main verification. The original valid claim remains on #3849. No new program, deployment or release is authorized.

## Verified baseline

Current main at resumption is `dff7c490d0e5501b22d4ba89beb389c956d4f423`. #3850 is **closed, not merged**. Spec1211 and its new production history types are absent from that main. The old implementation and local corrections remain preserved separately at `a3417044b0c476dceb6488c5deb6032ef1a82c99`; they are donor material, not a branch to merge wholesale.

| Landed work | How the remaining slices use it |
|---|---|
| Compact Inputs: [#4108](https://github.com/elsa-workflows/elsa-foundation/pull/4108), `cf517c54db`; composed 320px follow-up [#4174](https://github.com/elsa-workflows/elsa-foundation/pull/4174), `85f83c5691` | Preserve the value-first rows, Technical details action, accessible interactions and tests. Add the activity Data tab around this implementation. |
| Run-canvas first-view visibility: [#4187](https://github.com/elsa-workflows/elsa-foundation/pull/4187), `dd34ebec95`, and [#4197](https://github.com/elsa-workflows/elsa-foundation/pull/4197), `cb0978d6c4` | Keep the delivered canvas/MiniMap behavior when integrating Timeline/Diagram selection. |
| Decisions composition inventory: [#3947](https://github.com/elsa-workflows/elsa-foundation/pull/3947), `f588376231` | Reuse the current inventory; do not replay the prototype's former missing-entry fix. |
| Plain checkpoint marker reads: [#4166](https://github.com/elsa-workflows/elsa-foundation/pull/4166), `ae248a69a6` | Preserve the normal successful-write path; history-free commits must not regain unconditional marker reads. |
| New-run activity lookup probes: [#4030](https://github.com/elsa-workflows/elsa-foundation/pull/4030), `1769c5b5a8` | Preserve current read-selection/cache ownership and bounded work when adding history queries. |

The initial resumption check found an open historical `main is red: CI` tracker, #4177, and a still-running required shard on the base. Its required Build & test later passed, as recorded in [S01's review receipt](https://github.com/elsa-workflows/elsa-foundation/pull/4227#issuecomment-6837882481). The open historical tracker is not silently closed. Recheck actual required results before depending on them or merging; do not count unrelated skips as passes.

Adjacent open Studio work: [#4223](https://github.com/elsa-workflows/elsa-foundation/pull/4223), head `d2ce73eca248f22975fa64a6965081b02b0d49fb`, keeps a timeline row's selection/focus in Timeline until Activity is explicitly opened. It was not landed at this check. Reconcile its eventual source/tests when implementing S06/S07; its author-reported evidence is not acceptance of #3849.

## Task-ordered review units

Each row names a coherent boundary, not permission to include every donor file matching a prefix. Keep handwritten changes and their behavior tests together, aim for roughly 3k changed lines per PR, and split further when needed. Read the current source before porting; preserve unrelated landed changes. The API/Studio supporting slices may precede durable providers in task order, but must disclose unsupported recording and may not enable an incomplete production path.

| Slice | Tasks | Review boundary and required proof | Status |
|---|---|---|---|
| S01 | T001–T003 | Adopt the selected spec/reference, design contracts and this delivery map. Validate documentation and independent boundary review; no runtime/Studio implementation is enabled. | [#4227](https://github.com/elsa-workflows/elsa-foundation/pull/4227): source review clean R3; required PR checks passed; protected merge queue; unmerged |
| S02 | T004 | Immutable history envelope, policy/batch/head/bookmark/query contracts and API tracking. Constructor/identity/safety behavior and compatibility tests. | [#4236](https://github.com/elsa-workflows/elsa-foundation/pull/4236): source review clean R2; draft on S01; 24 cases not run |
| S03 | T005, T007 | Optional checkpoint write sidecar, fingerprint/fold preservation and writer-reader capability contract. History-free replay and unchanged continuation separation controls. | [#4243](https://github.com/elsa-workflows/elsa-foundation/pull/4243): source review clean R1; draft on S02; 12 cases not run |
| S04a | T006, supporting T007 | Optional admission-policy/known-choice carriers, actual start/checkpoint handoff, retained-policy precedence including legacy absence, and Required-state refusal without a contribution. No production resolver/capture registration yet. | [#4245](https://github.com/elsa-workflows/elsa-foundation/pull/4245): source review clean R1; draft on S03; 14 cases not run |
| S04b | Supporting T008/T025 | Immutable actual-owner source/value inputs and source-side replay identity, with legacy factory fallback. No source implementation/capture registration yet. | [#4248](https://github.com/elsa-workflows/elsa-foundation/pull/4248): source review clean R2; draft on S04a; 20 cases not run |
| S04c | Supporting T008/T025 | Actual immutable value-owner origin/delta production and native invoke/resume/structural/fault callers; legacy/default composition remains uncaptured. No capture registration yet. | [#4263](https://github.com/elsa-workflows/elsa-foundation/pull/4263): source review clean R2; draft on S04b; 25 cases not run |
| S04d | Supporting T008/T025 | Qualified lazy native source-fact handoff, exact input equality and actual incident creation/resolution/repair owners. No capture/writer registration yet. | [#4278](https://github.com/elsa-workflows/elsa-foundation/pull/4278): source review clean R2; draft on S04c; 29 new cases not run |
| S04e | Supporting T006–T008 | Retained Required-policy preflight before actual native invocation/background admission, parent/notification callbacks and typed resume; no capture implementation installed. | [#4279](https://github.com/elsa-workflows/elsa-foundation/pull/4279): source review clean R1; draft on S04d; 22 new cases not run |
| S04f | Supporting T008/T025 | Actual invoke/parent/notification/typed-resume callback observations and native outcome/cancellation/fault source handoff. Immutable structural and bookmark request carriers; causal inputs remain optional. No capture/writer registration. | [#4282](https://github.com/elsa-workflows/elsa-foundation/pull/4282): source review clean R2; draft on S04e; 21 new cases not run |
| S04g | Supporting T008 | Deterministic unpositioned candidates from original native checkpoint inputs; pinned-policy/source qualification, final proof checks and metadata-only references. No capture registration/writer. | [#4288](https://github.com/elsa-workflows/elsa-foundation/pull/4288): source review clean R3; draft on S04f; 22 new cases not run |
| S04h | Supporting T008 | Atomic in-memory candidate allocation, semantic/physical receipts, exact scoped reader pairing and bounded projection queries. No capture/admission module registration or observation/retention owner enabled. | [#4289](https://github.com/elsa-workflows/elsa-foundation/pull/4289): R1 scope/policy/exact-content findings fixed for R2; draft on S04g; 35 new cases not run |
| S04i+ | T006–T008 | Remaining native owner facts and qualified policy composition. Qualify rollback/replay/no-record and scoped registration before enabling. | Not started |
| S05 | T009–T010 | Authorized bounded run/event read APIs and honest coverage/capability results, with real handler/authorization and unknown-kind tests. | Not started |
| S06 | T011–T014 | Primary Timeline/Diagram workspace, stable observation/filter/selection and affected Workflows/browser behavior. Preserve delivered run-canvas changes. | Not started |
| S07 | T015–T018 | Exact execution journal and Journal/Bookmarks/Data navigation over compact Inputs; preserve occurrence/attempt/descendant identity and exact links. | Not started |
| S08 | T019–T022 | Actual bookmark creation/consumption/retirement, bounded inventory and lifecycle UI. Real claim/rearm/cancel/expiry controls; passive expiry is not a recorded transition. | Not started |
| S09 | T023–T024 | Independently authorized delivery observations and actual bookmark ingress/disposition. Duplicate/unmatched/faulting delivery controls without false continuation. | Not started |
| S10 | T025–T027 | Other actual semantic owners and causal navigation. Split by timer/child/publication/recovery/compensation/value owner as needed, keeping each owner's source and tests together. | Not started |
| S11 | T028–T029 | EF projections and atomic checkpoint participant/query implementation. Real scope, rollback, ambiguous acknowledgement, fence and replay controls. | Not started |
| S12 | T031–T033 | Current generated migrations and fresh/upgrade/provider qualification. Choose provider-sized sub-slices only where each intermediate model/target/store remains consistent and its checks pass. | Not started |
| S13 | T030, T034 | Run deletion/retention, capture changes, legacy markers and all persistence modes/cadences with real replay/no-record proof. | Not started |
| S14 | T035–T040 | Coverage, bounded long-history queries/cursors, live context, independent value access and tenant/scope denial. Keep server and client changes in independently testable sub-slices. | Not started |
| S15 | T041–T050 | Current-source composed restart/diagnostic journeys, inspected visuals, independent acceptance, final catalogs/review/checks and resulting-main closeout. | Not started |

Some task dependencies cross rows: foundational contract/registration work precedes every consumer; durable model/migration/reader/writer support must be qualified together before production admission; final integration depends on every story. The detailed [task dependencies](tasks.md#dependencies-and-parallel-work) still apply. A row is complete only when all of its sub-slices and required proof are recorded.

The donor has approximately18k generated migration lines. Regenerate against the current schema rather than copy obsolete target models. Record generated and handwritten counts separately when selecting S12 boundaries. Do not cut a coherent schema transition merely to meet a line count, silently waive the owner's review-size direction, or land a known failing intermediate target; resolve an incompatible boundary before opening that slice.

## Evidence and review discipline

- Each replacement PR records its exact base/head, imported versus adapted source, applicable tasks/requirements, actual commands/results and unverified states. References to old evidence retain their original source.
- The [closed prototype's four review rounds](https://github.com/elsa-workflows/elsa-foundation/blob/6cb9fc70f8fb8129c79c9151342f4e237b1a8d5e/specs/1211-run-history-timeline/evidence/review-round-4.md) remain historical. Carry each relevant finding into its replacement slice and record the slice's own numbered independent review; do not present the old review as approval of new code.
- R4's runtime wide-value boundary and three narrow exact-target visual captures need their applicable current-source proof. The later prototype native positives and composed42 pass do not close those findings for a new head by themselves.
- The prototype's separate retained-generation `UnknownAuthority` failure is not declared fixed by a later green summary. Use the current suite/tracker evidence; do not reproduce or patch unrelated ownership code speculatively.
- Code, generated artifacts and registration changes receive meaningful behavior/whole-project checks as applicable. S01 adds no product behavior and needs no extra local .NET/browser campaign; let the actual CI selector apply every required check, including any checks selected by contract paths.
- Use current hosted CI routing and required checks. The expired November6 Azure grants and the old Mac-heavy-work restriction are not new resource authorization. No Azure worker is restarted by this plan.
- Visible changes require the existing composed-host downloadable evidence and independent actual image inspection. Reference images, old screenshots and machine test success are distinct from visual acceptance.
- Keep the parent spec In progress through partial slices. Set it to Implemented in the final qualified delivery PR; keep #3849 and the delivery goal open until resulting-main/post-merge verification also passes.

## Slice receipts

Add concise receipts here as work lands: PR, exact merge/head, tasks covered, current proof and remaining limitations. Keep full raw artifacts and detailed review discussion on the owning PR; do not import the prototype's execution diary into each slice.
