# Requirements Checklist: Runtime Activity Invocation Boundary

- [x] Runtime-owned executable artifact remains the execution input.
- [x] Activity invocation targets `ActivityExecutionId`.
- [x] Invocation is queued scheduler work, not recursive bubbling.
- [x] Workflows Runtime does not silently acknowledge `InvokeActivity` without a provider.
- [x] Activities Runtime contributes the concrete provider.
- [x] Scope excludes edge traversal, checkpoints, bookmarks, retry, and outbox behavior.
