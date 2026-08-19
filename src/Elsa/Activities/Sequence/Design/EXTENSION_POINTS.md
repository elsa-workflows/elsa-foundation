# Extension points — Sequence composite (Design)

T128 split `Elsa.Activities.Sequence` into a `.Design` and a `.Runtime` package so a runtime-only engine can compose
the activity without reaching `Elsa.Workflows.Design.Core`. This project is the **design** half: it owns the
authored structure and may reference Design assemblies freely, because it *is* design. Execution-side contributions
live in [`../Runtime/EXTENSION_POINTS.md`](../Runtime/EXTENSION_POINTS.md), whose design-side entries moved here.

## Registration inventory

| Contribution | Registration | Status |
|---|---|---|
| Authored Sequence structure | `ActivitiesSequenceDesignFeature` registers `SequenceStructureHandler : IActivityStructureHandler` | Implemented |
| Container-scoped variables | `SequenceStructureHandler.ProjectScopedVariables` (ADR 0027) | Implemented |

## Overridable contracts

None. These are structure handlers keyed by structure kind; they are additive contributions, not replaceable
single-owner defaults.

## Boundary

This project references `Elsa.Workflows.Design.Core` and the `.Runtime` half (for the activity's structure-kind
constants and its compiled executable-structure models). Nothing in the runtime half may reference this project.
