# Make a visible Studio source edit

This walkthrough pairs a source-built Elsa Foundation Studio with a source-built Foundation Workbench. Use disposable checkouts for both repositories. The backend guide covers Workbench startup, its local Development seed, and its own workflow smoke test: [Run the backend from source](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/contributing/backend-source-quickstart.md).

For an optional setup that pairs source-built Studio with a published Workbench image, see [Use a packaged Workbench backend](studio-packaged-backend.md).

The commands below describe the tested macOS/Bash setup; other operating systems have not been verified here.

The clone commands follow each repository's current default branch. The test and browser evidence below applies only to the exact Studio and Foundation revisions recorded in the evidence section; verify later source pairs separately.

## Prerequisites and setup

Use Git, Node.js 22, the pnpm version pinned by Studio (`pnpm@11.9.0`), and a .NET 10 SDK. Start with a disposable Studio checkout, or use an existing disposable checkout/worktree:

```bash
git clone https://github.com/elsa-workflows/elsa-foundation-studio.git elsa-foundation-studio-contributor
cd elsa-foundation-studio-contributor
```

From the Studio repository root, install exactly from the lockfile:

```bash
node --version
pnpm --version
dotnet --version
pnpm install --frozen-lockfile
```

Keep the Foundation Workbench and Studio in separate terminals. Follow the Foundation guide to build and start its Development/SQLite HTTP profile at `http://localhost:5095`, then check `http://localhost:5095/health/ready`. That guide documents the disposable local Development seed (`admin` / `Password123!`); do not use it outside a local Development host.

Use these separate browser and backend addresses:

| Address | Role and source configuration |
|---|---|
| `http://localhost:5089` | Open this Studio page in the browser. It is the Studio Web `http` launch profile. The Workbench Development host's default CORS origins include this Studio origin. |
| `http://localhost:5095` | Foundation Workbench HTTP profile and browser-facing backend API. Set Studio's `Studio:BackendBaseUrl` to this address; Studio's default is `https://localhost:7243`. The Workbench CORS policy reads `Cors:AllowedOrigins`, whose default includes `http://localhost:5089`. |

Studio exposes `Studio:BackendBaseUrl` to its browser app. Server-side Studio management calls can instead use `Studio:BackendServerBaseUrl` when it is set; otherwise they fall back to `Studio:BackendBaseUrl`. This local pair uses the one browser-facing URL and does not configure the separate server-side override. If you change the Studio origin, the Workbench CORS allowed-origin list must include that exact origin.

From the Studio repository root, run:

```bash
pnpm --filter @elsa-workflows/studio-dashboard test
pnpm --filter @elsa-workflows/studio-dashboard typecheck
pnpm build
dotnet build src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj
Studio__BackendBaseUrl=http://localhost:5095 dotnet run --no-build --project src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj --launch-profile http
```

Open `http://localhost:5089` in an ordinary browser and sign in with the local seed from the Foundation guide. Keep both hosts running while using Studio. An ordinary browser is enough for this manual walkthrough. `pnpm install` installs the Playwright package for automated browser checks, but does not install the Chromium browser binary or operating-system dependencies; CI installs those separately with `pnpm exec playwright install --with-deps chromium` before `pnpm test:browser` in [the CI workflow](../../.github/workflows/ci.yml).

## Exercise a visible edit

The Dashboard page is registered at `/`, `/dashboard`, and `/overview`. Its empty state renders the `Dashboard` heading. First change only the existing empty-state heading expectation in `src/essentials/Elsa.Studio.Dashboard/Client/src/__tests__/DashboardPage.test.tsx` from `Dashboard` to `Your first Studio change`.

Run the focused test and observe the expected failure while the component still renders `Dashboard`:

```bash
pnpm --filter @elsa-workflows/studio-dashboard test src/__tests__/DashboardPage.test.tsx -t 'renders its empty state when no module contributes a widget'
```

Now change the visible heading in `src/essentials/Elsa.Studio.Dashboard/Client/src/DashboardPage.tsx` to `Your first Studio change` and rerun the same focused test; it should pass. To make this module edit visible, rebuild its Vite assets and the Studio host, then restart only the Studio process you started:

```bash
pnpm --filter @elsa-workflows/studio-dashboard build
dotnet build src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj
```

