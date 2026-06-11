# Feature Specification: Runtime Bookmark Stimulus Resume Dispatch

**Feature Branch**: `codex/runtime-bookmark-stimulus-resume`
**Created**: 2026-06-11
**Status**: Draft
**Input**: Continue the Runtime Execution Seam after bookmark state projection and bookmark resume contract. Bookmark state and artifact resume resolution exist, but runtime has no stimulus lookup or dispatch seam that turns a matched bookmark into workflow-agent work.

## Scenarios & Tests

1. Given bookmark continuation state for a workflow execution, when a resume stimulus with matching type and hash arrives, then runtime selects the non-expired bookmark and resolves it through the pinned executable artifact.
2. Given a matched bookmark resolves successfully, when runtime dispatches resume, then it enqueues a `ResumeBookmark` command through the workflow execution agent using stable runtime IDs and idempotency.
3. Given no bookmark matches, the workflow execution state is missing, the executable artifact is missing, or the resume target is not declared by the pinned artifact, then dispatch fails clearly without enqueueing agent work.
4. Given multiple matching bookmarks exist, dispatch rejects ambiguity instead of guessing which activity execution to resume.

## Requirements

- **FR-001**: Runtime.Core MUST expose a bookmark stimulus lookup contract over `BookmarkState.StimulusType` and `BookmarkState.StimulusHash`.
- **FR-002**: The default lookup implementation MUST ignore expired bookmarks and reject ambiguous matches.
- **FR-003**: Runtime.Core MUST expose a bookmark resume dispatcher that loads workflow execution state, loads the pinned executable artifact, resolves `BookmarkState.ResumeTargetId`, and sends a `ResumeBookmark` workflow execution command.
- **FR-004**: The resume command payload MUST carry bookmark ID, activity execution ID, executable node ID, resume target ID, and stimulus input; it MUST NOT carry C# callback method names.
- **FR-005**: Dispatcher idempotency MUST be deterministic from workflow execution ID, bookmark ID, stimulus type, stimulus hash, and optional caller-provided idempotency key.
- **FR-006**: Runtime API composition MUST register the lookup and dispatcher as overridable defaults.
- **FR-007**: Runtime execution projects MUST remain free of Design-owned authored workflow model dependencies.

## Non-Goals

- Consuming or deleting bookmarks after successful dispatch.
- Invoking activity resume handlers.
- Implementing external ingress adapters or a global unmatched stimulus inbox.
- Durable database indexes or provider-specific query optimization.
- Elsa 3 callback-method bookmark migration.

## Acceptance Criteria

- Tests prove stimulus lookup matches only active, non-expired bookmark state.
- Tests prove ambiguous matches fail clearly.
- Tests prove resume dispatch uses the pinned executable artifact and `ResumeTargetId`.
- Tests prove missing state/artifact/resume target failures do not enqueue agent commands.
- Focused runtime and architecture tests pass.
