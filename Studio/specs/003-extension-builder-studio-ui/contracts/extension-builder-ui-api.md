# Contract: Extension Builder UI API Consumption

Endpoint root: `/_elsa/extension-builder`

The Studio UI consumes these backend operations through `extensionBuilderApi.ts`. The backend remains authoritative for authorization, validation, build execution, promotion, feed mutation, runtime reconciliation, and rollback.

## Capabilities

| Operation | Method/path | Response |
|-----------|-------------|----------|
| `GetCapabilities` | `GET /_elsa/extension-builder/capabilities` | `ExtensionBuilderCapabilities` |

Required advisory flags: `canCreateWorkspace`, `canEditFiles`, `canBuild`, `canPromote`, `canRollback`.

## Workspace and project

| Operation | Method/path |
|-----------|-------------|
| `ListWorkspaces` | `GET /_elsa/extension-builder/workspaces` |
| `CreateWorkspace` | `POST /_elsa/extension-builder/workspaces` |
| `GetWorkspace` | `GET /_elsa/extension-builder/workspaces/{workspaceId}` |
| `DeleteWorkspace` | `DELETE /_elsa/extension-builder/workspaces/{workspaceId}` |
| `ListTemplates` | `GET /_elsa/extension-builder/templates` |
| `CreateProject` | `POST /_elsa/extension-builder/workspaces/{workspaceId}/projects` |
| `GetProject` | `GET /_elsa/extension-builder/projects/{projectId}` |
| `DeleteProject` | `DELETE /_elsa/extension-builder/projects/{projectId}` |

UI requirements: browser rows show workspace/project owner scope, package id/version, latest build status, and runtime status. Project creation validates name/package id/version before submission.

## Files

| Operation | Method/path |
|-----------|-------------|
| `ListProjectFiles` | `GET /_elsa/extension-builder/projects/{projectId}/files` |
| `ReadProjectFile` | `GET /_elsa/extension-builder/projects/{projectId}/files/{path}` |
| `WriteProjectFile` | `PUT /_elsa/extension-builder/projects/{projectId}/files/{path}` |
| `DeleteProjectFile` | `DELETE /_elsa/extension-builder/projects/{projectId}/files/{path}` |

UI requirements: create/edit/delete affordances are gated by `canEditFiles`; dirty editor buffers are explicit and protected from silent loss.

## Build

| Operation | Method/path |
|-----------|-------------|
| `SubmitBuild` | `POST /_elsa/extension-builder/projects/{projectId}/builds` |
| `GetBuild` | `GET /_elsa/extension-builder/builds/{buildId}` |
| `GetBuildLog` | `GET /_elsa/extension-builder/builds/{buildId}/log` |
| `GetBuildArtifact` | `GET /_elsa/extension-builder/builds/{buildId}/artifact` |

UI requirements: `SubmitBuild` is project-scoped and the backend snapshots the current project sources; `Pending` and `Running` builds poll for status/log updates; duplicate build submission is disabled while a build is active. Artifact metadata is read from `BuildResult.Artifact`; `GetBuildArtifact` is a package-file download endpoint and is not required for promotion gating.

## Promote and runtime

| Operation | Method/path |
|-----------|-------------|
| `PromoteBuild` | `POST /_elsa/extension-builder/builds/{buildId}/promote` |
| `GetRuntimeStatus` | `GET /_elsa/extension-builder/projects/{projectId}/runtime-status` |
| `RollbackPackage` | `POST /_elsa/extension-builder/projects/{projectId}/rollback` |
| `RetryReconciliation` | `POST /_elsa/extension-builder/projects/{projectId}/retry-reconcile` |

UI requirements: promotion is gated by `canPromote`; rollback is gated by `canRollback`; rejection categories render distinct guidance for `Duplicate`, `InvalidManifest`, `DependencyPolicy`, and `MalformedPackage`; runtime states render `Loaded`, `PendingRestart`, and `FailedReconciliation` distinctly.
