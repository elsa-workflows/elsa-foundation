# Data Model: Dashboard Widgets and Attention

## Dashboard Widget Contribution

Describes one module-provided body that Dashboard can frame and manage.

Fields:

- `id`: stable module-prefixed identity.
- `moduleId`: owner identity.
- `title`, `description`, `order`.
- `defaultVisible`.
- `defaultSize`: one of small, medium, wide, full.
- `supportedSizes`: non-empty set containing the default.
- `permissions`: permissions required before admission/loading.
- `minimumRefreshIntervalMs`, `cacheLifetimeMs`, `timeoutMs`.
- `settings`: optional Widget Settings Definition.
- `load`: optional cancellable snapshot loader.
- `component`: body renderer receiving managed snapshot and settings.

Validation:

- Identity is globally stable and module-prefixed.
- Timings are finite positive values within Host Policy caps.
- Unsupported sizes and invalid settings are rejected before loading.
- Unauthorized widgets are omitted before loader creation/invocation.

## Widget Runtime State

Discriminated states owned by Dashboard:

- `idle`: loader-less or not yet eligible.
- `loading`: first snapshot is in flight.
- `ready`: validated snapshot available.
- `refreshing`: previous snapshot remains visible while replacement loads.
- `empty`: successful snapshot has no display content.
- `error`: isolated loader failure.
- `timedOut`: loader exceeded effective timeout.

Common fields include `startedAt`, `updatedAt`, `nextRefreshAt`, `error`, and
request generation. A later generation always wins; superseded work is aborted.

## Dashboard Preference Document

Preference namespace `dashboard`.

Fields:

- `schemaVersion`.
- `refreshInterval`: off, 1m, 5m, 15m, or 30m.
- `autoAddNewWidgets`.
- `widgets`: ordered map/list keyed by widget ID containing visibility, size,
  order, widget settings schema version, and settings value.
- preference envelope scope/revision/update fields.

Rules:

- Host-pinned visibility overrides personal hiding.
- Unknown widget entries remain stored but render no grid cell.
- Invalid/migration-failed settings reset only the affected widget and emit a
  one-time notice.

## Studio Preference Document

Canonical governed envelope:

- `namespace`.
- `schemaVersion`.
- `revision` / ETag.
- `value`: bounded JSON document validated by namespace owner.
- `updatedAt`.
- implicit key scope: `subjectId`, `tenantId`, `studioHostId`, `namespace`.

State transitions:

```text
missing -> created
current(revision N) -> updated(revision N+1)
current -> conflict (stale If-Match)
older schema -> migrated -> updated
invalid/unregistered/over quota -> rejected
```

No document may contain secrets, credentials, workflow payloads, or protected
domain evidence.

## Attention Contributor

Fields:

- `id`, `displayName`, `requiredPermission`.
- documented default thresholds and Host Policy override descriptors.
- evaluation function accepting query, actor/tenant context, and cancellation.

Rules:

- Complete authorized evaluation is mandatory.
- Maximum five returned member items plus `totalCount`/`isTruncated`.
- No per-entity downstream fan-out; default downstream-call budget is two.
- Stable module-prefixed ID supports dynamic unload/return and snooze recovery.

## Attention Contributor Result

Discriminated union:

- `ready`: `evaluatedAt`, `totalCount`, `isTruncated`, bounded `items`.
- `forbidden`.
- `unavailable`.
- `timedOut`.
- `failed`.

Failure variants carry a machine code, safe detail, and optional diagnostic
destination. Domain failure never changes the overall response into failure.

## Attention Item

Fields:

- `id`: stable inside contributor.
- `generation`: changes for materially new occurrence.
- `severity`: critical, warning, info.
- `title`, optional sanitized `summary`.
- `occurredAt`, `lastObservedAt`.
- `count`.
- `destination`: inspect/navigation target.
- `correlations`: typed kind/value pairs.
- `sensitivity`: metadata or restricted.

Rules:

- Metadata only; no secret values, workflow payloads, raw log properties, stack
  traces, telemetry attributes, credentials, or mutation commands.
- Sort by severity then recency.
- Correlation groups equal typed identifiers only; member limits apply before
  grouping.
- Restricted summaries may be suppressed by Host Policy.

## Attention Snooze

Preference namespace `attention`.

Fields:

- scope key: subject, tenant, Studio host.
- contributor ID and item ID.
- generation and severity at snooze time.
- `snoozedUntil`.
- optional current correlation member keys.

Transitions:

```text
active -> snoozed -> active (expiry)
snoozed -> active (severity increase)
snoozed -> active (generation change)
active/snoozed -> absent (condition resolved)
absent -> snoozed (same id/generation/severity returns before expiry)
```

## Secret Tenant Boundary

Secrets gain authoritative tenant identity.

- `tenantId` is required for new secret metadata/material records.
- repository list/get/update/rotate/test/revoke/delete operations include tenant
  scope derived from authenticated context.
- legacy rows require explicit host-controlled backfill/migration; no shared
  default tenant is inferred at request time.

## Workflow Portfolio Snapshot

Fields:

- `generatedAt`.
- `activeDefinitionCount`.
- `publishedDefinitionCount`: active definitions with live Published reference.
- `unpublishedDraftCount`.
- `invalidDraftCount`: current drafts with one or more derived validation errors.
- optional availability/reason when provider adapter is unsupported.

Counters overlap by design; they are not a partition.

## Workflow Run Health Snapshot

Input:

- inclusive UTC `from`.
- exclusive UTC `to`.
- IANA `timeZone`.
- bucket: hour or day.
- `includeTestRuns` default false.

Output:

- `generatedAt`, normalized range and time zone.
- `startedCount`, `succeededCount`, `failedCount`, `cancelledCount`,
  `incompleteCount`.
- `incidentBearingRunCount`, `incidentCount`, `runningCount`.
- failure and incident-bearing percentages.
- ordered time buckets.
- bounded highest-failure definition summaries.
- optional availability/reason.

Outcome and incident dimensions overlap. Buckets use local calendar boundaries
while filtering uses absolute instants.
