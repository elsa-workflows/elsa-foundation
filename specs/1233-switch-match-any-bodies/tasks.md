# Tasks: Switch MatchAny case activities

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [contract](contracts/switch.md), [validation](quickstart.md).

**Prerequisite**: Final coordinator agreement, now recorded on the PR. The coordinator explicitly directed implementation in this same PR, overriding the earlier separate-branch/proposal-merge sequencing for S-12. Final initiative integration/review/merge gates remain.

**Tests**: Existing projects and risk-based behavior tests; no new registration test for unchanged plain wiring. There is no new greenfield application or declared TDD cadence. Test placement below follows dependencies; meaningful temporary mutations must prove the risky behavior is detected. No test asserts on documentation/source text.

## Phase 1: Setup

- [X] T001 Record approval of `specs/1233-switch-match-any-bodies/spec.md` at `0c058411ed`, refresh current initiative `18dd3aefb9`, and continue in the existing isolated worktree/PR as explicitly directed by the coordinator.
- [X] T002 Re-read `docs/contributing/initiatives.md`, `docs/contributing/studio-ux-acceptance.md` and `AGENTS.md` in that checkout; preserve the separate S-2/main-sync and other stream ownership.
- [X] T003 Classify the construction/descriptor/version impact in `Essentials/src/Activities/ControlFlow/Switch/Activities/Switch.cs` and the owning project's API baseline policy (it has no PublicAPI files); record any Major constructor break and affected activity contract/hash handling before committing runtime changes.

## Phase 2: Foundations

- [X] T004 Define selected-case/body access in `Essentials/src/Activities/ControlFlow/Switch/Internal/SwitchNavigator.cs`, retaining typed pinned-input, label, mode, schema and slot-reference guards; use existing structures without duplicate mutable selection state.
- [X] T005 Implement MatchAny case/case Unordered versus case/Default MutuallyExclusive facts in `Essentials/src/Activities/ControlFlow/Switch/Internal/SwitchStructureHandler.cs`; keep conservative result availability and normal completion-after-children facts.
- [ ] T006 Prove concurrency/availability behavior in `Essentials/tests/Activities/ControlFlow/Tests/ChildTimeRelationTests.cs` and the real analyzer integration in `ChildTimeRelationTests.cs`, including shared-variable warning and MatchFirst/Default negative controls.

## Phase 3: US1 — Run every matching body (P1)

**Independent test**: Two selected bodies start once, a false body never starts, and a bookmarked selected body prevents successor execution until resumed.

- [X] T007 [US1] Extend `Essentials/src/Activities/ControlFlow/Switch/Activities/Switch.cs` to schedule selected populated bodies in case order with independent branch provenance; use the existing parent-scoped state-store contract to wait for every selected body and emit the full ordered label selection once.
- [X] T008 [US1] Remove the blanket populated-MatchAny refusal in `Essentials/src/Activities/ControlFlow/Switch/Internal/SwitchStructureHandler.cs` and `SwitchNavigator.cs`; retain structural guards/remapping and add FR-012a Done-label rejection for this new body-containing shape in `SwitchCaseRules.cs` plus runtime backstop.
- [ ] T009 [US1] Replace the intentional body-refusal test and add two-match, false-body, mixed-empty, all-empty, zero-case and Default-present/absent behavior in `Essentials/tests/Activities/ControlFlow/Tests/Switch/SwitchMatchAnyTests.cs`; assert exact starts, selections and completion counts.
- [ ] T010 [US1] Extend `Essentials/tests/Activities/ControlFlow/Tests/Switch/SwitchConditionRuntimeTests.cs` to prove all conditions materialize before any body, a late condition failure starts none, pinned selection survives child variable writes, and MatchFirst still short circuits.
- [ ] T011 [US1] Extend `Essentials/tests/Workflows/Publishing/Api/Tests/OutcomeSelectionPublicationTests.cs` and `Essentials/tests/Activities/ControlFlow/Tests/Switch/SwitchStructureHandlerTests.cs` to publish/remap valid bodies and reject duplicate identities/labels, Done in the new body-containing MatchAny shape, unknown mode, malformed references and invalid conditions; retain existing-mode controls.

## Phase 4: US2 — Waits, control exits and recovery (P1)

**Independent test**: Reverse-order resumes and persisted runtime reconstruction yield one completion, no repeated committed effect and no condition reevaluation; interruption cannot route normal success.

