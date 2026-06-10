# Data Model: Runtime Diagnostics History And Incidents

## RuntimeHistoryEvent

Observation record for what happened during execution. Categories are workflow, activity, bookmark, value, incident, scheduler, and operational lifecycle events. History events are not read by runtime continuation logic.

## WorkflowLifecycleHistoryEvent / ActivityLifecycleHistoryEvent

Typed lifecycle projections for common workflow and activity transitions. They carry runtime execution identities and optional safe metadata.

## IncidentState

Minimal continuation state for an execution-affecting incident. It supports querying blocking incidents without replaying history.

## IncidentHistoryProjection

Richer incident observation projection. It may carry diagnostic payloads according to policy, but is not continuation state.

## RuntimePayloadCapturePolicy

Policy decision surface for whether history/diagnostic payloads capture nothing, metadata only, or full payload.
