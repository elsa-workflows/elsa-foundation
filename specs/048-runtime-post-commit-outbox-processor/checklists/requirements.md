# Requirements Checklist: Runtime Post-Commit Outbox Processor

- [x] Processor is a single-run boundary, not a background worker.
- [x] Delivery claiming remains out of scope.
- [x] Failed dispatch uses outbox retry state, not domain retry.
- [x] Runtime stays Design-free.
