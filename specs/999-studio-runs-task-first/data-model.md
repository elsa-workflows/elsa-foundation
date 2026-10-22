# UI state model

No new persisted entity or runtime API. Existing incident meanings remain in [Spec192](../192-incident-troubleshooting/contracts/incident-troubleshooting.md).

| State | Owner and invariant |
| --- | --- |
| Applied Runs filters | Existing URL-backed RunHistoryFilters; status, incidentHealth, definitionId and detailed criteria remain independent. Chips describe only this state. |
| Advanced draft | Local form state; typing does not change applied query. Apply commits the supported criteria together. A primary change updates only its matching field so unrelated drafts survive. |
| Workflow choice | Stable definitionId with hydrated display name; the search text is not a runtime predicate. Duplicate names retain ID/version disambiguation; lookup failure keeps the selected raw ID. |
| Paging | Existing pageSize and opaque cursor. A filter change clears cursor/selection and retains pageSize. Page changes clear selection. |
| Selection | Existing selected run IDs, permission and deletion capability gates; bulk toolbar is absent at zero selected. |
| Return context | Encoded local Runs-list URL, parsed and canonicalized through existing Runs helpers. Only the exact local list pathname is accepted. No context means the default list. |
| Disclosure | Real applied advanced count; optional collapse behavior does not override errors/explicit force-open. Hidden form stays mounted. |

List request generations and chooser requests must ignore stale completions. Loading, ready-empty, unsupported capability and failed states stay distinct. Only a successful empty cursor page uses existing first-page recovery; a request error remains an error with Retry.
