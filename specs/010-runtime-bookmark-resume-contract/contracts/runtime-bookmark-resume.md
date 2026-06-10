# Contract: Runtime Bookmark Resume

## Bookmark State

Runtime bookmarks are durable resume handles keyed by workflow execution, activity execution, executable node, stimulus, and resume target.

Required durable fields:

- `BookmarkId`
- `WorkflowExecutionId`
- `ActivityExecutionId`
- `ExecutableNodeId`
- `ResumeTargetId`
- `StimulusType`
- `StimulusHash`

Forbidden durable fields:

- C# callback method name
- Delegate reference
- Runtime handler instance
- Design-owned authored workflow document reference as an execution input

## Resume Resolution

Resolution follows this path:

```text
BookmarkState.ResumeTargetId
  -> WorkflowExecutionState.PinnedExecutable
  -> WorkflowExecutable.ResumeTargets[ResumeTargetId]
  -> WorkflowExecutable.Nodes[ExecutableNodeId]
```

The resolver must fail when:

- The executable artifact is not the workflow execution's pinned artifact.
- The bookmark's resume target ID is missing from the artifact.
- The resume-target table entry's value does not carry the same resume target ID as its lookup key.
- The resolved resume target points at a different executable node than the bookmark.
- The resolved executable node is missing from the artifact.

## Deferred Work

This contract does not define bookmark persistence indexing, external stimulus matching, resume command execution, or activity handler invocation.
