# Requirements Checklist: Runtime Workflow Execution State Store

- [x] Workflow execution state remains separate from activity execution state.
- [x] Projection is checkpoint-writer driven, not scheduler-handler driven.
- [x] Durable provider implementation remains out of scope.
- [x] Runtime execution contracts stay Design-free.
