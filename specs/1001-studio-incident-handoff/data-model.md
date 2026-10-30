# Data model: Incident handoff

No persisted or wire model is added.

- Selected incident: incidentId, workflowExecutionId, status/health/blocking, exact activity/input association and evidence limits.
- Current run: workflowExecutionId, lifecycle, definitionId and pinned executable identity. Never infer health from lifecycle.
- Available evidence: current exact association and existing compensation journal/runtime state; loading/partial/NotLoaded/unavailable remain explicit.
- Action availability: server-projected management hint, capability observation and canonical applicability/pending/canceled state. The existing action model retains its exact target/consequence/confirmation.
- Handoff presentation: concise explanation, permitted existing destination(s), pinned-executable caveat and any known evidence limit. Derived from current inputs; no stale cross-target state or mutation.

Changes to incident/run/endpoint/authority invalidate asynchronous guidance observations. An omitted permission projection fails closed for action availability without proving a particular denial. Sensitive/protected/value-payload content stays in existing authorized evidence components.