- [X] T012 [US2] Implement immediate Break plus atomic live-child subtree cancellation in `Essentials/src/Activities/ControlFlow/Switch/Activities/Switch.cs`; preserve existing child-fault blocking policy, no reschedule/rematerialization opt-in, Finish and enclosing cancellation behavior.
- [ ] T013 [US2] Add two independent bookmarks, reverse resume and one-completed/one-waiting assertions in `Essentials/tests/Activities/ControlFlow/Tests/Switch/SwitchMatchAnyTests.cs`, proving successor execution remains zero before the last selected success.
- [ ] T014 [US2] Exercise duplicate/out-of-order callbacks, foreign-parent records, nested descendants, unselected/Default rows and cancelled/faulted/superseded non-success controls in `Essentials/tests/Activities/ControlFlow/Tests/Switch/SwitchActivityTests.cs`; use real parent-handler/harness behavior where deduplication is runtime-owned.
- [ ] T015 [US2] Add persisted restart/failpoint coverage in `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/SwitchRecoveryEfTests.cs` using the existing EF test harness; reconstruct after child commit/before parent handling and prove immutable selection, no repeated child effects and exactly one final completion.
- [ ] T016 [US2] Add Break with a live bookmarked sibling and a queued sibling, enclosing loop propagation, workflow Finish, and parent/workflow cancellation cases in `Essentials/tests/Activities/ControlFlow/Tests/Switch/SwitchRuntimeTests.cs`; assert bookmark cleanup and suppressed queued starts.
- [ ] T017 [US2] Prove a faulted body plus successful sibling retains the child blocking incident and no successful Switch routing in `Essentials/tests/Activities/ControlFlow/Tests/Switch/SwitchRuntimeTests.cs`; explicitly verify the existing processed-fault child reschedule refusal rather than silently opting Switch into a new recovery policy.

## Phase 5: US3 — Faithful Elsa 3 conversion (P1)

**Independent test**: Import a nested two-body MatchAny Switch, publish, run and observe both selected effects before one Done successor; deliberately unsupported constructs still refuse.

- [X] T018 [US3] Replace the blanket body refusal with recursive conversion in `Extensions/Elsa3/src/Mapping/ActivityMap/Rewrites/Elsa3SwitchValueSynthesisRewrite.cs`; preserve ItemId, ConditionProperty, paths/order/mode/Default. Implement FR-015a effective boundary-outcome validation in `Extensions/Elsa3/src/Mapping/Conversion/Elsa3SwitchSafety.cs` after normalization and script lowering: refuse reachable or unproven native Break via activity, literal/computed script selection, container propagation or nested labels, retaining proved-safe declared ports and supported case-local consuming loops.
- [ ] T019 [US3] Update `Extensions/Elsa3/tests/Mapping/Tests/Elsa3NodeRewriteTests.cs` with body/identity/condition preservation and existing refusal controls; assert localized UnsupportedMode for effective outward Break (including computed Break in PossibleOutcomes, nested labels, outer-loop and aliases), plus owning Break/Done labels. Implement FR-015b own-label guard in `Elsa3SwitchSafety.cs`; keep ordinary scripts/nested/owning labels and declared-but-unreachable Break convertible.
- [ ] T020 [US3] Extend `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/ExplicitOutcomesRunTests.cs` with nested-body import→publish→run fixtures for multiple matches, mixed empty cases, no match with/without Default, a waiting body and Done successor timing.
- [ ] T021 [US3] In `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/ExplicitOutcomesRunTests.cs`, prove shared-target default join and dead-path safety; activity/script/nested-label Break refusal; owning Break/Done-label refusal with Sequence successor/ordinary-label control; published unreachable-port/case-local-loop positives with live sibling waits; and Finish, MatchFirst and outcome-only FlowSwitch controls.
- [X] T022 [US3] Reconcile guidance in `Extensions/Elsa3/src/Mapping/README.md` and `Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs`; remove only the obsolete MatchAny-body example and retain the shared UnsupportedMode code and every remaining refusal reason.

## Phase 6: US4 — Studio authoring (P2)

**Independent test**: On the real composed host, author/save/reopen/publish/run two true populated cases in both modes, with mode-accurate help and preserved content.

