# Requirements Checklist: Runtime Activity Input Resolution

- [x] Scope excludes expression execution.
- [x] Scope excludes reference provider resolution.
- [x] Active activity output reads remain scoped by `ActivityExecutionId`.
- [x] Durable values are read only through declared durable value state.
- [x] Design models and history/audit output reads remain excluded.
