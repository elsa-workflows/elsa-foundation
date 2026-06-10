# Tasks: Checkpoint Commit Envelope And Post-Commit Intent Boundary

**Input**: `specs/008-checkpoint-commit-envelope/spec.md`

- [X] T001 Add `RuntimeCheckpointCommit` and state-change envelope models in `src/Elsa/Workflows/Runtime/Core/Models/`
- [X] T002 Add `IRuntimePostCommitIntentDispatcher` and update `IRuntimeCheckpointWriter` to accept commit envelopes.
- [X] T003 Add `RuntimeCheckpointCommitter` orchestration service.
- [X] T004 Add focused runtime contract tests for commit categories, policy separation, and post-commit dispatch ordering.
- [X] T005 Update extension-point catalog and Speckit current-feature pointers.
- [X] T006 Run runtime and architecture validation.
