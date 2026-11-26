# Quickstart: Extension Builder Studio UI Validation

## Prerequisites

- Node/pnpm dependencies installed for the repository.
- The optional `ExtensionBuilderStudio` feature enabled in the Studio host's `shells.json` (#535); a default Studio has no Extension Builder page or bridge routes.
- The Studio host configured with `Studio:BackendServerBaseUrl` (or `Studio:BackendBaseUrl`) and `Studio:BackendModuleManagementApiKey`, and a backend exposing the canonical `/_elsa/extension-builder` contract — or Vitest mocks of the Studio bridge routes (`/_elsa/studio/backend-management/extension-builder/*`) the page actually calls.

## Narrow validation commands

```bash
pnpm --filter @elsa-workflows/studio-extension-builder test
pnpm --filter @elsa-workflows/studio-extension-builder build
dotnet test tests/Elsa.Studio.Tests --filter FullyQualifiedName~Elsa.Studio.Tests.ExtensionBuilder
```

## Manual validation scenarios

1. Open `/extension-builder` as a trusted user. Expected: capabilities are requested from the Studio bridge (`GET /_elsa/studio/backend-management/extension-builder/capabilities`, which relays to the backend's `GET /_elsa/extension-builder/capabilities` with the server-side management key), workspace/project browser renders, and create/edit/build/promote/rollback affordances reflect returned flags. Against a backend without Extension Builder the page shows its explicit "backend management unavailable" state.
2. Create a workspace and an Elsa activity/module project. Expected: the Elsa template is pre-selected when present, project name/package id/version are validated, and the new project appears in the owner-scoped browser.
3. Select a project file, edit, save, and build. Expected: file tree and editor stay in sync, dirty state is visible, `Pending`/`Running`/`Succeeded`/`Failed` build status, logs, and diagnostics update without manual refresh, and running builds disable duplicate submission.
4. Select a failed diagnostic. Expected: the related file opens and the editor status points to the diagnostic line/column when provided.
5. Promote a successful artifact. Expected: accepted promotion refreshes runtime status; rejected promotion shows distinct guidance for `Duplicate`, `InvalidManifest`, `DependencyPolicy`, or `MalformedPackage`.
6. Inspect runtime state. Expected: `Loaded`, `PendingRestart`, and `FailedReconciliation` states render clearly; retry reconciliation is available for failed reconciliation; rollback is available only for eligible prior versions.
