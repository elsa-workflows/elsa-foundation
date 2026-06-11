# Contract: Runtime Activity Fault Incidents

When activity invocation handles a non-cancellation fault:

- create one blocking `IncidentState`;
- add the incident ID to the faulted `ActivityExecutionState.IncidentIds`;
- commit both state changes through `RuntimeCheckpointNames.IncidentRecorded`;
- do not emit history/audit payloads as continuation state.

The incident ID is deterministic from the invoke scheduler work item, activity execution ID, and fault sub-status so replayed work does not duplicate incident state.
