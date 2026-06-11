# Tasks: Workflow Root Activity Contract

**Input**: `specs/070-workflow-root-activity-contract/spec.md`

- [x] T001 Add architecture-doc corrections and Speckit slice artifacts.
- [x] T002 Mark conflicting graph-shaped specs as superseded by this slice.
- [x] T003 Refactor `WorkflowDefinitionState` to expose `RootActivity`.
- [x] T004 Replace generic `ActivityNode.Composition` with activity-specific child-slot metadata/adapters.
- [x] T005 Update design projections, persistence commands, validators, and diffing for root activity traversal through child slots.
- [x] T006 Refactor `WorkflowExecutable` to expose `RootActivity` without generic `ExecutableNode.Composition` and derive `NodesById` through child slots.
- [x] T007 Update publishing to compile the authored root activity recursively through child slots into the executable root activity.
- [x] T008 Update start scheduling to schedule only the executable root activity.
- [x] T009 Remove workflow-level/generic executable edge traversal from completion scheduling.
- [x] T010 Rewrite focused design, publishing, runtime, and architecture tests.
- [x] T011 Run targeted validation.
- [x] T012 Refresh generated maps if required.
- [x] T013 Commit the completed work unit.
