# Quickstart: Extension Builder Backend

## Prerequisites

- .NET SDK matching the repo target framework.
- `Elsa.Server` configured with `Elsa:ModuleManagement:ApiKey`.
- Nuplane directory feed configured in `appsettings.json`/`shells.json` as used by the existing module-management API.

## Run the backend

```bash
dotnet run --project src/Apps/Elsa.Server/Elsa.Server.csproj
```

Use the configured management key on all requests:

```bash
export ELSA_KEY='<configured-management-key>'
```

## Validate capabilities and templates

```bash
curl -H "X-Elsa-Module-Management-Key: $ELSA_KEY" \
  http://localhost:5000/_elsa/extension-builder/capabilities

curl -H "X-Elsa-Module-Management-Key: $ELSA_KEY" \
  http://localhost:5000/_elsa/extension-builder/templates
```

Expected outcome: capabilities include `can-create-workspace`, `can-edit-files`, `can-build`, `can-promote`, and `can-rollback`; templates include the Elsa activity/module template and a generic .NET template.

## Validate author/build/promote loop

1. Create a workspace with `POST /_elsa/extension-builder/workspaces`.
2. Create an Elsa activity/module project with `POST /_elsa/extension-builder/workspaces/{workspaceId}/projects`.
3. Confirm starter files with `GET /_elsa/extension-builder/projects/{projectId}/files`.
4. Submit a build with `POST /_elsa/extension-builder/projects/{projectId}/builds`.
5. Poll `GET /_elsa/extension-builder/builds/{buildId}` until status is `Succeeded`.
6. Retrieve the log and artifact from `/log` and `/artifact`.
7. Promote with `POST /_elsa/extension-builder/builds/{buildId}/promote`.
8. Check runtime status with `GET /_elsa/extension-builder/projects/{projectId}/runtime-status`.

Expected outcome: the unmodified Elsa template builds successfully, promotion publishes a `.nupkg` into the Nuplane-loadable feed, reconciliation is reported, and runtime status maps the promoted package to `Loaded`, `PendingRestart`, or `FailedReconciliation`.

## Validate diagnostics

1. Write invalid C# using `PUT /_elsa/extension-builder/projects/{projectId}/files/{path}`.
2. Submit another build.
3. Read the build result and log.

Expected outcome: the build status is `Failed`, includes at least one error diagnostic where the compiler provides location data, and exposes no promotable artifact.

## Validate promotion rejection

Promote the same successful build twice.

Expected outcome: the second promotion is rejected with rejection reason `duplicate`, and the existing feed package is left unchanged. Additional malformed package, invalid manifest, and dependency policy cases are covered by focused tests.

## Validate rollback and retry

1. Promote versions N and N+1 for a project.
2. Call `POST /_elsa/extension-builder/projects/{projectId}/rollback` with version N.
3. Call `POST /_elsa/extension-builder/projects/{projectId}/retry-reconcile`.

Expected outcome: rollback activates the requested available version and retry returns a reconciliation outcome with reload/restart flags.
