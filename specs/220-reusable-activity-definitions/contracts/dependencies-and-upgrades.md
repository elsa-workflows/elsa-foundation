# Contract: Dependency, Usage, and Upgrade Read Models

Dependency APIs distinguish authoritative direct facts from rebuildable reverse/transitive projections. Upgrade APIs operate only on mutable drafts (or create drafts from immutable versions); they never rewrite published activity versions, workflow versions, templates, or executables.

## 1. Dependency route

```http
GET /design/activities/versions/{versionId}/dependencies
    ?direction=outbound|inbound
    &transitive=false|true
    &include=versions,drafts
    &cursor={opaque}
    &limit=100
```

Defaults:

- `direction=outbound`
- `transitive=false`
- `include=versions`
- bounded server-configured page size when `limit` is omitted

The same endpoint can return uses from activity and workflow consumers. `versionId` always names the exact reusable activity version at the root.

## 2. `ActivityDependencyPageView`

```json
{
  "root": {
    "kind": "ActivityVersion",
    "definitionId": "activity-def-tax",
    "versionId": "activity-ver-tax-2",
    "version": "2.0.0",
    "draftId": null,
    "revision": null,
    "templateHash": "sha256-tax-2",
    "tenantId": "tenant-a",
    "lifecycle": "Active"
  },
  "query": {
    "direction": "Inbound",
    "transitive": true,
    "include": ["Versions", "Drafts"]
  },
  "consistency": {
    "kind": "DerivedProjection",
    "isAuthoritative": false,
    "asOfSequence": 94812,
    "asOf": "2026-07-15T12:30:00Z",
    "rebuildId": null
  },
  "items": [],
  "nextCursor": null
}
```

### Consistency values

| `kind` | Meaning |
|---|---|
| `AuthoritativeDirect` | Direct outbound immutable edge records read from publication truth. |
| `DerivedProjection` | Reverse, transitive, or draft-usage view derived from authoritative versions/current drafts. |

Clients MUST display/use `asOf` when making bulk upgrade decisions from a derived view. Upgrade-plan creation re-reads authoritative/current state and is not allowed to trust this list as a write precondition.

## 3. `DefinitionReferenceView`

Shared identity used for owners, dependencies, roots, and paths.

```json
{
  "kind": "WorkflowDraft",
  "definitionId": "workflow-def-checkout",
  "versionId": null,
  "version": null,
  "draftId": "workflow-draft-checkout",
  "revision": 17,
  "templateHash": null,
  "tenantId": "tenant-a",
  "lifecycle": "Active"
}
```

Allowed `kind` values:

- `ActivityVersion`
- `ActivityDraft`
- `WorkflowVersion`
- `WorkflowDraft`

Immutable entries use `versionId` and semantic `version`; drafts use `draftId` and `revision`. `templateHash` is present for published activity versions and for workflow versions when the workflow artifact is already available.

## 4. `ActivityDependencyItemView`

```json
{
  "relationshipId": "activity-ver-checkout-3:node-tax:activity-ver-tax-2",
  "owner": {
    "kind": "ActivityVersion",
    "definitionId": "activity-def-checkout",
    "versionId": "activity-ver-checkout-3",
    "version": "3.0.0",
    "templateHash": "sha256-checkout-3",
    "tenantId": "tenant-a",
    "lifecycle": "Active"
  },
  "dependency": {
    "kind": "ActivityVersion",
    "definitionId": "activity-def-tax",
    "versionId": "activity-ver-tax-2",
    "version": "2.0.0",
    "templateHash": "sha256-tax-2",
    "tenantId": "tenant-a",
    "lifecycle": "Active"
  },
  "occurrence": {
    "occurrenceId": "node-tax",
    "nodeOrigin": [
      { "kind": "AuthoredNode", "id": "node-tax" }
    ]
  },
  "isDirect": true,
  "depth": 1,
  "path": [
    {
      "kind": "ActivityVersion",
      "definitionId": "activity-def-checkout",
      "versionId": "activity-ver-checkout-3",
      "version": "3.0.0",
      "templateHash": "sha256-checkout-3"
    },
    {
      "kind": "ActivityVersion",
      "definitionId": "activity-def-tax",
      "versionId": "activity-ver-tax-2",
      "version": "2.0.0",
      "templateHash": "sha256-tax-2"
    }
  ]
}
```

Rules:

- `owner` is the artifact/draft containing the exact placement; `dependency` is always the referenced immutable activity version.
- `occurrenceId` is stable within the owner and lets tooling focus the placed node.
- `path` is included for transitive results and cycle diagnostics. It uses exact identities, not logical definition ids alone.
- A repeated dependency occurrence yields one direct item per occurrence even when both point to the same exact version.
- Unauthorized cross-tenant owners are omitted; exact ids do not bypass authorization.

## 5. Cursor behavior

Dependency cursors are opaque and bind:

- tenant/visibility scope,
- root version id,
- direction/transitive/include query,
- projection watermark,
- deterministic ordering position.

Ordering is by `depth`, owner kind, owner identity, occurrence id, and dependency version id (all ordinal). A binding mismatch returns `409 activity.cursor.binding-mismatch`; an unavailable watermark returns `410 activity.cursor.expired`.

## 6. Create an upgrade plan

```http
POST /design/activities/upgrade-plans
Content-Type: application/json
```

```json
{
  "replacements": [
    {
      "fromVersionId": "activity-ver-tax-1",
      "toVersionId": "activity-ver-tax-2"
    }
  ],
  "roots": [
    { "kind": "WorkflowDraft", "id": "workflow-draft-checkout" },
    { "kind": "ActivityDraft", "id": "activity-draft-invoice" }
  ],
  "includeTransitiveDependents": true,
  "createDraftsForPublishedDependents": true
}
```

