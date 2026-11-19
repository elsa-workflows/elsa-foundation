# Run Studio from source

This walkthrough runs Elsa Studio from the `Studio/` tree in this Foundation repository and pairs it with a source-built Workbench from the same checkout. Do not clone `elsa-workflows/elsa-foundation-studio` for current `main`; that separate repository is no longer the contributor path.

The backend guide covers Workbench startup, its local Development seed, and its own workflow smoke test: [Run the backend from source](../../../docs/contributing/backend-source-quickstart.md).

For an optional setup that pairs source-built Studio with a published Workbench image, see [Use a packaged Workbench backend](studio-packaged-backend.md). That recipe is a recorded image pair, not this source-pair walkthrough.

The commands below were executed on Linux/amd64 at Foundation `6adeeaa0aa731b8b62893f23d1d9d8e9519cd2fa`. Other operating systems were not verified in this pass.

## Prerequisites

- Git.
- Node.js 22 or later. This pass used `v22.23.4`.
- The pnpm version pinned by Studio [`package.json`](../../package.json) (`packageManager`: `pnpm@11.9.0`), provided by Corepack.
- A stable .NET 10 SDK. The repository [`global.json`](../../../global.json) sets the SDK floor at `10.0.300` with `latestFeature` roll-forward. From the repository root, `dotnet --version` prints the SDK selected for this checkout. This pass installed `10.0.300` because no SDK was on `PATH`.
- Network access to npm (for pnpm and Studio packages) and to the public package sources listed in the committed [`NuGet.config`](../../../NuGet.config).

Set `COREPACK_ENABLE_DOWNLOAD_PROMPT=0` before the first Studio `pnpm` command so Corepack can download the pinned `pnpm@11.9.0` without a confirmation prompt. On this pass, Corepack 0.36.0 printed `! Corepack is about to download https://registry.npmjs.org/pnpm/-/pnpm-11.9.0.tgz` when the variable was unset. With a TTY it also asked `? Do you want to continue? [Y/n]`. With the variable set, `pnpm --version` printed `11.9.0` and did not prompt.

Use one Foundation checkout for both hosts. An existing disposable checkout or Git worktree of this repository is enough; Studio is `Studio/` inside it.

```bash
git clone https://github.com/elsa-workflows/elsa-foundation.git elsa-foundation-contributor
cd elsa-foundation-contributor
```

## Check versions

From the repository root:

```bash
export COREPACK_ENABLE_DOWNLOAD_PROMPT=0
node --version
dotnet --version
(cd Studio && pnpm --version)
```

Expect Node 22 or later, a .NET 10.0 SDK at `10.0.300` or a later compatible feature-band SDK, and `11.9.0` from the Studio directory (Corepack selects the pin even when another pnpm version is on `PATH`).

## Build and start Workbench

Keep Workbench and Studio in separate terminals. From the repository root, build the focused backend project:

```bash
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true
```

Start the HTTP launch profile in one terminal and leave it running:

```bash
dotnet run --no-build --project Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
```

This Development/SQLite profile listens at `http://localhost:5095`. In a second terminal, check readiness:

```bash
curl -fsS http://localhost:5095/health/ready
```

