# Implementation Plan: Workflow Incident Troubleshooting

**Branch**: `1308-incident-troubleshooting` | **Date**: 2026-10-02 | **Spec**: [spec.md](spec.md)

## Summary

Preserve causal evidence when an activity input fails before the activity-start checkpoint, then expose incident health and direct troubleshooting navigation in Foundation Studio. Keep WaitForIntervention and existing lifecycle/retry behavior. Deliver additive runtime/API contracts and shared Studio presentation, followed by rebuilt real-app acceptance and gated PR delivery.

## Technical Context

- Backend: C#/.NET 10, runtime Core/Services/API, in-memory and EF Core persistence; reuse current poison metadata and input inspection evidence where possible.
- Frontend: TypeScript/React, pnpm/Vite, XYFlow and semantic `--studio-*` tokens; Vitest/testing-library regression suites.
- Target: normally composed Workbench and Studio web host; no production deployment.
- Persistence: additive compatible evidence; old records remain readable. Never commit partial successful input values to explain a failed attempt.
- Constraints: permission/tenant scope, stable cursor paging, exact activity execution association, pinned executed graph, cancellation and durable checkpoint invariants. No runtime dependency on Design.
- Scope: run list/header, Flowchart/Sequence/runtime BPMN mapping, nested/repeated activity inspection, input evidence and incident summary/navigation. No editor intelligence or recovery-policy redesign.
- Performance measurements remain retired by ADR 0073. Use bounded server paging and existing store contracts; no N+1 parallel reads against a shared EF context.

## Constitution Check

Pre-design and post-design checks pass in scope: Runtime stays independent of Design (§E2.2); inspection uses pinned executable evidence (§E2.6); existing behavior and tests remain (§2.21.1); tests cover new decision branches and composition (§2.23); changed feature/API behavior receives discoverable docs (§2.22). No new dependency, anonymous dispatch, substitute persistence family or governance exception is planned. Provisional Design mutation/Model X sections do not govern this change. New public shapes are additive and preserve wire identifiers.

Required delivery gates: affected backend/API/provider suites, relevant REST e2e against rebuilt fresh-DB host, affected frontend tests/typecheck/lint/build, architecture guard, authoritative generated maps check, root diff review, independent review, targeted revert/mutation bite proof, exact-head required CI and post-merge main CI/Maps. Existing unrelated main failure #2293 stays visible and is not declared fixed by this program.

## Project Structure

Canonical artifacts: `specs/191-incident-troubleshooting/{spec,plan,research,data-model,quickstart,tasks}.md`, `contracts/incident-troubleshooting.md`; program `docs/program-goals/incident-troubleshooting.md`; evidence `docs/reports/incident-troubleshooting/`.

Backend code: `src/essentials/Workflows/Runtime/{Core,Services,Api,Persistence/EntityFrameworkCore}/`; tests mirror those layers under `tests/essentials/Workflows/Runtime/`; black-box resilience/inspection suites under `e2e-tests/`.

Studio code in `elsa-foundation-studio`: `src/essentials/Elsa.Studio.Workflows/Client/src/{api/runtime.ts,workflowTypes.ts,workflowAdapter.ts,inputInspectionRows.ts,workflow-editor/,bpmn/,__tests__/}`. Preserve code-editor work owned by Program #2310.

## Delivery Sequence

1. Establish causal/failure and list-health contract; claim bounded task issues and isolate writer worktrees.
2. Backend causal input failure evidence and health query proceed in independent files; Studio implements against the agreed additive contract.
3. Root reviews each diff, integrates backend changes, runs scoped checks through the build-slot wrapper, and reconciles any contract mismatch.
4. Root rebuilds isolated normally composed hosts with fresh database and unused loopback ports, repeats the exact browser scenario and control matrix, captures after screenshots.
5. Independent reviewers assess code and acceptance evidence. Fix findings and prove the tests fail when the targeted behavior is reverted.
6. Push/open PRs using the user-selected Git route; attach each PR, publish concrete gate evidence, merge only green, verify main and close task/feature/epic/program in order.

## Complexity Tracking

No planned constitution violations. Two implementation repositories are required by the existing backend/Studio boundary; canonical product specification stays in Foundation and Studio links to it.
