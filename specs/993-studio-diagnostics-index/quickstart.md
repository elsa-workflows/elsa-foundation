# Validation Guide: Studio Diagnostics Index

Run focused checks from the `Studio/` directory after the RED assertions are added and the implementation is ready. The commands below define the required checks; baseline results are recorded separately.

## Prerequisites

- Use the existing workspace versions. OpenTelemetry declares the same `react-dom` and `@types/react-dom` test dev dependencies as Structured Logs; after the offline lockfile update, verify installation with `pnpm install --offline --frozen-lockfile` from `Studio/`. No runtime dependency or new library version is added.
- Keep the baseline comparison at the approved product source revision, `e656cc6042fd86d15d61c7278d846222054eb535`.
- Use stubbed API responses for component tests so page-state coverage is deterministic.
- For the Playwright fixture, use the actual Studio host/navigation components in the bounded Vite fixture. The fixture does not boot Elsa Workbench or prove a composed host's module registration, session, or backend.

## Focused checks

```bash
cd Studio
pnpm --filter @elsa-workflows/studio-web exec vitest run src/__tests__/overview.test.tsx src/__tests__/navPlacement.test.ts src/__tests__/app-routing.test.ts
pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry exec vitest run src/__tests__/module.test.ts
pnpm --filter @elsa-workflows/studio-diagnostics-structured-logs exec vitest run src/__tests__/module.test.ts
pnpm --filter @elsa-workflows/studio-workflows exec vitest run src/__tests__/runtimeDiagnosticsSettings.test.tsx
pnpm exec playwright test tests/browser/diagnostics-index.spec.ts
```

## Expected evidence

| Check | Expected result |
|---|---|
| Host Diagnostics | Only registered entries that survive the real host access filter are shown. Visible labels and links match the current navigation destinations. A synthetic gated custom entry is omitted when the host filter omits it. Built-ins have accurate task descriptions; a custom entry without task semantics has neutral copy. The empty link message does not imply outage or empty data. Widgets continue to render and isolate failure. |
| Normal and baseline registrations | Check the Studio module manifest separately from backend shell composition. The normal Studio `shells.json` enables OpenTelemetry and Structured Logs; baseline absence uses an isolated copy with only `OpenTelemetryStudio` and `StructuredLogsStudio` omitted from `CShells:Shells:Default:Features`, paired with backend `shells.baseline.json`. Confirm both modules are disabled in the client manifest before asserting that the index omits them. A backend-only baseline tests missing backend support while the frontend routes may remain registered. Runtime evidence settings retains `/workflows/runtime-diagnostics`; listed routes match sidebar/dock destinations where present. |
| OpenTelemetry page DOM | Trace states remain distinct for loading, successful empty, permission, unavailable, and failure responses. Metrics and OpenTelemetry Logs are clearly unavailable on this page before any placeholder can be entered, or the selectable affordances are omitted with an explanation. A direct Structured Logs link appears only when that page can confirm the destination is registered and accessible; otherwise the handoff returns to Diagnostics with neutral wording. |
| Structured Logs page DOM | Loading, empty, permission, unavailable, and failure states are visibly distinct and remain owned by Structured Logs. |
| Runtime evidence settings | View-only users still find and read the page, cannot save, and produce no PUT after attempted edits. Authorized users retain the exact existing save route and payload. No policy or permission changes occur. |
| Browser fixture | Every visible destination is keyboard reachable. At 320, 768, 1024, and 1440 CSS-pixel widths in light and dark themes, descriptions and focus remain usable with no horizontal overflow or clipped content. |

The task-provided untouched-product baseline passed offline installation with the unchanged lockfile and reports Web 563 tests across 49 files, OpenTelemetry 5 tests in 1 file, Structured Logs 10 tests in 1 file, and Workflows 2,124 tests across 167 files. Those counts are comparison baselines, not proof of the new DOM states or composition behavior.

## Composed Workbench comparison

