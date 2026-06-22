# Data Model: Extension Builder Studio UI

## ExtensionBuilderCapabilities

- `can-create-workspace`: advisory flag for workspace/project creation.
- `can-edit-files`: advisory flag for create/edit/delete file affordances.
- `can-build`: advisory flag for build submission.
- `can-promote`: advisory flag for promotion.
- `can-rollback`: advisory flag for rollback.

Validation: Missing or false flags disable the corresponding UI. Server rejection remains authoritative.

## ExtensionWorkspace

- `id`
- `name`
- `owner`
- `createdAt`
- `updatedAt`
- `projects`: `ExtensionProject[]`

Relationships: Contains one or more projects. Owner-scoped and server-side persisted.

## ExtensionProject

- `id`
- `workspaceId`
- `name`
- `templateId`
- `packageId`
- `packageVersion`
- `currentRevision`
- `latestBuildStatus`
- `runtimeStatus`

Relationships: Belongs to a workspace; has project files, build history, build artifacts, and runtime state.

Validation: Project name, package id, and version are required before project creation/build/promote affordances are enabled.

## ExtensionTemplate

- `id`
- `name`
- `description`
- `tags`
- `primary`

Validation: Elsa activity/module template is primary/pre-selected when available; generic .NET template remains selectable.

## ProjectFile

- `path`
- `type`: `file` or `folder`
- `size`
- `updatedAt`
- `content`

UI-only state: active file path, editor buffer, dirty flag, diagnostic markers.

State transitions: unopened -> loaded -> dirty -> saved; dirty buffers must be acknowledged before file/project/workspace switches or builds.

## BuildRequest

- `workspaceId`
- `projectId`
- `revision`

## BuildResult

- `id`
- `projectId`
- `status`: `queued`, `running`, `succeeded`, `failed`
- `startedAt`
- `finishedAt`
- `diagnostics`: `BuildDiagnostic[]`
- `artifact`: `BuildArtifact | null`

State transitions: queued -> running -> succeeded/failed. Running builds disable duplicate submission.

## BuildDiagnostic

- `severity`: `error`, `warning`, or `info`
- `message`
- `filePath`
- `line`
- `column`

Validation: Location is optional; diagnostics without a path remain visible but cannot navigate to a source location.

## BuildArtifact

- `id`
- `buildId`
- `packageId`
- `version`
- `fileName`
- `size`

Validation: Promotion is only enabled for a successful build with an artifact.

## PackagePromotionRequest

- `buildId`
- `artifactId`

## PackagePromotionResult

- `accepted`
- `category`: `duplicate`, `invalid-manifest`, `dependency-policy`, `malformed-package`, or null
- `message`
- `runtimeStatus`

State transitions: submitted -> accepted/rejected. Rejections render category-specific guidance and never imply overwrite.

## ExtensionRuntimeStatus

- `packageId`
- `version`
- `state`: `Loaded`, `PendingRestart`, or `FailedReconciliation`
- `features`: contributed runtime capabilities/activities
- `history`: promoted/loaded versions
- `diagnostics`

State transitions: promoted -> Loaded/PendingRestart/FailedReconciliation; FailedReconciliation can retry; prior versions can rollback when available.
