# Elsa.Activities.Sequence Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contract is:

- `Sequence.Activities` child slot

## Cross-domain contributions

- `SequenceStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`), including `ProjectScopedVariables` — a `Sequence` is a container scope that can own container-scoped variable declarations visible to its descendant activities (ADR 0027). The authored/executable structures carry these declarations through publishing so the runtime materializes them without re-reading the design document.
