# Implementation Plan: Activity-by-activity execution playback

**Branch**: `agent/codex/4366-activity-playback-spec` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: #4366; the owner now authorizes Spec Kit planning, tasks and end-to-end implementation. This supersedes the original capture-only instruction.

**Dependency implementation authority**: The owner's subsequent instruction explicitly requests coordination with #3849 to implement T003, then continue this feature end to end. The [implementation handoff request](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6858677569) records that scope; it does not transfer the existing owner's claim or qualify an unmerged source stack. The [accepted S14a handoff](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6858916502) fixes base `f2638c8ac8f23cb3f08071db4d68b793a84d9217` and the owning paths/PR boundary. Runtime facet source authoring is underway in a separate worktree; EF/providers/retention/composition and final qualification remain with #3849.

## Summary

Add Previous/Next, Play/Pause, four presentation speeds and ordinal scrubbing to recorded run inspection. Reuse the workbench's exact occurrence selection, pinned scope resolution, permission context and conservative transition evidence. Keep playback transport separate from live observation.

**Dependent playback implementation status: blocked at foundational task T003.** The current production activity browser explicitly provides a live collection with no cross-request snapshot. It has no authoritative occurrence-entry history, frozen membership, ordinal seek or coverage/gap response. The existing history owner (#3849/spec1211) owns those missing foundations. A timestamp-sorted list, forged cursor, client-side count fence or fixture-backed history would not implement this specification. Coordinated runtime prerequisite implementation is authorized after its source boundary is agreed; dependent playback/user-story implementation begins after that facet is delivered and qualified. See [research](research.md) and the [proposed dependency contract](contracts/playback-inspection.md).

## Technical Context

**Language/Version**: TypeScript/React in the existing Studio Workflows package; .NET10 in the runtime-owned dependency when its read facet is implemented.

**Primary Dependencies**: Existing Studio SDK/UI, React Flow/read-only designers, activity inspection/navigation, runtime history ownership and authenticated capability discovery. No new package or domain is proposed.

**Storage**: Playback state is local presentation state. Reuse the history owner's retained facts/head/coverage; do not create a second capture store or change persistence modes.

**Testing**: Vitest behavior/controller tests, isolated Playwright regressions, relevant runtime API/provider suites for the prerequisite, and owned composed-host journeys with independent visual review. Existing-code corrections earn causal red/green controls. Foundation uses risk-based tests (framework §2.21/2.23); no TDD cadence is declared or inferred for new UI code.

**Target Platform**: Composed Studio/Workbench in Chromium, desktop and320px, light/dark, keyboard/pointer/reduced motion.

**Project Type**: Existing web-app feature with an explicitly blocking runtime-history dependency.

**Performance Goals**: Bounded read payloads and correct stable navigation through200 occurrences; no whole-history browser download before seeking. Presentation cadence is verified with a controlled browser clock, not a performance benchmark.

**Constraints**: Exact execution identity, authenticated context retirement, signed server-owned continuations, truthful capture/retention coverage, recorded order without fabricated causation, no runtime mutation, and no independent takeover of #3849's active capture/provider/read slices.

**Scale/Scope**: Four mandatory control groups and27FRs; four composed journeys × four existing projects =16 planned executions. New playback journeys use320px in narrow projects without changing inherited journeys. Dependency implementation remains with its owning source/slice and needs its own coherent tests/review.

## Constitution Check

Loaded framework v6.1.0 and Elsa v6.5.0, both Gate indexes and `docs/contributing/review-checklist.md`. Before/after design: no new domain, project, dependency, persistence schema, runtime-to-design reference, public API implementation or constitutional amendment is admitted by these documents. A future public history-read addition must stay in its owner's layer, retain existing constructors/contracts, update its API baseline/catalog, and earn applicable provider/composition checks. Studio controls remain private and use public controls/design tokens.

The design preserves artifact-only execution, independent inspection/value permissions, Balanced/default capture policy and existing exact selection. It relies on no draft/provisional constitutional rule. Constitution design check passes; **dependency readiness fails**, so the implementation gate stays closed. This is a real prerequisite, not a request to waive a checklist or accept a reduced feature.

## Project Structure

### Documentation (this feature)

`spec.md`, `research.md`, `data-model.md`, `contracts/playback-inspection.md`, `quickstart.md`, `tasks.md`, and the preserved `design/` concept.

### Source Code (planned, repository root)

- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityPlaybackControls.tsx`: accessible transport/scrubber; no live-update control reuse.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useActivityPlayback.ts`: playback state, timer and generation ownership at workbench lifetime.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/api/activityPlayback.ts`: capability-discovered adapter to the qualified owner read facet; no hardcoded endpoint or forged cursor.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx`: thin exact-selection/manual-navigation integration; coordinate with spec1211 S06/S07.
- Existing `workflowAccessibility`, `executionScope`, `executionMotion` and runtime value components: preserve their identity, focus and conservative evidence boundaries.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/`: pure/controller, adapter and mounted workbench behavior tests.
- `Studio/tests/browser/activity-playback.spec.ts`: isolated control/focus/rapid-response regressions, explicitly fixture evidence.
- `Studio/tests/acceptance/activityPlaybackJourney.ts` plus existing `journeys.spec.ts` and `tools/ux-acceptance/run.py`: real-host evidence.

The runtime-history owner determines the concrete read facet/provider paths after its capture/head/query prerequisites are ready. `contracts/playback-inspection.md` is a proposed dependency boundary, not a shipped endpoint or permission to duplicate that infrastructure. No generated maps or new solution filters are needed for the planned private UI files.

## Delivery Strategy and Acceptance Record

1. Agree and qualify the runtime-owned dependency with #3849 (its S02–S05, S11–S14 foundations and S06/S07 UI ownership). Do not reinterpret a source-clean or queued PR as delivered capability.
2. Add a private sequence adapter/controller and controls; confirm each selection through existing cancellable exact ancestry resolution before scheduling another tick.
3. Qualify US1 manual traversal, US2 transport, US3 seeking and US4 trustworthy state/access behavior, preserving the same cursor.
4. Complete all composed proof, independent source/UX review, required current-head checks, normal merge and resulting-main verification before closing #4366.

**User goal/reference**: Walk a recorded run interactively without leaving Timeline; use the existing inspector/shared UI and retained illustrative concept. The concept does not establish runtime support.

**States**: Unavailable/denied/empty/one-step/populated/paused/playing/buffering/ended/gap/retry; repeated and nested occurrences; concurrent branches; live appends; missing diagram; context/access loss; explicit Activity inspection. No runtime re-execution or event-level lifecycle replay.

**Coverage**: [quickstart.md](quickstart.md) defines the16 real-host cases and supporting controls. Real ordering, coverage and gap facts must come from actual owner records. A backend clock witness creates an actual52-minute recorded wait; browser clock mocks qualify cadence only. A held-retry seed must use the actual public alteration path. No acceptance is currently claimed.

## Complexity Tracking

No constitution exception requested. The dependency is intentionally not replaced by a second journal/snapshot capture system. Moving only the controls ahead of that dependency would produce a misleading or universally unavailable feature and does not satisfy the authorized end-to-end goal.
