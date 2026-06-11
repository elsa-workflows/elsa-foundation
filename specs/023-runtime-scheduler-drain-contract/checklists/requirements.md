# Requirements Checklist: Runtime Scheduler Drain Contract

- [x] Drain boundary is keyed by workflow execution ID.
- [x] Handler dispatch is separate from activity execution.
- [x] Faults stop the drain by default.
- [x] Default behavior is no-op acknowledgement, not scheduler behavior.
