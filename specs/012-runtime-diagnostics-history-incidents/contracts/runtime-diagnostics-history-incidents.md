# Contract: Runtime Diagnostics, History, And Incidents

Runtime separates continuation state from observability:

```text
Checkpoint state changes -> workflow/scheduler/activity/bookmark/durable-value/incident/operational state
History/diagnostics -> projections outside continuation state
```

Rules:

- Runtime does not read history/audit records to continue.
- `IncidentState` is the minimal runtime state for unresolved or blocking incidents.
- Incident history projection may carry richer diagnostic payloads.
- Payload capture is policy-controlled.
- Default policy excludes sensitive payloads and omits workflow/activity input-output snapshots.
