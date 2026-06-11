# Requirements Checklist: Runtime Operational Recovery And Post-Commit Outbox

- [X] Operational state is typed and provider-neutral.
- [X] Checkpoint envelopes carry typed operational state.
- [X] Recovery scanner contracts are provider boundaries.
- [X] Outbox state models record, deliver, and mark-delivered ordering.
- [X] Wait-dependent post-commit intent fields are explicit.
- [X] Domain retry policy boundary is separate from operational recovery.
- [X] Runtime contracts do not depend on Design-owned workflow models.