The response should include `"status":"ready"`. If port 5095 is occupied, follow the [backend alternate-port instructions](../../../docs/contributing/backend-source-quickstart.md#port-already-in-use) and use that URL as `Studio__BackendBaseUrl` below.

The Workbench Development host's default CORS origins include `http://localhost:5089`. That guide documents the disposable local Development seed (`admin` / `Password123!`); do not use it outside a local Development host.

## Build and start Studio

Use these separate browser and backend addresses:

| Address | Role and source configuration |
|---|---|
| `http://localhost:5089` | Open this Studio page in the browser. It is the Studio Web `http` launch profile. The Workbench Development host's default CORS origins include this Studio origin. |
| `http://localhost:5095` | Foundation Workbench HTTP profile and browser-facing backend API. Set Studio's `Studio:BackendBaseUrl` to this address; Studio's default in `Studio/src/apps/Elsa.Studio.Web/appsettings.json` is `https://localhost:7243`. |

Studio exposes `Studio:BackendBaseUrl` to its browser app. Server-side Studio management calls can instead use `Studio:BackendServerBaseUrl` when it is set; otherwise they fall back to `Studio:BackendBaseUrl`. This local pair uses the one browser-facing URL and does not configure the separate server-side override. If you change the Studio origin, the Workbench CORS allowed-origin list must include that exact origin.

From the repository root, with `COREPACK_ENABLE_DOWNLOAD_PROMPT=0` still set:

```bash
cd Studio
pnpm install --frozen-lockfile
pnpm build
cd ..
dotnet build Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj
Studio__BackendBaseUrl=http://localhost:5095 dotnet run --no-build --project Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj --launch-profile http
```

When Studio is listening, `/studio-runtime.js` should include `"backendBaseUrl":"http://localhost:5095"`:

```bash
curl -fsS http://localhost:5089/studio-runtime.js
```

Open `http://localhost:5089` in an ordinary browser. Studio redirects to the Workbench identity login at `http://localhost:5095/_elsa/identity/login` with a return URL back to Studio. Sign in with the local seed from the Foundation guide. After sign-in, Studio should show its workspace (Home) and a backend selector of `localhost:5095`. Keep both hosts running while using Studio.

These ports and project paths match `tools/ux-qa/setup.sh`. That script is the UX QA routine's scripted pair; this walkthrough did not invoke it. It also installs Playwright Chromium for that routine, which this manual path does not need.

## Evidence (Linux/amd64, this pass)

On Foundation `6adeeaa0aa731b8b62893f23d1d9d8e9519cd2fa` (Linux 6.12.113+ x86_64), with Node `v22.23.4`, Corepack `0.36.0`, pnpm `11.9.0`, and .NET SDK `10.0.300`:

- Workbench locked-mode build completed with 0 errors.
- `pnpm install --frozen-lockfile` in `Studio/` completed with pnpm `11.9.0` (745 packages added).
- `pnpm build` in `Studio/` completed.
- `dotnet build Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj` completed with 0 warnings and 0 errors.
- `GET http://localhost:5095/health/ready` returned `"status":"ready"`.
- `GET http://localhost:5089/studio-runtime.js` returned `backendBaseUrl` `http://localhost:5095`.
- CORS preflight and POST from Origin `http://localhost:5089` to Workbench login were allowed (`Access-Control-Allow-Origin: http://localhost:5089`).
- Browser sign-in with the documented Development seed reached Home at `http://localhost:5089/?backend=legacy`. The UI showed **Default backend: localhost:5095** and user **Administrator**. **Workflow definitions** opened and showed the empty All workflows list. No authentication-unavailable banner.

Limits of this pass:

- The walkthrough used an existing Foundation checkout at `origin/main`, not a freshly recorded `git clone`.
- PowerShell was not on `PATH`, so the backend `pwsh` workflow smoke script was not run.
- The Dashboard heading edit, focused Dashboard tests, and the in-browser create/publish/run journey from the earlier macOS source-pair were not repeated.
- `tools/ux-qa/setup.sh` was read for paths and ports and was not executed.
- A fresh operating-system install and a first-time human contributor trial were not performed.

Stop only the Workbench and Studio processes you started, with Ctrl+C in their terminals.

## Earlier recorded source-edit exercise

The following Dashboard heading edit and in-browser Write Line journey were last fully exercised on macOS 26.6 arm64 with Studio revision `2eabb6dc7112211b141d129f8849165f1816cbd7` and Foundation revision `592d6c0eb2a1ae45563ce5b234fb4e97af2867ed`. The file paths still exist under `Studio/` in this repository. They were **not** re-run on the Linux/amd64 SHA above.

The Dashboard page is registered at `/`, `/dashboard`, and `/overview`. Its empty state renders the `Dashboard` heading. From the `Studio/` directory, first change only the existing empty-state heading expectation in `src/essentials/Elsa.Studio.Dashboard/Client/src/__tests__/DashboardPage.test.tsx` from `Dashboard` to `Your first Studio change`.

Run the focused test and observe the expected failure while the component still renders `Dashboard`:

```bash
pnpm --filter @elsa-workflows/studio-dashboard test src/__tests__/DashboardPage.test.tsx -t 'renders its empty state when no module contributes a widget'
```

Now change the visible heading in `src/essentials/Elsa.Studio.Dashboard/Client/src/DashboardPage.tsx` to `Your first Studio change` and rerun the same focused test; it should pass. To make this module edit visible, rebuild its Vite assets and the Studio host, then restart only the Studio process you started:

```bash
pnpm --filter @elsa-workflows/studio-dashboard build
dotnet build src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj
```

The Dashboard Vite build writes its module assets under `src/essentials/Elsa.Studio.Dashboard/wwwroot/studio/modules/dashboard`. Rebuilding and restarting is the recorded source-edit path; do not assume hot reload for this asset.

After restarting Studio, reload `http://localhost:5089` and confirm the Dashboard heading reads `Your first Studio change` before restoring the exercise. After restoration, rebuild and restart again, reload the page, and confirm the default `Dashboard` heading returns.

On that earlier macOS pair, the completed browser journey created and published `Contributor source journey 583`, ran it, and inspected a `Completed` run whose **Write Line** activity completed with no active incidents. That journey is also tracked under [Studio issue #583](https://github.com/elsa-workflows/elsa-foundation-studio/issues/583).