Rules:

- Replacements are exact from/to version pairs. There is no “upgrade to latest” server interpretation.
- A root uses the provider-neutral `{kind,id}` identity shape; `id` is a draft or immutable version id according to `kind`.
- Roots are explicit authorization/scope boundaries. Discovery APIs can help users choose them, but the plan request names the approved roots.
- `createDraftsForPublishedDependents=true` means the plan may propose a new draft cloned from an immutable dependent version; it does not modify that version.
- Planning verifies tenant rules, lifecycle, cycles, compatibility diffs, and provider schema readability.

Response: `201 Created` with `ActivityUpgradePlanView`.

## 7. `ActivityUpgradePlanView`

```json
{
  "planId": "upgrade-plan-1",
  "createdAt": "2026-07-15T12:40:00Z",
  "expiresAt": "2026-07-15T13:10:00Z",
  "status": "Ready",
  "replacements": [
    {
      "from": {
        "definitionId": "activity-def-tax",
        "versionId": "activity-ver-tax-1",
        "version": "1.1.0",
        "templateHash": "sha256-tax-1"
      },
      "to": {
        "definitionId": "activity-def-tax",
        "versionId": "activity-ver-tax-2",
        "version": "2.0.0",
        "templateHash": "sha256-tax-2"
      }
    }
  ],
  "expectedSnapshots": [
    {
      "kind": "WorkflowDraft",
      "id": "workflow-draft-checkout",
      "revision": 17,
      "definitionId": "workflow-def-checkout",
      "headVersionId": "workflow-ver-checkout-4"
    },
    {
      "kind": "ActivityDefinition",
      "id": "activity-def-invoice",
      "revision": null,
      "definitionId": "activity-def-invoice",
      "headVersionId": "activity-ver-invoice-2"
    }
  ],
  "steps": [],
  "diagnostics": []
}
```

### `status`

- `Ready`: all proposed steps can be applied if snapshots remain current.
- `Blocked`: one or more error diagnostics require user action; apply is rejected.
- `Applied`: plan has already been applied and is idempotently readable.
- `Expired`: plan cannot be applied.

## 8. `ActivityUpgradeStepView`

```json
{
  "stepId": "step-activity-invoice",
  "order": 10,
  "target": {
    "kind": "ActivityDraft",
    "definitionId": "activity-def-invoice",
    "draftId": "activity-draft-invoice",
    "revision": 5
  },
  "action": "UpdateDraft",
  "dependsOnStepIds": [],
  "replacements": [
    {
      "occurrenceId": "node-tax",
      "fromVersionId": "activity-ver-tax-1",
      "toVersionId": "activity-ver-tax-2"
    }
  ],
  "expectedRevision": 5,
  "expectedDefinitionHeadVersionId": "activity-ver-invoice-2",
  "resultingDiff": {
    "compatibility": "Breaking",
    "requiredBump": "Major",
    "behaviorChanged": true,
    "changes": []
  },
  "diagnostics": []
}
```

Allowed `action` values:

- `UpdateDraft`: update an existing selected draft.
- `CloneActivityVersion`: create an activity draft from a published dependent version, then apply replacements.
- `CloneWorkflowVersion`: create a workflow draft from a published dependent version, then apply replacements.

Bottom-up ordering means an activity draft that upgrades its own dependencies precedes any parent activity/workflow draft that will reference the new result. Because publishing a resulting draft is an explicit later action, a plan may include a blocking handoff: the parent step names the expected future version selection but cannot be applied until that child draft is published. The API reports such steps as `Blocked` with `activity.upgrade.requires-published-version` rather than guessing a version id.

## 9. Apply an upgrade plan

```http
POST /design/activities/upgrade-plans/{planId}/apply
Content-Type: application/json
```

```json
{
  "selectedStepIds": [
    "step-activity-invoice",
    "step-workflow-checkout"
  ]
}
```

Rules:

- Omitting `selectedStepIds` selects all ready steps.
- A subset must be dependency-closed. Otherwise `422 activity.upgrade.selection-not-closed` lists missing prerequisite steps.
- Apply rechecks every expected draft revision, definition head, exact target version, lifecycle, and tenant rule under the required locks.
- Any stale snapshot returns `409 activity.upgrade.stale-plan` and writes nothing.
- All selected draft creations/updates commit atomically.
- Plan apply does not publish drafts; publication remains a separately reviewed SemVer transition.

Response: `200 OK`.

```json
{
  "planId": "upgrade-plan-1",
  "status": "Applied",
  "appliedAt": "2026-07-15T12:45:00Z",
  "drafts": [
    {
      "kind": "ActivityDraft",
      "draftId": "activity-draft-invoice",
      "definitionId": "activity-def-invoice",
      "revision": 6,
      "created": false
    },
    {
      "kind": "WorkflowDraft",
      "draftId": "workflow-draft-checkout",
      "definitionId": "workflow-def-checkout",
      "revision": 18,
      "created": false
    }
  ],
  "diagnostics": []
}
```

## 10. Lifecycle semantics in dependency views

- `Retired` targets remain visible in existing version/draft dependencies and remain executable inside closed parent templates. New direct authoring selection is blocked.
- `Revoked` targets remain visible with lifecycle state and produce blocking diagnostics where policy disallows publication/dispatch.
- Retiring or revoking a version does not cascade mutations to dependency edges, parent versions, templates, or workflow artifacts.
- Deleting/retiring source Design records does not remove executable closure needed by live Source References.
