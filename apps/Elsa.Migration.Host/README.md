# Elsa 3 migration host

**Temporary local migration tool. Don't deploy it.** It is removed by the follow-up cleanup on
[#5800](https://github.com/elsa-workflows/elsa-foundation/issues/5800), once a Docker app profile covers the Elsa 3
import story. Spec: [1236](../../specs/1236-elsa3-migration-host/spec.md).

The host runs the Elsa 4 workflows the migration CLI (spec 1237,
[Elsa 3 workflows migration tooling](../../extensions/Elsa3/src/Migration/Tooling/README.md)) writes to a `migrated/` folder. It composes only the
features those workflows, the JTest acceptance set and the Studio designer need, plus JSON workflow and activity
reconciliation. It is a plain CShells host: it finds its features in the projects it references, loads no packages
at runtime, and has no admin or module-management endpoints. It carries no Elsa 3 code.

## Run it

From the repository root (or any folder):

```bash
dotnet run --project apps/Elsa.Migration.Host
```

- URL: `http://localhost:5096` (fixed, from `Properties/launchSettings.json`).
- Ready when `GET http://localhost:5096/health/ready` returns 200. Until then, or when the log has a line
  `could not activate shell 'default'`, the host is not ready and that line names the feature, setting or file at fault.
- The sample workflow answers `GET http://localhost:5096/workflows/http/hello`.
- `dotnet run` runs in this project folder, so the relative sample paths and `data/` resolve here. Started any other
  way (the built assembly, an IDE profile of your own), set `ASPNETCORE_ENVIRONMENT=Development` and pass absolute
  folder paths: without Development the identity features refuse to run in development mode and the shell doesn't
  activate.
- Data is SQLite in `data/elsa.db` (git-ignored), migrated at startup. Delete `data/` to start from an empty store.

## Point it at your migrated folder

The CLI writes `migrated/workflows/` (flat, one JSON workflow catalog per file) and
`migrated/activities/activities.json` (`[]` when there are no reusables). Two settings name them; both default to the
sample folder in this project:

| Setting | Default |
|---|---|
| `CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FolderPath` | `migrated/workflows` |
| `CShells:Shells:default:Features:JsonActivityReconciliation:Options:FilePath` | `migrated/activities/activities.json` |

Set them, with absolute paths, in any of these (lowest first). Settings in `appsettings*.json` sit below `shells.json`
and can't change them.

1. A git-ignored `shells.Local.json` next to `shells.json`:

   ```json
   {
     "CShells": { "Shells": { "default": { "Features": {
       "JsonWorkflowReconciliation": { "Options": { "FolderPath": "C:/work/acme/migrated/workflows" } },
       "JsonActivityReconciliation": { "Options": { "FilePath": "C:/work/acme/migrated/activities/activities.json" } }
     } } } }
   }
   ```

2. User secrets: `dotnet user-secrets set "CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FolderPath" "C:/work/acme/migrated/workflows" --project apps/Elsa.Migration.Host`.
3. Environment variables: `CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath`.
4. The command line: `dotnet run --project apps/Elsa.Migration.Host -- --CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FolderPath=C:/work/acme/migrated/workflows`.

Every definition in the workflows folder is imported and published at startup (`PublishOnReconcile`; set it to
`false` to import only). A definition that fails to publish is logged and the others still run. A missing folder or
activities file, or a file that can't be read, keeps the host from becoming ready and the log names it.

JSON workflow reconciliation also takes a `Files` list instead of `FolderPath` (exactly one of `FilePath`, `Files` or
`FolderPath`); its [README](../../essentials/src/Workflows/Design/Reconciliation/Json/README.md) has the shape. To
use `Files`, set `FolderPath` to an empty string in the same source.

## Add your own activities

In your clone, add a project reference to your feature project in `Elsa.Migration.Host.csproj` (restore updates
`packages.lock.json`), and list its feature in a local source, for example `shells.Local.json`:

```json
{ "CShells": { "Shells": { "default": { "Features": { "AcmeActivities": {} } } } } }
```

A listed feature that no referenced project provides keeps the host from becoming ready, and the log names it.

## Sign in

Development identity with the seeded administrator from `shells.json` (`admin` / `Password123!`). Local use only.

- Cookie sign-in: `POST /_elsa/identity/login`.
- Bearer token: `GET /_elsa/identity/token` with the sign-in cookie.
- The runtime API is under `/runtime/workflows/*` (executables, instances, activity executions, diagnostics settings);
  HTTP triggers are under `/workflows/http/*`.

## Studio

Use the repository's essentials Studio host, `Studio/src/apps/Elsa.Studio.Essentials.Host`, pointed at this host:

```bash
dotnet run --project Studio/src/apps/Elsa.Studio.Essentials.Host --launch-profile http -- --Studio:BackendBaseUrl=http://localhost:5096
```

Open `http://localhost:5087` and sign in with the development administrator. CORS and the identity return URLs of
this host allow that Studio host's two local addresses (`http://localhost:5087`, `https://localhost:7028`). Build the Studio
frontend first (`pnpm build` in `Studio`), as for any Studio app from source.

This host doesn't advertise `elsa.api.standalone-triggers`, `elsa.api.workflow-dashboard`, `elsa.api.attention` or
`elsa.api.secrets`, so Studio hides the standalone triggers screens, the portfolio, run-health and needs-attention widgets,
the Attention widget and Secrets.

The definitions list shows the imported definitions. At startup the host gives each imported definition that has no
draft one, cloned from its newest imported version, so Studio opens it in the designer. Edit it there and publish:
the host runs the new version, and it keeps running it after a restart, because reconciliation skips a version from
`migrated/` that is older than the newest stored one. To change a workflow from your `migrated/` folder instead,
give it a `version` newer than any version published from Studio (a stored version never changes) and restart the
host. The designer then shows the new version, unless you saved the draft in Studio: a saved draft is kept, so a
restart never throws away your edits.

## What your own host needs

[`composition-reasons.json`](composition-reasons.json) lists every feature in [`shells.json`](shells.json) with the
reason it is there: a JTest suite or template, the S-15 run configuration, the Studio designer, the migrated folder,
another feature that requires it, or what the host runs on. Use it as the starting point for your own Elsa 4 host.
The GA composition (a thin base, essentials profiles, images) is
[#5800](https://github.com/elsa-workflows/elsa-foundation/issues/5800).

Not composed: BPMN, Weaver, agents, decisions, connections, dashboards, Secrets, Studio preferences, standalone trigger
authoring, OpenTelemetry and structured logs, compensation, attention, module management and Liquid. Add one as above
when your workflows need it.
