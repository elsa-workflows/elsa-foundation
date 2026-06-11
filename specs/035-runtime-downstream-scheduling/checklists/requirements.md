# Requirements Checklist: Runtime Downstream Scheduling

- [x] Scope follows locked completion-propagation and checkpoint-ordering decisions.
- [x] Downstream scheduling remains runtime scheduler work.
- [x] Downstream work is delivered after checkpoint writer success.
- [x] Durable outbox, retry, joins, workflow completion, and activity invocation remain out of scope.
- [x] Runtime remains free of Design-owned authored workflow models.
