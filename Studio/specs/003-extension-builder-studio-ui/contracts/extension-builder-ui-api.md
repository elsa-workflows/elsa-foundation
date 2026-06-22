# Contract: Extension Builder UI API Consumption

Endpoint root: `/_elsa/extension-builder`

The Studio UI consumes these backend operations through `extensionBuilderApi.ts`. The backend remains authoritative for authorization, validation, build execution, promotion, feed mutation, runtime reconciliation, and rollback.

## Capabilities

| Operation | Method/path | Response |
|-----------|-------------|----------|
| `GetCapabilities` | `GET /_elsa/extension-builder/capabilities` | `ExtensionBuilderCapabilities` |

Required advisory flags: `can-create-workspace`, `can-edit-files`, `can-build`, `can-promote`, `can-rollback`.

## Workspace and project

| Operation | Method/path |
|-----------|-------------|
| `ListWorkspaces` | `GET /_elsa/extension-builder/workspaces` |
| `CreateWorkspace` | `POST /_elsa/extension-builder/workspaces` |
| `GetWorkspace` | `GET /_elsa/extension-builder/workspaces/{workspaceId}` |
| `DeleteWorkspace` | `DELETE /_elsa/extension-builder/workspaces/{workspaceId}` |
| `ListTemplates` | `GET /_elsa/extension-builder/templates` |
| `CreateProject` | `POST /_elsa/extension-builder/workspaces/{workspaceId}/projects` |
| `GetProject` | `GET /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}` |
| `DeleteProject` | `DELETE /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}` |

UI requirements: browser rows show workspace/project owner scope, package id/version, latest build status, and runtime status. Project creation validates name/package id/version before submission.

## Files

| Operation | Method/path |
|-----------|-------------|
| `ListProjectFiles` | `GET /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/files` |
| `ReadProjectFile` | `GET /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/files/{path}` |
| `WriteProjectFile` | `PUT /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/files/{path}` |
| `DeleteProjectFile` | `DELETE /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/files/{path}` |

UI requirements: create/edit/delete affordances are gated by `can-edit-files`; dirty editor buffers are explicit and protected from silent loss.

## Build

| Operation | Method/path |
|-----------|-------------|
| `SubmitBuild` | `POST /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/builds` |
| `GetBuild` | `GET /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/builds/{buildId}` |
| `GetBuildLog` | `GET /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/builds/{buildId}/log` |
| `GetBuildArtifact` | `GET /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/builds/{buildId}/artifact` |

UI requirements: `queued` and `running` builds poll for status/log updates; duplicate build submission is disabled while a build is active.

## Promote and runtime

| Operation | Method/path |
|-----------|-------------|
| `PromoteBuild` | `POST /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/builds/{buildId}/promote` |
| `GetRuntimeStatus` | `GET /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/runtime-status` |
| `RollbackPackage` | `POST /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/runtime-status/rollback` |
| `RetryReconciliation` | `POST /_elsa/extension-builder/workspaces/{workspaceId}/projects/{projectId}/runtime-status/retry-reconciliation` |

UI requirements: promotion is gated by `can-promote`; rollback is gated by `can-rollback`; rejection categories render distinct guidance for `duplicate`, `invalid-manifest`, `dependency-policy`, and `malformed-package`; runtime states render `Loaded`, `PendingRestart`, and `FailedReconciliation` distinctly.
