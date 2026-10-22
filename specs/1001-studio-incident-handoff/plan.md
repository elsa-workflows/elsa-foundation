# Implementation Plan: Incident investigation handoff

**Branch**: `agent/codex/3067-incident-handoff` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: #3067 / #3057 / #2559; base `5d1df7097db3a731330735a94c2b6a5fb7f9a476`, qualified through Core39683644467 attempt2, Studio39683644232 and Maps. [Selection](https://github.com/elsa-workflows/elsa-foundation/issues/3057#issuecomment-6329077033); winning App claim6329077955. Home3063 is not a dependency and remains stopped for its requeue requirement.

## Summary

Add concise selected-incident/current-run next-step guidance to the existing investigation composition. Reuse the canonical compensation controls, exact activity/input navigation and Runs return context. Preserve honest unknown/partial/denied evidence, scheduled retry and pending/canceled guards, separate runtime/design authority and pinned-executable semantics. No recovery API or second action implementation.

## Technical Context

**Language/Version**: Existing TypeScript/React Studio pnpm workspace.
**Primary Dependencies**: Existing Workflows, public studio-sdk/studio-ui, local incident/compensation/operator-action modules; no new dependency.
**Storage**: Existing run/incident/journal read projections; no new persisted state.
**Testing**: Vitest/Testing Library observable component/hook tests, Playwright incident fixtures, rebuilt disposable normal Workbench/Studio blocked-run and read-only proof.
**Target Platform**: Current normal Studio browser composition; runtime-only/denied Design remains usable for investigation.
**Project Type**: Brownfield UI behavior, not a new feature module or greenfield application.
**Performance Goals**: No benchmark or invented response-time target.
**Constraints**: Backend remains authoritative. No runtime/design mutation from explanatory inspection, no new recovery command, broad permission-policy change or optional module/service setup.
**Scale/Scope**: Existing Workflows incident/run UI plus focused local guidance/availability helpers, tests and current UX/incident guidance.

## Constitution Check

Root loaded framework6.1.0 and Elsa6.1.0 in full, both Gate indexes and docs/contributing/review-checklist.md for this design. Re-evaluated after the decisions below; no exception.

- §2.6/§2.23.4: use current public module boundaries and existing backend/read projections; no dependency on Dashboard/host internals, no recreated runtime side effect.
- §2.17: keep actual repeated guidance/setup in focused Workflows-owned helpers; no global framework or transitive dependency to share a handful of lines.
- §2.21.1/§2.23.2: preserve valid operator/incident tests; prove decisions, selection/availability races, denied/partial evidence and navigation through observable behavior. Capture meaningful existing-UI RED before implementation and final revert/mutation bite-proof. No source/markdown-text tests. Brownfield cadence is not a new greenfield-owner choice.
- §2.22/§2.23.3/§4.2 and Elsa§E5: no feature registration, C# public API, package/version or dependency change; no event/catalog update needed. Module-internal optional props/helpers must preserve existing consumers.
- Elsa§E2.2.3/§E2.6: Design is not needed to investigate Runtime; definition editing affects future runs, not the current executable. No deployment or executable semantics change.
- Required architecture restore/test, listed/executed reconciliation and generated solution-filter check remain root integration gates. Do not restore deleted documentation maps or edit the unrelated AGENTS pointer. Draft/provisional constitution sections impose no new approval gate here.

## Design and Implementation

