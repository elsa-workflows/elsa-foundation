# Additive incident troubleshooting contract

## Run list

Both historical and paged list projections add `activeIncidentCount` and `blockingIncidentCount`; existing `incidentCount` remains historical. Paged request adds optional `incidentHealth=active|blocking|none`. Invalid values are rejected clearly. `active` means neither Resolved nor Suppressed; `blocking` means currently Blocking; `none` means no unresolved incidents. Health filtering is authoritative before authorization-safe total count/cursor/paging. No changes to status semantics. Older clients ignore additive fields. Backend advertises additive capability relation `workflow-instances-health-filter` pointing to the existing paged list route. Studio enables the health filter only with this signal, since older paged servers may silently ignore an unknown query. Older servers do not imply active counts from historical totals; Studio retains historical evidence and identifies unsupported health filtering.

## Causal incident and input evidence

Attributable start/input failures preserve payload-derived `activityExecutionId` and `executableNodeId` in the durable poison metadata and canonical IncidentState fields. A typed input-evaluation failure carries `inputKey`, expression language, and useful root cause through the existing fault capture policy. Reuse existing input snapshot view's `failure`/`incidentId` evidence contract when exposing a failed attempt. No expression-source or exception details bypass sensitive-value authorization. No parsing of human messages/fingerprints. Unattributable engine failures retain null association and an explicit run-level UI explanation.

Metadata keys are centrally defined in RuntimeMetadataKeys: `runtime.activityExecutionId`, `runtime.executableNodeId`, `runtime.inputKey`, `runtime.inputFailureCode`, `runtime.expressionLanguage`, `runtime.inputEvaluationPhase`. Failure code `ExpressionEvaluationFailed` identifies portable evaluation failure; a distinct `InputMaterializationFailed` code/phase may identify pre-evaluator parameter preparation failure. Existing `runtime.faultInnerType` / `runtime.faultInnerMessage` carry policy-controlled fault evidence; do not add raw exception/source/value/stack fields. Studio uses existing permission-filtered incident fields/metadata. Protected inputs keep their existing redacted inner exception. A synthesized payload-free snapshot has `subject=ActivityInput`, `inputKey`, `evaluationPhase`, `failure={code,message,incidentId}`, `captureState=captureFailed` and the existing disclosure state; permission-hidden failure remains withheld/unavailable, not a successful value. Prefer no EF schema migration because metadata already persists. Cancellation, lease loss and checkpoint failure are not recategorized as expression failure.

## Navigation

Graph action resolves the selected exact execution/incident, opens incident details, expands the details pane as needed and frames the authored activity in its executed nested scope. Incident Show affected activity performs the reverse with the same occurrence. Graph node counts may aggregate occurrences. Activity selection with active incidents defaults to the incident; normal healthy selection defaults to activity details. Accessible activation and focus remain distinct from selection/color.

## Presentation

Use current semantic tokens and existing shared components. Label current blocking health as Needs intervention while retaining Running/Scheduled status. Active nonblocking evidence uses a less urgent cue; resolved-only historical evidence stays discoverable without current alarm. Lead error summary with activity/input/root cause, retain expandable technical details/copy. Accepted-but-faulted test dispatch is warning feedback with an incident review action.
