# Extension points — Control-flow composites (Design)

T128 split `Elsa.Activities.ControlFlow` into a `.Design` and a `.Runtime` package so a runtime-only engine can compose
the activity without reaching `Elsa.Workflows.Design.Core`. This project is the **design** half: it owns the
authored structure and may reference Design assemblies freely, because it *is* design. Execution-side contributions
live in [`../Runtime/EXTENSION_POINTS.md`](../Runtime/EXTENSION_POINTS.md), whose design-side entries moved here.

## Registration inventory

| Contribution | Registration | Status |
|---|---|---|
| Authored structures for If, Switch, ForEach, For, While, Do, Parallel | `ActivitiesControlFlowDesignFeature` registers seven `IActivityStructureHandler` implementations | Implemented |
| Duplicate Switch case validation (FR-034) | `ActivitiesControlFlowDesignFeature` registers `SwitchDuplicateCaseValidator : IDraftValidator` | Implemented |
| `If` authoring helpers | `IfBuilderExtensions` | Implemented |

## Overridable contracts

None. These are structure handlers keyed by structure kind; they are additive contributions, not replaceable
single-owner defaults.

## Boundary

This project references `Elsa.Workflows.Design.Core` and the `.Runtime` half (for the activity's structure-kind
constants and its compiled executable-structure models). Nothing in the runtime half may reference this project.