The Dashboard Vite build writes its module assets under `src/essentials/Elsa.Studio.Dashboard/wwwroot/studio/modules/dashboard`. Rebuilding and restarting is the verified source-edit path; do not assume hot reload for this asset.

After restarting Studio, reload `http://localhost:5089` and confirm the Dashboard heading reads `Your first Studio change` before restoring the exercise. After restoration, rebuild and restart again, reload the page, and confirm the default `Dashboard` heading returns.

To return to the default Dashboard, restore both the component heading and test expectation to `Dashboard`, rerun the focused test, build the Dashboard assets and Studio host with the commands above, and restart your own Studio process. Stop only the Workbench and Studio processes you started, with Ctrl+C in their terminals.

## Run a workflow in the browser

With the source-built hosts running and signed in:

1. Open **Workflow Definitions**, choose **Create**, leave the fixed default **Flowchart** selected, enter `Contributor source journey 583`, and click **Create** in the dialog.
2. Add one **Write Line** activity with a single click and set its text to `Hello from the contributor source journey 583`.
3. Wait for save/autosave and validation to finish.
4. Choose **Review & publish**, select the default publication channel, and choose **Publish**. The observed publication was version `1.0.0`.
5. Open the published executable. This opens **Artifacts**; choose **Run executable**.
6. Open the new run and confirm **Completed**, the **Write Line** activity completed, and **No active incidents**. Confirm the expected `Hello from the contributor source journey 583` text in the Workbench terminal output; it is not displayed as run-detail text.

This exact source-pair journey was completed on the revisions and platform below; it is also tracked under [Studio issue #583](https://github.com/elsa-workflows/elsa-foundation-studio/issues/583). Stop only the two hosts you started after inspecting the run.

## Evidence and remaining checks

On the Studio source revision `2eabb6dc7112211b141d129f8849165f1816cbd7`, the existing empty-state test was observed failing after changing only its expected heading to `Your first Studio change`. After changing the visible component heading to that text, the Dashboard suite passed 20 tests and its typecheck passed. The frozen install reused all 742 packages from the existing pnpm store. The workspace build and scoped Studio.Web build also passed on macOS with Node `v22.22.1`, pnpm `11.9.0`, and .NET SDK `10.0.300`.

The local browser showed the edited heading at `http://localhost:5089`; an incorrect password produced `Invalid username or password`, and signing in with the documented Development seed opened Studio. The completed browser journey created and published `Contributor source journey 583`, ran it, and inspected a `Completed` run whose **Write Line** activity completed with no active incidents. Its expected stdout appeared in the Workbench terminal. This is a local source-pair result, not a claim that every Dashboard panel had runtime data: Attention still showed incomplete workflow runtime data and the optional WeatherForecast sample was unavailable.

Observed recovery paths on this source pair:

| Failure introduced | Observed signal | Recovery observed |
|---|---|---|
| Incorrect password | Login displayed `Invalid username or password`. | Signed in with the documented local Development seed and reached Dashboard. |
| Workbench stopped | Studio displayed `Unable to sign in` and `The authentication service may be unavailable. Check the backend, then try again.` | Restarted the same Workbench checkout/profile, waited for readiness, selected **Try again**, signed in with the local seed, and reached Dashboard. |
| Studio configured for free port 5096 while Workbench remained on 5095 | Studio displayed `Unable to sign in` and the authentication-service-unavailable message. | Restarted the owned Studio host with `Studio__BackendBaseUrl=http://localhost:5095`; Dashboard returned and Studio showed backend API `localhost:5095`. |
| Source/test restored while generated bundle still held the exercise heading | Browser reload continued to show `Your first Studio change`. | Rebuilt the Dashboard assets and Studio host, then restarted Studio. Reload showed the restored `Dashboard` heading and backend API `localhost:5095`. |

The source pair used Studio revision `2eabb6dc7112211b141d129f8849165f1816cbd7` and Foundation revision `592d6c0eb2a1ae45563ce5b234fb4e97af2867ed` on macOS 26.6 arm64. After rebuilding the restored assets and host, the root agent confirmed that the browser returned to the default `Dashboard` heading with backend API `localhost:5095`. A different Studio/Workbench version pairing, a fresh operating-system install, and a first-time contributor trial remain outside this evidence.
