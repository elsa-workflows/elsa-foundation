# Requirements Checklist: Runtime Bookmark State Projection

- [x] No Design-owned authored workflow model dependency is introduced.
- [x] Scope excludes full durable bookmark lookup/index behavior.
- [x] Scope excludes bookmark resume dispatch behavior.
- [x] Bookmark state remains part of split continuation state.
- [x] Checkpoint semantics remain separate from persistence policy.
