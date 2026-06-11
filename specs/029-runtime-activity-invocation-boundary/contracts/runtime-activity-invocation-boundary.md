# Contract: Runtime Activity Invocation Boundary

- `StartActivity` must enqueue deterministic `InvokeActivity` work after `ActivityExecutionState` becomes `Running`.
- `InvokeActivity` must validate the pinned executable snapshot, executable node, and activity execution state before invoking an activity.
- Activities Runtime contributes the concrete invocation handler; Workflows Runtime contributes only a fallback that reports missing invocation support.
- Invocation uses runtime-owned executable node descriptors and `IActivityFactory`; it must not load Design-owned authored workflow state.
- Invocation records only the targeted activity execution state. Completion propagation remains a later scheduler slice.
