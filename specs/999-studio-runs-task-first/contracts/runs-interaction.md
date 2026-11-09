# Runs interaction contract

This is a client interaction contract using existing APIs, not a new backend contract.

- Needs attention applies `incidentHealth=active`; Running applies `status=Running`. Both compose. Preserve narrower blocking and other supported choices under More filters.
- Definition search uses existing `listDefinitions` and exact identity lookup; choosing a result applies `definitionId`. Typing never sends a runtime name predicate. The chooser replaces the separate definition-ID field; URL-provided identities remain supported and exact IDs stay visible/copyable. Search covers the server result set, with truthful paging/continuation if more matches exist.
- Primary choices apply immediately. Advanced fields apply together. Applied chips are removable individually, Clear all clears draft and applied criteria, and every filter change resets cursor/selection while retaining page size.
- `AdvancedSection.allowCollapseWhenSet` is optional/default false. When true, a nonzero set count alone does not force open. Errors and explicit force-open still take priority; the real badge and external chips remain visible.
- Run rows lead with resolved human names and retain exact copyable IDs. Copy/selection controls do not trigger row inspection. Bulk actions keep their existing capability/permission gates and appear only after selection.
- Opening detail adds an encoded `returnTo` representing the canonical current Runs list. Validate against the exact local `/workflows/instances` pathname and rebuild only supported filters/page state; reject external, protocol-relative and unrelated paths. Preserve existing detail `tab` and `activityExecutionId` behavior. Direct links return to the default list.
- Loading, empty, unsupported and request failure are separate. Failed requests have an actionable Retry. Do not change authorization, tenant scoping, date conversion, server filtering before paging or successful-empty-cursor recovery.

Acceptance mappings: spec US1→primary/chooser, US2→draft/chips/disclosure, US3→identity/selection, US4→return/states.
