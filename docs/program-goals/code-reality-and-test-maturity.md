# Code Reality And Test Maturity

Status: active.

Area: codebase verification / tests / weak implementations.

Steward(s): Joey plus active engineers/agents.

## Purpose

Track the hard code and test reality work that remains after the Elsa foundation workspace operating model became usable.

This bucket exists so weak implementations, missing tests, TestContainers policy, event failure-strategy support, and verification follow-ups are not hidden inside broad operating-model reports.

## In Scope

- Codebase verification against constitution gates.
- Test maturity work, including registration-test coverage and shared infrastructure tests.
- TestContainers-based integration-testing policy/work units.
- Event dispatcher failure policy and subscriber failure classification implementation planning.
- Classification and follow-up for weak, stub, placeholder, or `NotImplementedException` surfaces.
- Map refresh or map expansion when needed for code/test verification.

## Out Of Scope

- Runtime execution seam design itself; use [Runtime Execution Seam](runtime-execution-seam.md).
- Broad launch onboarding; use [Workspace Launch Readiness](workspace-launch-readiness.md).
- Broad constitution ratification; use [Constitution Readiness](constitution-readiness.md).
- CShells generator readiness; use [Feature Composition Readiness](feature-composition-readiness.md).

## Active Objectives

1. Convert selected findings from test maturity, weak implementation, and unfinished-work reports into focused work units.
2. Define TestContainers-based integration testing policy only when a concrete verification target needs it.
3. Plan event dispatcher failure policy and subscriber failure classification support before treating event skills as fully executable in code.
4. Keep verification report-first: inspect gates, maps, source, tests, and catalogs before proposing code changes.

## Linked Surfaces

- [Test maturity and weak implementation report](../reports/test-maturity-and-weak-implementation-report.md)
- [NotImplemented classification](../reports/notimplemented-classification.md)
- [Unfinished work](../reports/unfinished-work.md)
- [Maps index](../maps/README.md)
- [Skills catalog](../skills/catalog.md)
- [Framework constitution](../../.specify/memory/constitution-framework.md)
- [Elsa constitution](../../.specify/memory/constitution.md)

## Current Roadmap Notes

- Start from a selected verification target, not from a broad "fix tests" pass.
- **Activity contract reality (2026-08).** The Elsa 4 activity library now has a contract-surface snapshot guard and
  an in-process behavioural drive that measures declared outcomes, outputs and required inputs against what the
  engine actually commits ([report](../reports/elsa-4-activity-behavioural-drive-2026-08.md), issue #1119, PR #1123).
  **All 28 activities are now covered** — #1124 closed the last two (`DispatchWorkflow`, `GraphActivity`), so
  `UndrivenCoverage` is empty and its guard test is inverted to assert it stays empty. All five `DispatchWorkflow`
  outcomes are shown reachable by making a real child complete, fault, be cancelled, and become unstartable; a REST
  suite (`e2e-tests/composition/Test-DispatchWorkflowOutcomes.ps1`) drives the three that REST can reach. Open
  follow-up from that unit: [#1127](https://github.com/elsa-workflows/elsa-foundation/issues/1127) — a workflow
  root activity's completion outcome is recorded durably but dropped from the inspection projection after a resume.
- Runtime-specific execution decisions should come from the Runtime Execution Seam bucket first.
- Integration testing policy should be grounded in actual connected-feature verification needs.

## Drift / Review Notes

- This bucket is where the repo confronts code reality after operating-model readiness.
- If a finding is only evidence and not planned work, keep it in reports rather than moving it here.

## Removal or Completion Conditions

Complete or pause this bucket when the high-value verification/code-reality findings have been routed to focused work units, completed, or intentionally deferred with links to evidence.