This is a separate, required gate. The recipe below runs both compositions from copied configuration under one fresh private root, with private databases, key rings and empty Nuplane package feeds. It does not edit tracked or user configuration. The build steps use the configured pnpm/NuGet caches and may populate them. Use a fresh browser context. The two QA allocations are `127.0.0.1:5695` (Workbench) and `127.0.0.1:5689` (Studio); if either is already occupied, stop and have the QA owner assign an available pair. Do not stop an unknown listener.

The previous root-run before/after qualification and cleanup were recorded outside this repository. Those results are historical evidence for that run; they are not setup instructions and do not validate a later source revision. Root checked the portable snippets' syntax, generated both private configurations, verified their paths and baseline feature omission, and exercised guarded cleanup. The portable launch commands have not been rerun; they do not establish live qualification of a later revision. For a before/after comparison, point `REPO` at the approved base checkout for the before run and at the target checkout for the after run, preparing a new `RUN` each time. Never reuse either profile's database between source revisions.

### Build the current checkout

Run these only when current build outputs are not already available. Keep builds serial and use the repository's build-slot wrapper.

```bash
REPO=/absolute/path/to/elsa-foundation-checkout
(cd "$REPO/Studio" && pnpm build)
dotnet build "$REPO/Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj"
dotnet build "$REPO/Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj"
```

### Prepare private normal and baseline roots

The configuration flow follows `Essentials/apps/Elsa.Workbench/Program.cs` and `Studio/src/apps/Elsa.Studio.Web/Program.cs`: both hosts read settings from their explicit content root, and the Workbench layers its selected `shells.json` over appsettings. The script uses Python's standard library only. It copies the normal Workbench shell without displaying or documenting its existing Development seed values; never print or paste the copied identity settings. Studio's base `appsettings.json` is JSONC, so it is copied byte-for-byte and overrides go into valid JSON `appsettings.Development.json` files.