- [X] T023 [US4] Add a labelled shared-control mode choice and accurate mode-specific help to `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/CollectionSlotEditor.tsx` and its `InspectorPanel.tsx` host, including an informative read-only presentation.
- [X] T024 [US4] Preserve mode, unknown payload members, case identities, conditions, bodies and wires through ordinary mutations in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/collectionSlots.ts`; keep existing outcome-port/navigation rules in `switchOutcomePorts.ts`.
- [ ] T025 [US4] Extend `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/switchConditions.test.tsx` and `collectionSlots.test.tsx` for default/MatchAny choice, read-only enforcement, keyboard selection, undo/redo and reorder/rename/body preservation.
- [X] T026 [US4] Extend `Studio/tests/browser/switch-conditions.spec.ts` and `switch-case-outcome-ports.spec.ts` for mode/help, save/reload, focus, validation recovery, nested navigation and retained wires, identifying isolated fixture evidence accurately.
- [ ] T027 [US4] Add `Studio/tests/acceptance/switchMatchAnyJourney.ts` and register it in `Studio/tests/acceptance/journeys.spec.ts`; use owned real hosts/stores to prove author→save→reopen→publish→run, both modes, no-match/Default, validation repair and read-only states without Code view.
- [X] T028 [US4] Register finite desktop/narrow and light/dark journeys/captures in `tools/ux-acceptance/run.py`; retain source/head and every intended case, screenshot, ARIA snapshot, trace and failure/skip/retry, with no silent acceptance omissions.
- [ ] T029 [US4] Run the affected Studio suites and composed journey from the committed implementation tree per `specs/1233-switch-match-any-bodies/quickstart.md`; have an independent acceptance reviewer open downloadable screenshots/traces, fix all verified usability findings and rerun the affected whole journeys.

## Phase 7: Documentation, validation and review convergence

- [X] T030 Reconcile `Essentials/src/Activities/ControlFlow/Switch/README.md`, `Switch/EXTENSION_POINTS.md` and the relevant `specs/1208-explicit-outcomes/` restriction pointers with implemented selection, child concurrency, pinned input, lifecycle and conversion behavior; retain historical scope and the separate join boundary.
- [ ] T031 Run affected local checks and representative temporary mutations from `specs/1233-switch-match-any-bodies/quickstart.md`, then restore/retest the source; record exact commands, head, counts and failed/not-run platform states on the implementation PR without waiting for CI.
- [ ] T032 Once all implementation tasks/acceptance are complete, set `specs/1233-switch-match-any-bodies/spec.md` to Implemented and remove its terminal `checklists/requirements.md` residue in the implementation PR; keep final artifacts, evidence and PublicAPI changes consistent before final independent review.
- [ ] T033 Run independent correctness, standards and acceptance review against the complete implementation diff and PR discussions, using `specs/1233-switch-match-any-bodies/spec.md` as the acceptance source. Verify/fix every surviving finding, including minor ones, in new commits without force-push and repeat until a fresh exact-head round has no remaining verified findings.
- [ ] T034 Post the exact reviewed head, local/negative/UX evidence, review-round links, explicit CI-deferred status and remaining formal approval/merge gates on the implementation PR; reconcile title/body with `specs/1233-switch-match-any-bodies/spec.md` and request the required review.
- [ ] T035 Post a factual implementation handoff on the initiative and have the coordinator update only its S-12 board row after re-reading the latest board, using `specs/1233-switch-match-any-bodies/spec.md` and PR/merge evidence; preserve all other streams.

## Dependencies and parallel opportunities

Setup and foundations precede runtime. US1 supplies US2 and US3's executable behavior. US4 can prepare its distinct UI files once the approved contract is fixed, but its composed execution proof waits for US1/US3. All stories precede final status/review/evidence. Any commit after a clean review requires a fresh exact-head review; T033 includes fixes and refreshed evidence until convergence.

Independent file work may proceed in parallel only when it does not share a mutation target: publication-fact tests and converter fixtures; Studio unit tests and EF recovery fixture preparation; documentation and a stable affected-project run. Shared Switch runtime edits remain sequential. The build-slot queue still governs all .NET builds; these opportunities do not authorize concurrent solution builds.

## Implementation strategy

US1 is the smallest functional increment, but S-12 delivery includes all four stories. Continue implementation in this same PR as explicitly directed by the coordinator. No acceptance reduction follows from an MVP checkpoint. The authorized review convergence has no default numeric stop; CI is deferred, not disabled or claimed passed.
