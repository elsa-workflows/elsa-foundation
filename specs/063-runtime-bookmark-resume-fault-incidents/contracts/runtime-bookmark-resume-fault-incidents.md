# Contract: Runtime Bookmark Resume Fault Incidents

When bookmark resume handles a non-cancellation fault:

- create one blocking `IncidentState`;
- add the incident ID to the faulted `ActivityExecutionState.IncidentIds`;
- commit both state changes through `RuntimeCheckpointNames.IncidentRecorded`;
- preserve the bookmark state;
- do not emit history/audit payloads as continuation state.

The incident ID is deterministic from the resume scheduler work item, activity execution ID, and fault sub-status so replayed work does not duplicate incident state.