1. **Guidance composition**: a short clear next-step block in the existing selected-incident investigation/details surface, before advanced technical detail. Reuse current semantic tokens, spacing, buttons/links/disclosure and focus conventions; retain copy/evidence controls. Avoid a new page, recovery wizard or generic UI framework.
2. **Selected context**: derive from the exact selected incident, current run/lifecycle, existing activity association, canonical applicable-action state and available compensation evidence. Recompute for selection/run/evidence changes. Missing context remains explicit, including standalone WorkflowIncidentList consumers.
3. **Known versus unknown**: current useOperatorActions.canAct is run canManage plus linksAvailable, not an applicable-incident decision. Its boolean capability result currently conflates loading, failed discovery and absent support. Preserve canonical guards; expose a narrow read-only availability distinction only as needed for honest copy. Do not label unknown as denied/unsupported, and cancel/ignore out-of-order observations across endpoint/run identity.
4. **Recovery**: reuse isWaitingForIntervention, existing canceled-requester/pending-follow guards, canonical action rendering and confirmations. Pending action for another incident in the same run must not be described as this target's action. Reuse existing compensationRuntime/journals to recognize RetryScheduled with retryNotBefore; an elapsed due time does not create a manual action. NotLoaded or partial paging is not absence. No new recovery endpoint/action or mutation branch.
5. **Authority/handoff**: Runtime details' canManage/canInspectValues are not Design authority. Use existing scoped session/permission/capability contracts for appropriate exact-definition navigation; preserve server wildcard/resource-subtree/verb/manage-to-read/case semantics and auth-off posture if interpreting raw grants. An absent exact client key alone is not denial. No Dashboard-private import or global SDK authorization rewrite. Unknown/unavailable Design can fall back to existing runtime evidence, copying and an author/operator handoff. Do not promise editing/publishing to an inspection-only caller.
6. **Pinned identity/evidence**: retain the exact definition ID, affected activity occurrence/input focus, protected/unavailable/payload boundaries and original list query/page/returnTo contract. Say explicitly that editing/publishing affects future executions, not the run's pinned executable. Do not claim value retrieval from a navigation/loading screenshot or silently choose a different occurrence.
7. **Guidance docs**: update relevant entries in Studio/docs/ux/design-spec.md and Studio/docs/workflow-run-incidents.md with the delivered next-step and retained domain exceptions. Do not replace canonical runtime recovery docs or claim final3075 acceptance.

## Project Structure

Feature artifacts: specs/1001-studio-incident-handoff/{spec,plan,research,data-model,quickstart,tasks}.md, contracts/incident-handoff.md and a temporary requirements checklist.

Expected production boundary: Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx; focused local guidance component/deriver if useful; useOperatorActions.tsx and compensationRuntime.ts only for necessary read-only availability/association reuse; Workflows styles.css for the changed composition only. Preserve API/action mutation modules unless root explicitly reconciles a discovered contract problem.

Tests: existing Client/src/__tests__/workflowInstances.test.tsx, operatorActionFlow.test.tsx, compensationJournal.test.ts and focused siblings sharing fixtures; Studio/tests/browser/incident-diagnostics.spec.ts and incidentDiagnosticsFixture.tsx, with existing compensation fixtures as appropriate. No new module/project/dependency.

## Validation and Integration

Cost order: focused existing-UI failing regression → narrow guidance/availability/identity matrix and root diff review → complete affected Workflows tests/typecheck, Studio lint, affected UI/host builds → three independent non-author exact-head axes → final current-head normal-host and mutation/revert proof → canonical PR and resulting-main checks. Serialize heavy builds/testhosts through current slots, cap client workers at2, inspect shared load for timing failures. Do not rerun unchanged expensive gates after a cheap deterministic failure.

Normal proof uses private disposable identity/config/database/data and owned processes, normal browser sign-in, source/build/served-asset binding and verified cleanup. Show a real blocked-run handoff, exact activity/input identity and original paged/filtered list return; show an actual read-only caller's guidance with zero unintended run/recovery/design mutation. Where exercising an available canonical action, inspect its target/consequence and Cancel; retain the upstream established confirmed-action evidence rather than invent a new action path. The fixture matrix owns scheduled/history/partial/denied/capability-failure cases; state exactly which ran live. Loaded value retrieval is a separate claim requiring its own grants/capability proof.

No backend design/publishing/runtime semantics are intended to change, so existing backend workflow-flow/recovery e2e is not rerun solely for text/navigation. If implementation changes those semantics or the canonical mutation mapping, stop and refine the scope/required rebuilt-server e2e before continuing. Preserve existing M4/M5 evidence and regression suites.

Before merge: exact-head clean three-axis review, affected tests/lint/builds, architecture/maps, normal-host proof, required/relevant CI green, no human hold/unresolved adopted findings and standing authorization audit. Any failed required base gate stops implementation until reconciled. After merge: resulting-main qualification, issue/Project acceptance and specific claim release. Future gates remain public evidence, not pre-checked task claims. Final3075 remains separate.
