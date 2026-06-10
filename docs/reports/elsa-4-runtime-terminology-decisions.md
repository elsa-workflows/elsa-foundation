# Elsa 4 Runtime Terminology Decisions

Status: terminology decisions from Runtime Execution Seam brainstorming. This is a staging report for glossary promotion, not yet the canonical glossary.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

Decision source: [Elsa 4 runtime execution addendum topics](elsa-4-runtime-execution-addendum-topics.md).

Related decisions: [Elsa 4 runtime execution brainstorm decisions](elsa-4-runtime-execution-brainstorm-decisions.md).

## Purpose

Capture runtime execution vocabulary selected during the Elsa 4 runtime execution brainstorm. Stable terms should later be promoted into `docs/glossary/` after the first execution Speckit confirms the concrete model names.

## Naming Principle

Use distinct words for distinct runtime concepts. Avoid relying on context to disambiguate overloaded terms when the runtime can provide clearer names.

The most important split:

```text
Pause / Unpause
  Administrative control-plane operations.

Suspend / Resume
  Durable runtime wait/bookmark continuation operations.

Wait / Continue
  Volatile in-memory or internal scheduler continuation operations.
```

## Core Terms

### Authored Workflow Document

Design-owned durable authoring/import/export shape.

Use for the stable document edited by Studio and imported/exported by APIs. Runtime does not execute this document directly.

### Workflow Executable

Runtime-owned compiled artifact that can be executed.

Use for the artifact produced by compile/publish from an authored workflow document. Workflow executions pin to an exact executable artifact snapshot by default.

### Executable Node

Runtime node inside the executable artifact.

Use to distinguish runtime executable nodes from authored/design activity nodes.

### Workflow Execution

One running or resumable execution of a workflow executable.

Use for the runtime instance/execution state that owns scheduler state, activity executions, bookmarks, durable values, incidents, and operational references.

### Activity Execution

One concrete execution of one executable activity node.

Use instead of `ActivityAttempt`. Each loop iteration or parallel branch execution gets its own activity execution identity.

### Trigger

External source that starts or resumes workflow execution.

Examples include HTTP endpoint, timer trigger, message trigger, external event, or manual/API start.

### Generator

In-workflow activity that emits execution events over time.

A generator belongs to a running workflow execution scope. It does not outlive its owning scope by default.

### Generated Event

An event emitted by a generator activity into the workflow scheduler.

The event should have identity for diagnostics and ordering, but does not necessarily become a full activity execution.

### Bookmark

Durable runtime resume handle.

Bookmarks store executable-artifact resume targets, not C# callback method names.

### Resume Target

Stable executable-artifact target used to resolve a bookmark into runtime resume behavior.

`ResumeTargetId` is the durable contract. Method names and delegates are implementation details.

### Checkpoint

Named persistence boundary for runtime state changes and post-commit intents.

Checkpoint names describe durable runtime facts, while persistence policy decides when and how to flush them.

### Durable Suspension

Persisted execution wait/unload boundary.

Use when workflow execution commits state and leaves memory until a bookmark/event resumes it.

### Volatile Wait

In-memory wait that keeps workflow execution alive in the current host context.

Use for await-style behavior where an activity execution or branch waits without durable unload.

### Pause

Administrative hold.

Pause is control-plane state. It is not durable suspension and does not imply a runtime bookmark.

### Unpause

Remove administrative hold.

Use instead of overloading `Resume` for control-plane pause reversal.

### Suspend

Enter durable persisted wait/unload state.

Use for runtime execution state, not administrative control.

### Resume

Continue from durable suspension/bookmark.

Reserve for durable runtime continuation. Avoid using `Resume` for administrative unpause.

### Wait

Activity or execution is waiting.

Use as the general state/behavior word that can be refined into durable suspension or volatile wait.

### Continue

Proceed after a volatile wait or internal scheduler event.

Use for internal scheduler continuations and volatile wait completion.

### Incident

Operator-visible problem record.

Use as the persisted problem/attention record.

### Faulted

Lifecycle status meaning execution cannot continue normally.

Use as status, not as a competing persisted problem object.

### Failure

Generic explanatory word for something going wrong.

Use in prose when a precise runtime concept such as incident or faulted status is not intended.

## Fault And Incident Rule

New Elsa 4 model names should avoid `Fault` as a noun unless they are referring to legacy/Elsa 3 behavior.

Preferred usage:

```text
Incident
  Persisted operator-visible problem record.

Faulted
  Lifecycle status.

Failure
  Generic explanatory word.
```

Avoid:

```text
Fault
  As a durable domain object competing with Incident.
```

## API Naming Guidance

Use clear operation names:

```http
POST /workflow-executions/{id}/pause
POST /workflow-executions/{id}/unpause
POST /bookmarks/{id}/resume
```

Use explicit internal commands:

```csharp
PauseWorkflowExecution
UnpauseWorkflowExecution
ResumeBookmark
ContinueVolatileWait
```

## Promotion Notes

Before promoting this report into `docs/glossary/`, confirm:

- Final concrete name for `WorkflowExecutable`.
- Final concrete names for executable node and authored node concepts.
- Whether `GeneratedEvent` remains the term for generator emissions.
- Whether `DurableSuspension` and `VolatileWait` become public terms or only internal specification terms.
- Whether public API uses `unpause` or another product-facing word such as `release`.
