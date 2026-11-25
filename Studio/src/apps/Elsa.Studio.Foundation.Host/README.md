# Elsa.Studio.Foundation.Host

The bare Studio shell. It pairs with the bare server host, [`apps/Elsa.Foundation.Host`](../../../../apps/Elsa.Foundation.Host),
and is the base for custom Studio builds: a deployable Studio where every module comes from a Nuplane feed.

| Studio app | Pairs with | Compiled in |
| --- | --- | --- |
| `Elsa.Studio.Foundation.Host` (this app) | `Elsa.Foundation.Host` | The bare shell |
| [`Elsa.Studio.Workbench.Host`](../Elsa.Studio.Workbench.Host) | `Elsa.Workbench.Host` | Every Studio module |

An app decides which modules ship compiled in. It never decides which server Studio talks to: capability discovery decides which
screens show, and each module hides itself when the backend lacks its capability.

## What is compiled in

`shells.json` enables exactly these features:

| Feature | What it gives you |
| --- | --- |
| `StudioApi` | The Studio module protocol and settings API. |
| `BackendsStudio` | Backend profiles, selection and Settings → Backends. |
| `ThemeStoreCore`, `ThemePicker`, `ThemeManagement` | The Theme Store, the theme picker and theme management. |
| `FeatureManagement` | The Features page at `/features`, where you enable and configure the features of installed add-ons. |

Nuplane administration is part of the shared wiring, so the Modules page at `/modules` lists installed packages and uploads new
ones to the feed.

The shared wiring lives in [`Elsa.Studio.Hosting.Distribution`](../../essentials/Elsa.Studio.Hosting.Distribution/README.md), so
`Program.cs` passes only the FeatureManagement assembly.

## How add-ons arrive

Every other Studio module, such as Workflows, Secrets or ETL, is a Nuplane add-on. `nuplane-management.json` registers the
`studio-local-packages` directory feed over the `packages` folder beside the app, with a watcher and a one-minute poll. Add a
module in one of two ways:

- Upload its `.nupkg` on the Modules page (`/modules`), then enable its feature on the Features page (`/features`).
- Copy its `.nupkg` into `packages`. The watcher reconciles it within seconds. Then enable its feature on the Features page.

The server side of a module is a separate add-on on the paired server host. Install both: the Studio module shows its screens
once the backend advertises the capability it requires.

## Run it

```bash
dotnet run --project Studio/src/apps/Elsa.Studio.Foundation.Host/Elsa.Studio.Foundation.Host.csproj --launch-profile http
```

| URL | What it is |
| --- | --- |
| `http://localhost:5088` | This Studio (`http` profile; the `https` profile adds `https://localhost:7029`). |
| `https://localhost:7250` | The default backend, `Studio:BackendBaseUrl`: the `https` profile of `Elsa.Foundation.Host`. |

Build the Studio frontend first (`pnpm build` in `Studio`), as for any Studio app from source. `Studio:Auth:Enabled` is `true`, so
the module admin API needs a signed-in backend session; the server provides sign-in through its identity add-on. Point
`Studio__BackendBaseUrl` at another server to use it with that server instead.

The boot test, `FoundationStudioHostBootTests` in `Studio/tests/Elsa.Studio.Tests`, starts the built app as a child process and
checks the index page, the module admin routes and the compiled-in module list.
