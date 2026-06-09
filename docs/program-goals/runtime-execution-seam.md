# Runtime Execution Seam

Status: active.

Area: Workflows Runtime architecture / executable artifact seam.

Steward(s): Joey plus the incoming runtime architect.

## Purpose

Create a focused coordination bucket for the Workflows Runtime execution seam so the incoming architect can start from the current evidence, define the runnable artifact boundary, and move into Speckit without reopening the broader Elsa Brain Operating Model work.

The goal is to specify the seam between Workflows Design and Workflows Runtime before runtime implementation begins. This bucket should keep runtime execution planning separate from operating-model grooming, CShells composition work, and broad constitution ratification.

## In Scope

- Runtime execution seam planning and Speckit preparation.
- The minimal runnable artifact currently named `WorkflowExecutable` or an approved equivalent.
- Compile/publish bridge inputs and outputs between Design-owned state and Runtime-owned runnable form.
- Runtime dependency envelope and Design-free execution-time boundary.
- Workflow execution context lifetime, DI scope, and concurrency model.
- Runtime-owned executable graph/node terminology if needed.
- Activity/workflow input-output boundary decisions where they affect runtime execution.
- Workflow-as-activity consumer/pinning questions, nested execution, and cycle guards when they become part of the runtime seam.
- Test obligations that belong to the runtime seam, including structural dependency tests and focused unit tests.

## Out Of Scope

- Broad constitution ratification unrelated to the runtime seam.
- CShells appsettings generation or feature-composition tooling.
- Runtime implementation before the seam has an approved spec/plan.
- TestContainers integration-testing policy as the first step; that should follow after the runtime seam has enough shape to test.
- Event dispatcher failure-strategy implementation, except where runtime execution explicitly depends on event semantics.

## Active Objectives

1. Use [runtime execution pre-spec handoff](../reports/runtime-execution-pre-spec-handoff.md) as the primary starting evidence.
2. Convert the handoff into an architect-owned Speckit work unit when the incoming architect is ready.
3. Keep Design reads confined to compile/publish bridge planning; execution-time Runtime must remain artifact-only.
4. Decide the runtime artifact, crossing points, context lifetime, graph terminology, and I/O boundary questions before coding.
5. Capture any approved spec, report, or implementation follow-up here instead of stretching the Elsa Brain Operating Model bucket.

## Linked Surfaces

- [Runtime execution pre-spec handoff](../reports/runtime-execution-pre-spec-handoff.md)
- [Unfinished work](../reports/unfinished-work.md)
- [Test maturity and weak implementation report](../reports/test-maturity-and-weak-implementation-report.md)
- [Activity construction seam spec](../../specs/006-activity-construction-seam/spec.md)
- [Elsa constitution](../../.specify/memory/constitution.md)
- [Framework constitution](../../.specify/memory/constitution-framework.md)
- [Skills catalog](../skills/catalog.md)

## Current Roadmap Notes

- Start with Work Unit Planner and Speckit Flow Guide from the skill catalog.
- Do not implement `WorkflowExecutionContext`, `WorkflowDefinitionActivity.Execute`, or runtime graph behavior as a drive-by change.
- Before relying on generated maps for verification, check [maps manifest](../maps/manifest.json); regenerate the relevant map if freshness matters.
- Treat the Runtime JavaScript Design reference as known deferred architecture debt, not as the first runtime-execution fix.

## Drift / Review Notes

- This bucket exists because runtime execution is now a distinct mid-term architecture effort, not merely an operating-model cleanup item.
- If the work turns primarily into integration testing, event failure strategy, or CShells composition, create or select a more specific bucket instead of broadening this one.
- If runtime seam decisions produce ratified gates, move those gates to the constitution and leave links here.
- If the result becomes a repeatable workflow, move the workflow to the skill catalog and leave links here.

## Removal or Completion Conditions

This bucket can be completed or paused when the runtime execution seam has an approved Speckit spec/plan, its follow-up implementation work is tracked in a more specific surface, or the incoming architect chooses a different coordination bucket.
