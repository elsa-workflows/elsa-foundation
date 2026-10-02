# Research and decisions

1. Live QA proves the expression `qaMissingVariable` is accepted at design time and fails during runtime input materialization. The poison observer loses the existing start payload activity/node identity. Restore that structured causal link; never infer from strings or IDs.
2. WaitForIntervention intentionally preserves Running/Scheduled lifecycle for poisoned work. Incident health is a separate current-state axis. No blanket Faulted conversion or new strategy is authorized by this feature.
3. Existing input inspection snapshots already support failure and incident references. Use that model for attempted-but-failed evaluation, preserving durable checkpoint semantics and never fabricating a successful input snapshot. Infrastructure/cancellation faults remain distinct.
4. Existing poison/incident metadata is persisted. Prefer compatible metadata for additive evidence over schema migration. Exact evidence keys and projection are recorded in the contract and checked by integration tests.
5. The list page API already exists. Apply active/blocking health before count/cursor/pagination within authorization scope; retain lifecycle filtering and historical totals. Array-only servers must expose unsupported health filtering honestly.
6. Flowchart/Sequence share activity rendering; BPMN uses element IDs with bound activity IDs and needs explicit runtime mapping acceptance. Nested scopes and repeated occurrences must navigate by execution identity.
7. Keep the node surface calm: semantic danger border, compact icon/count action, neutral lifecycle pill and separate focus/selection. Header action is independent of saved pane layout. Resolved history is not an active alarm.
8. Dedicated fresh hosts use explicit loopback port/config overrides. Do not rebuild or restart the user's running hosts, alter their DB, or reuse unrelated agents' servers. Heavy build-slot queue and high host load are operational limits, not test results.