```bash
set -eu
umask 077
REPO=/absolute/path/to/elsa-foundation-checkout
test -f "$REPO/Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj"
RUN="$(mktemp -d "${TMPDIR:-/tmp}/elsa-3073.XXXXXX")"
chmod 700 "$RUN"
printf 'Private QA root: %s\n' "$RUN"

python3 - "$REPO" "$RUN" <<'PY'
import json
import os
import shutil
import sys
from pathlib import Path

repo, root = Path(sys.argv[1]).resolve(), Path(sys.argv[2]).resolve()
if not root.is_dir() or any(root.iterdir()) or root == repo or root in repo.parents or repo in root.parents:
    raise SystemExit("QA root and repository must be separate")
os.umask(0o077)
os.chmod(root, 0o700)
wb = Path("Essentials/apps/Elsa.Workbench")
studio = Path("Studio/src/apps/Elsa.Studio.Web")
origin, backend_url = "http://127.0.0.1:5689", "http://127.0.0.1:5695"
def read(path):
    return json.loads((repo / path).read_text(encoding="utf-8-sig"))
def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    os.chmod(path, 0o600)
def overlay(data, *keys, value):
    for key in keys[:-1]:
        data = data.setdefault(key, {})
        if not isinstance(data, dict):
            raise SystemExit("Source configuration shape changed")
    data[keys[-1]] = value

profiles = {}
for profile in ("normal", "baseline"):
    normal = profile == "normal"
    wb_content, wb_data = root / "workbench" / profile / "content", root / "workbench" / profile / "data"
    wb_packages, studio_content = wb_content / "packages", root / "studio" / profile / "content"
    studio_packages = root / "studio" / profile / "packages"
    for directory in (wb_packages, wb_data, studio_content, studio_packages):
        directory.mkdir(parents=True, mode=0o700)

    backend = read(wb / "appsettings.json")
    backend["Cors"]["AllowedOrigins"] = [origin]
    backend["ConnectionStrings"]["Elsa"] = f"Data Source={wb_data / 'elsa.db'}"
    backend["Nuplane"]["Loading"]["Enabled"] = False
    backend["Nuplane"]["Setup"]["AutomaticReconciliation"] = False
    feeds = backend["Nuplane"]["Setup"]["Feeds"]
    if len(feeds) != 1 or "Watch" not in feeds[0]["Directory"]:
        raise SystemExit("Expected the current single Workbench directory feed")
    feeds[0]["DirectoryPath"] = str(wb_packages)
    feeds[0]["Directory"]["Watch"] = False
    backend["Elsa"]["Diagnostics"]["ConsoleLogStreaming"]["Enabled"] = False
    write(wb_content / "appsettings.json", backend)

    backend_dev = read(wb / "appsettings.Development.json")
    overlay(backend_dev, "Elsa", "Diagnostics", "ConsoleLogStreaming", "Enabled", value=False)
    overlay(backend_dev, "Elsa", "DataProtection", "ApplicationName", value=f"ElsaStudioDiagnostics3073-{root.name}-{profile}")
    overlay(backend_dev, "Elsa", "DataProtection", "EntityFrameworkCore", value={
        "Enabled": True, "Provider": "Sqlite", "ConnectionString": f"Data Source={wb_data / 'keyring.db'}"
    })
    write(wb_content / "appsettings.Development.json", backend_dev)

    shell_source = wb / ("shells.json" if normal else "shells.baseline.json")
    backend_shell = read(shell_source)
    features = backend_shell["CShells"]["Shells"]["default"]["Features"]
    otel, logs = "DiagnosticsOpenTelemetryEntityFrameworkCore", "DiagnosticsStructuredLogsEntityFrameworkCore"
    if normal:
        features["FoundationIdentityAspNetCoreIdentity"]["AllowedReturnUrlOrigins"] = [origin]
        for key in (otel, logs):
            features[key]["ConnectionString"] = f"Data Source={wb_data / 'diagnostics.db'}"
    write(wb_content / "shells.json", backend_shell)

    shutil.copyfile(repo / studio / "appsettings.json", studio_content / "appsettings.json")
    os.chmod(studio_content / "appsettings.json", 0o600)
    studio_dev = read(studio / "appsettings.Development.json")
    overlay(studio_dev, "Studio", "BackendBaseUrl", value=backend_url)
    overlay(studio_dev, "Studio", "BackendServerBaseUrl", value=backend_url)
    overlay(studio_dev, "Studio", "Auth", "Enabled", value=normal)
    overlay(studio_dev, "Nuplane", "Loading", "Enabled", value=False)
    overlay(studio_dev, "Nuplane", "Setup", "AutomaticReconciliation", value=False)
    overlay(studio_dev, "Nuplane", "Setup", "Feeds", value=[{
        "DirectoryPath": str(studio_packages), "Directory": {"Watch": False}
    }])
    write(studio_content / "appsettings.Development.json", studio_dev)

    studio_shell = read(studio / "shells.json")
    studio_features = studio_shell["CShells"]["Shells"]["Default"]["Features"]
    if not normal:
        for key in ("OpenTelemetryStudio", "StructuredLogsStudio"):
            del studio_features[key]
    write(studio_content / "shells.json", studio_shell)
    profiles[profile] = {
        "workbenchContentRoot": str(wb_content), "studioContentRoot": str(studio_content),
        "workbenchData": str(wb_data), "studioPackages": str(studio_packages),
    }

manifest = {"purpose": "#3073 isolated composed-host QA", "root": str(root), "profiles": profiles}
manifest_path = root / "manifest.json"
manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
os.chmod(manifest_path, 0o600)
print(f"Prepared private config roots: {manifest_path}")
PY
```

