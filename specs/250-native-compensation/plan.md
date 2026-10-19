# Implementation plan: native compensation scopes (M1)

**Spec**: [spec.md](spec.md) · **Issue**: [#3044](https://github.com/elsa-workflows/elsa-foundation/issues/3044)

## Technical context

.NET 10 runtime modules under `Essentials/`. React 19/Vite Studio module under `Studio/`. No new persistence
tables and no new runtime mechanisms in M1: the scope uses the structural-activity, fault-absorption and
private-state contracts as they are.

## Placement

| Item | Location | Notes |
|---|---|---|
| Activities `CompensationScope` | `Essentials/src/Activities/Compensation/` (new project `Elsa.Activities.Compensation`) | Follows `Elsa.Activities.ControlFlow` layout: `Activities/`, `Internal/`, `Models/`, feature class |
| Structure handler, validator | same project, `Internal/` (internal types) | Design side references `Elsa.Workflows.Design.Core`, like Switch |
| Inspection endpoint | `Essentials/src/Workflows/Runtime/Api` capability link plus handler that delegates to a projector registered by the compensation feature | Read-only; reuses activity-execution read authorization and disclosure |
| Feature | `ActivitiesCompensationFeature` (CShells feature), enabled in Workbench `shells.json` | Registration test per ADR 0080 D1 risk-based rule |
| Sample participant | `Extensions/Samples.Reservations/` | Test and sample use only, never in production compositions |
| Studio | `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/compensation/` | `CompensationScopeDesigner`, run-view `CompensationScopeRunView`, `CompensationRecoveryPanel` |
| Docs | `website/docs/` reference page; glossary rows | |

## Constitution and ADR checks

- **Internal by default** (ADR 0080 D3): only the activity types, their models and the feature class are
  public. Handler, validator, projector and state types are internal.
- **Risk-based tests** (D1): unit tests for the state machine and structure handler; integration tests for
  restart; no tests that assert on markdown.
- **D7**: runtime code, so human merge.
- **Extension points**: `EXTENSION_POINTS.md` lists the new activity and structure kind; maps regenerate.

## Delivery as PRs

1. Spec 250 (this design packet), as a documentation PR.
2. Runtime: activity, structure handler, validation, feature, unit and integration restart tests, sample
   participant.
3. Inspection API and Studio: designer, run view, recovery panel, component tests.
4. End-to-end: owned-process Workbench restart test, opt-in live browser journey, remaining M1 gaps (repeat
   deliveries, republish, captured values, row "Go to", mocked browser suite), docs, maps. Spec status becomes
   `M1 implemented`.

PRs 2 and 3 can be reviewed independently; PR 4 needs both merged.
