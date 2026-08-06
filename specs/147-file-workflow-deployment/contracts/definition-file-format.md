# Contract: Workflow definition file format

A definition file is a **top-level JSON array** of workflow-version envelopes (`WorkflowVersionReconciliationModel`), camelCase, deserialized via the shell's `IPayloadSerializer`. This contract is pre-existing (spec 085); restated here because it becomes the authoring surface for file-based deployment.

## Envelope

```jsonc
[
  {
    "definitionId": "wfdef_orders-intake",        // PIN THIS — omitting it mints a fresh random id per restart → duplicates
    "name": "orders-intake",                       // required
    "description": "Intake pipeline",              // optional
    "version": "1.0.0",                            // required, SemVer 2.0.0 (author-controlled)
    "state": {                                     // required — WorkflowDefinitionState (authored document, §E2.9.1)
      "rootActivity": {
        "activityVersionId": "actver_…",           // MUST be a resolved catalog id — see recipe below
        /* activity-specific authored contract */
      },
      "variables": [], "inputs": [], "outputs": []
    },
    "sourceCreatedAt": "2026-08-06T00:00:00Z",     // optional
    "contentHash": null,                           // optional, forward-compat (not evaluated today)
    "deleted": false                               // optional; true soft-deletes a source-owned definition
  }
]
```

## Authoring rules

1. **Pin `definitionId`.** The import path warns when it is omitted; without it every restart creates a new definition.
2. **`activityVersionId` must resolve.** Activity references carry `actver_*` catalog ids (`ActivityCatalogStableIds`), deterministic:
   `"actver_" + base64url(SHA-256(activityTypeKey + U+001F + SemVer.ToSortKey(activityVersion)))`.
   Obtain them either by computing offline (recipe above) or by querying a running server's activity catalog (`GET /design/activities…`, or the e2e helper `Get-ActivityVersionId`). Accepting `(activityTypeKey, version)` pairs directly in files is a tracked stretch item (issue #1157 gap 4), out of scope here.
3. **Versions are immutable.** Re-shipping the same `(definitionId, version)` with different `state` is a broken source: the reconciler logs the content-mismatch tripwire and keeps the stored version. Ship a new SemVer instead.
4. **Restart idempotency** comes from the `(definitionId, SemVer sort key)` existence check — unchanged files reconcile to zero writes.
5. **Ordering across files** (folder mode) is ordinal file-name order; within the reconcile pass, per-definition ordering is by SemVer, and only the latest reconciled version is published when `PublishOnReconcile` is on.
6. **Deletion**: `"deleted": true` (latest version wins) soft-deletes a *source-owned* definition; sources can never delete catalog-authored (Studio) definitions. Deleted definitions are never published.

## Failure modes (fail shell activation — visible at `/health/ready` as 503 `shell_activation_failed`)

| Cause | Error surface |
|---|---|
| File missing / unreadable | `InvalidWorkflowCatalogJsonException` naming the path |
| Not a JSON array of envelopes | `InvalidWorkflowCatalogJsonException` naming the path |
| Unresolvable `activityVersionId` | publish-time validation failure (logged, per-definition — does not fail activation when only publishing fails) |
