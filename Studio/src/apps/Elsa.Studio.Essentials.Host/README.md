# Elsa.Studio.Essentials.Host

The Studio side of the workflow-essentials server. It pairs with [`apps/Elsa.Essentials.Host`](../../../../apps/Elsa.Essentials.Host)
and compiles in the Studio modules whose backend capabilities that server advertises. Every other module is a Nuplane add-on.

| Studio app | Pairs with | Compiled in |
| --- | --- | --- |
| [`Elsa.Studio.Foundation.Host`](../Elsa.Studio.Foundation.Host) | `Elsa.Foundation.Host` | The bare shell |
| `Elsa.Studio.Essentials.Host` (this app) | `Elsa.Essentials.Host` | The bare shell plus the modules the Essentials server serves |
| [`Elsa.Studio.Workbench.Host`](../Elsa.Studio.Workbench.Host) | `Elsa.Workbench.Host` | Every Studio module |

An app decides which modules ship compiled in. It never decides which server Studio talks to: capability discovery decides which
screens show, and each module hides itself when the backend lacks its capability.

## What is compiled in

`shells.json` enables exactly these features:

| Feature | What it gives you | Backend capability it needs |
| --- | --- | --- |
| `StudioApi`, `BackendsStudio` | The Studio module protocol, settings API and backend profiles. | None |
| `ThemeStoreCore`, `ThemePicker`, `ThemeManagement` | The Theme Store, the theme picker and theme management. | None |
| `FeatureManagement` | The Features page at `/features`, where you enable the features of installed add-ons. | None |
| `WorkflowsStudio` | Workflow definitions, executables, runs, activity definitions, triggers and templates. | `elsa.api.workflow-design`, `elsa.api.runtime`, `elsa.api.activity-design`, `elsa.api.publishing`, `elsa.api.standalone-triggers`, `elsa.api.expressions`, `elsa.api.activity-data-selection` |
| `WorkflowsDashboardStudio` | The workflow content on Home: the workflow task group, the core workflow widgets, and the portfolio and run-health widgets. | Portfolio and run health: `elsa.api.workflow-dashboard`. Needs attention: `elsa.api.attention`. Tasks: the UserTasks Studio module (`requiresFeature: "user-tasks"`), whose screens need `elsa.api.user-tasks`. Get started: `elsa.api.workflow-design` and `elsa.api.runtime`. Quick actions, Recent workflows and the task group: `elsa.api.workflow-design`. Recent runs: `elsa.api.runtime` |
| `AttentionStudio` | The Attention widget. | `elsa.api.attention` |
| `UserTasksStudio` | The user task inbox and forms. | `elsa.api.user-tasks` |
| `SecretsStudio` | Secrets management and the secret picker. | `elsa.api.secrets` |
| `JavaScriptExpressionEditorStudio`, `LiquidExpressionEditorStudio` | The JavaScript and Liquid expression editors. | None; the server compiles in both languages |

Home at `/` is part of the shell, so this app has it without `DashboardStudio`; the modules above fill it with widgets.

The Workflows module also carries the BPMN screens. They stay hidden here, because their capabilities (`elsa.api.bpmn-*`) come
from the BPMN server add-on.

`StudioEssentialsDriftGuardTests` in `essentials/tests/Architecture/Tests` keeps this list in step with the server: a module is
compiled in exactly when `Elsa.Essentials.Host` advertises every capability it requires. To change the set, change the server's
curated set first, then add or remove the module's `ProjectReference` in the project file, its assembly in `Program.cs` and its
entry in `shells.json`.

## What comes from the feed

Dashboards and questions (`DashboardStudio`), Agent, ETL, Decisions, Connections, external sign-in (`ExternalAuthenticationStudio`),
Weaver, the diagnostics modules (OpenTelemetry, structured logs), Extension Builder, Console Stream and the sample feature are
add-ons. Each needs its server add-on on `Elsa.Essentials.Host` as well; Extension Builder, Console Stream and the sample are
development tools (#5717).

`nuplane-management.json` registers the `studio-local-packages` directory feed over the `packages` folder beside the app, with a
watcher and a one-minute poll. Add a module in one of two ways:

- Upload its `.nupkg` on the Modules page (`/modules`), then enable its feature on the Features page (`/features`).
- Copy its `.nupkg` into `packages`. The watcher reconciles it within seconds. Then enable its feature on the Features page.

No rebuild or restart is needed. `EssentialsStudioAddOnTests` in `Studio/tests/Elsa.Studio.Tests` uses the ETL module to verify
that the package loads, the feature can be enabled, and the running app serves the module's JavaScript and stylesheets from
its verified package archive.

## Run it

```bash
dotnet run --project apps/Elsa.Essentials.Host --launch-profile https
dotnet run --project Studio/src/apps/Elsa.Studio.Essentials.Host/Elsa.Studio.Essentials.Host.csproj --launch-profile http
```

| URL | What it is |
| --- | --- |
| `http://localhost:5087` | This Studio (`http` profile; the `https` profile adds `https://localhost:7028`). |
| `https://localhost:7262` | The default backend, `Studio:BackendBaseUrl`: the `https` profile of `Elsa.Essentials.Host`. |

`Elsa.Essentials.Host` lists both Studio origins in `Cors:AllowedOrigins` and in the sign-in `AllowedReturnUrlOrigins`, so the
pair works without extra configuration. Sign in with the server's seeded development user.

Build the Studio frontend first (`pnpm build` in `Studio`), as for any Studio app from source. `Studio:Auth:Enabled` is `true`, so
the module admin API needs a signed-in backend session. Point `Studio__BackendBaseUrl` at another server to use it with that
server instead.

The boot test, `EssentialsStudioHostBootTests` in `Studio/tests/Elsa.Studio.Tests`, starts the built app as a child process and
checks the index page, the module admin routes and the compiled-in module list.

## Docker image

This app publishes as **`elsaworkflows/elsa-studio`**, the Studio image that pairs with the
`elsaworkflows/elsa-essentials-host` server image. Build it from the repository root after `pnpm build` in `Studio`:

```bash
docker build -f Studio/src/apps/Elsa.Studio.Essentials.Host/Dockerfile -t elsa-studio-essentials:local .
docker run --rm -p 8080:8080 -e Studio__BackendBaseUrl=http://localhost:13000 elsa-studio-essentials:local
```

The Dockerfile copies only this app's project closure: the shared Studio projects and the seven extension modules above.
`.github/workflows/studio-docker.yml` publishes it with the other two flavors; [Studio Docker](../../../docs/docker.md) covers
the runtime settings.

In the image, an add-on's browser files answer 503 until [valence-works/nuplane#145](https://github.com/valence-works/nuplane/issues/145) lands. Nuplane serves an installed package's files only
from an enrolled extracted store, and that store does not qualify a container's filesystem. The compiled-in modules are unaffected.
