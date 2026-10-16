# Elsa.Activities.Sequence Extension Points

This module does not expose replaceable service contracts in v1. Its activity-owned contract is:

- `Sequence.Activities` child slot

## Cross-domain contributions

- `SequenceStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`), including `ProjectScopedVariables` — a `Sequence` is a container scope that can own container-scoped variable declarations visible to its descendant activities (ADR 0027). The authored/executable structures carry these declarations through publishing so the runtime materializes them without re-reading the design document.
- `SequenceStartEntryProvider` implements `IStructuralStartEntryProvider` (`Elsa.Workflows.Runtime.Core`, spec 238). For the trigger binding preflight's start-path rule, it names the first child in structure order as where every run enters a Sequence, so a start trigger is valid inside a Sequence only through child 0.
