# Incident handoff UI contract

| Context | Required explanation / behavior |
|---|---|
| Active blocking | Current intervention context; only an actually applicable canonical recovery action, otherwise inspection/share/operator handoff. |
| Active nonblocking | Current attention without claiming the run is blocked or offering an invented retry. |
| Historical/resolved/suppressed | Historical outcome, current-run context and inspection; no new mutation through guidance. |
| RetryScheduled | Existing scheduled attempt and recorded due time; no duplicate manual action even after due time. |
| Pending/followed action | Explain the exact selected target versus another action in this run; preserve canonical suppression. |
| Canceled requester | No handler-start/decision shortcut; preserve canonical guard. |
| Scheduled/non-Faulted source or non-Running parent | Suppress ineligible Retry; exact source evidence and canonical preflight govern reviewability. No new recovery path. |
| Known loaded descendant blocker | Suppress source Retry using exact loaded same-run ancestry and active blocking incident evidence; do not infer a complete subtree from partial pages. Preserve independently offered canonical decisions. |
| Missing current-run summary | Explain unavailable context; reserve a run-mismatch claim for a supplied different execution ID. |
| Missing exact source/recorded parent | Explain incomplete action evidence; do not claim an applicable Retry before its existing preconditions are confirmed. |
| Terminal run or historical incident | Preserve inspection/handoff; no actionable controls for a state the existing runtime refuses. |
| Unknown/failed capability | Honest unavailable/unknown guidance; false alone is not a proven unsupported server. |
| Denied/missing permission projection | No unauthorized action; do not invent a specific denied grant from missing data. |
| Partial/unloaded/denied evidence | Preserve NotLoaded/unavailable boundaries; never infer absent handler or substitute occurrence. |
| Permitted definition handoff | Exact definition and existing route; future-executions/pinned-executable caveat, appropriate read/manage wording. |
| Runtime-only/unknown Design | Existing permitted run evidence/copy/operator handoff; no promise of editing/publishing. |
| Incident/run selection changes | Current target explanation and authority; late observations cannot overwrite the new target. |

Preserve existing affected activity/input navigation, safe clipboard content, modal focus/Escape and original Runs list query/page/returnTo. Pure investigation/inspection/navigation issues zero run/recovery/design mutation. Normal-host proof and fixture-only cases are recorded separately; no new endpoint or recovery action is specified.

Implementation clarification (22 October): `RetryFaultedActivityAlterationHandler.PreflightAsync` already rejects terminal runs, non-Faulted source occurrences and a non-Running recorded parent. A JavaScript input-evaluation failure leaves its activity Scheduled, so WaitingForIntervention plus management/capability alone is insufficient evidence for Retry. Reuse a small shared UI eligibility observation for guidance and canonical control visibility; do not duplicate server concurrency/admission checks or change action payloads/handlers. Existing valid recovery confirmation tests remain, with fixtures representing actual eligible sources.

Round-one review clarification: the same existing preflight rejects a blocking incident on a descendant as `BlockingIncidentOutsideSource`. Reflect that known loaded evidence in shared read-only eligibility; no new runtime rule or broad client-side admission engine. Unknown descendants, concurrency, active scheduler claims and final admission remain server-owned. Scheduled/due automatic retry evidence must suppress duplicate operator controls as well as change copy. New guidance uses semantic Studio typography/spacing tokens.
