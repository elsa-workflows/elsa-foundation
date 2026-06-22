# Quickstart: Extension Builder Studio UI Validation

## Prerequisites

- Node/pnpm dependencies installed for the repository.
- Backend endpoint available at the Studio runtime backend base URL, or Vitest mocks for the canonical `/_elsa/extension-builder` contract.

## Narrow validation commands

```bash
pnpm --filter @elsa-workflows/studio-web test -- extension-builder
pnpm --filter @elsa-workflows/studio-web build
```

## Manual validation scenarios

1. Open `/extension-builder` as a trusted user. Expected: capabilities are requested from `GET /_elsa/extension-builder/capabilities`, workspace/project browser renders, and create/edit/build/promote/rollback affordances reflect returned flags.
2. Create a workspace and an Elsa activity/module project. Expected: the Elsa template is pre-selected when present, project name/package id/version are validated, and the new project appears in the owner-scoped browser.
3. Select a project file, edit, save, and build. Expected: file tree and editor stay in sync, dirty state is visible, build status/logs/diagnostics update without manual refresh, and running builds disable duplicate submission.
4. Select a failed diagnostic. Expected: the related file opens and the editor status points to the diagnostic line/column when provided.
5. Promote a successful artifact. Expected: accepted promotion refreshes runtime status; rejected promotion shows distinct guidance for `duplicate`, `invalid-manifest`, `dependency-policy`, or `malformed-package`.
6. Inspect runtime state. Expected: `Loaded`, `PendingRestart`, and `FailedReconciliation` states render clearly; retry reconciliation is available for failed reconciliation; rollback is available only for eligible prior versions.
