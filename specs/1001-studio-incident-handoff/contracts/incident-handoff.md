# Incident handoff UI contract

| Context | Required explanation / behavior |
|---|---|
| Active blocking | Current intervention context; only an actually applicable canonical recovery action, otherwise inspection/share/operator handoff. |
| Active nonblocking | Current attention without claiming the run is blocked or offering an invented retry. |
| Historical/resolved/suppressed | Historical outcome, current-run context and inspection; no new mutation through guidance. |
| RetryScheduled | Existing scheduled attempt and recorded due time; no duplicate manual action even after due time. |
| Pending/followed action | Explain the exact selected target versus another action in this run; preserve canonical suppression. |
| Canceled requester | No handler-start/decision shortcut; preserve canonical guard. |
| Unknown/failed capability | Honest unavailable/unknown guidance; false alone is not a proven unsupported server. |
| Denied/missing permission projection | No unauthorized action; do not invent a specific denied grant from missing data. |
| Partial/unloaded/denied evidence | Preserve NotLoaded/unavailable boundaries; never infer absent handler or substitute occurrence. |
| Permitted definition handoff | Exact definition and existing route; future-executions/pinned-executable caveat, appropriate read/manage wording. |
| Runtime-only/unknown Design | Existing permitted run evidence/copy/operator handoff; no promise of editing/publishing. |
| Incident/run selection changes | Current target explanation and authority; late observations cannot overwrite the new target. |

Preserve existing affected activity/input navigation, safe clipboard content, modal focus/Escape and original Runs list query/page/returnTo. Pure investigation/inspection/navigation issues zero run/recovery/design mutation. Normal-host proof and fixture-only cases are recorded separately; no new endpoint or recovery action is specified.