The normal pair preserves `Essentials/apps/Elsa.Workbench/shells.json` and Studio's normal module shell. Baseline pairs `shells.baseline.json` with a copied Studio shell omitting only `OpenTelemetryStudio` and `StructuredLogsStudio`; this is necessary because the Studio manifest follows its own active features. Baseline is anonymous composition proof only: it retains Runtime evidence settings and Console, and does not prove zero links or authenticated permissions. The script gives each profile private `elsa.db` and keyring files; normal Diagnostics stores use private `diagnostics.db`. Both use the existing EF SQLite Data Protection settings, distinct application names, empty packages folders, disabled Nuplane loading/reconciliation/watch, and disabled Workbench console-log streaming. No optional provider is invoked, management key is invented, tracked file is changed, or HOME override is used.

### Launch and inspect each profile

Run each host in its own foreground terminal and wait for it to stop before reusing the allocated port for the other profile. The explicit content roots and URLs avoid the launch-profile ports. The selected appsettings and shell files already contain the private database and Nuplane paths, so the command line does not need credentials or config overrides.

```bash
PROFILE=normal
DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project "$REPO/Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj" \
  --no-build --no-restore --no-launch-profile -- \
  --contentRoot "$RUN/workbench/$PROFILE/content" --urls http://127.0.0.1:5695

# In a second terminal, using the same REPO, RUN, and PROFILE values:
DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project "$REPO/Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj" \
  --no-build --no-restore --no-launch-profile -- \
  --contentRoot "$RUN/studio/$PROFILE/content" --urls http://127.0.0.1:5689
```

For normal, sign in using the disposable Development identity from the copied source shell; do not record its credential values. Check `GET http://127.0.0.1:5695/health/ready`, then require `200` from `http://127.0.0.1:5689/studio/`, `/studio-runtime.js`, and `/_elsa/studio/modules`. Inspect the manifest for both expected modules, then capture navigation and destination evidence in the isolated browser. Preserve request method, path, status, and content type; exclude bearer and cookie data. Settings save/restore is limited to the private normal database, and the original test policy must be restored.

Stop both foreground processes with Ctrl-C, wait for their sessions to exit, and verify the allocated ports are free before setting `PROFILE=baseline` and repeating the same two launch commands. Baseline Studio auth is disabled for anonymous composition inspection; verify its module manifest omits both telemetry modules while Runtime evidence settings and Console remain. Use separate normal and baseline data roots as prepared above. The authenticated read-only/no-write case remains covered by its component or authenticated-principal evidence, not by anonymous baseline.

### Cleanup

Close the isolated browser and stop only the two foreground sessions started for this run. After confirming they exited and both allocated ports are free, validate the exact private root before deleting it:

```bash
python3 - "$RUN" <<'PY'
import json, shutil, sys
from pathlib import Path
root = Path(sys.argv[1])
if root.is_symlink() or not root.is_dir() or not root.name.startswith("elsa-3073."):
    raise SystemExit("Refusing to remove an unexpected QA path")
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
if manifest.get("purpose") != "#3073 isolated composed-host QA" or Path(manifest.get("root", "")).resolve() != root.resolve():
    raise SystemExit("QA manifest does not identify this exact root")
shutil.rmtree(root)
PY
unset RUN
```

Keep qualification evidence outside the disposable runtime root, with no bearer/cookie material. Never use a port-wide kill, remove repository `wwwroot` output, or reuse one profile's database for the other.

Two pre-existing functional defects remain independently tracked: #3131 for the OpenTelemetry request/DTO contract mismatch and #3132 for Structured Logs stream credentials. They block final qualification #3075. Index registration, destination error-state wording and scoped accessibility results do not establish working trace inspection/live log streaming or complete shell accessibility. Final exact-head reviews/CI, merge and resulting-main qualification remain required.

## Architecture prerequisite

The architecture guard requires evaluated dependency assets in both configurations. Use the same repository procedure as CI before its suite:

```bash
bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

A missing evaluated Debug assets error is a setup failure, not a passed guard. Preserve that failure and run the required setup before requalification. Keep heavy checks serial and respect the machine's build-slot wrapper.
