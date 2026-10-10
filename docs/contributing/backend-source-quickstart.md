# Run the backend from source

Build the default `Elsa.Workbench` backend, check that it is ready, and run your first workflow. The
Development profile uses SQLite, so this path needs no separate database server or Docker. The Git and
.NET commands work on Windows, macOS, and Linux; shell-specific verification and recovery commands are
shown separately.

## Prerequisites

- Git and a stable .NET 10 SDK for building and running the backend.
- PowerShell for the workflow smoke test: PowerShell 7 (`pwsh`) on macOS/Linux, or Windows PowerShell 5.1
  (`powershell.exe`) on Windows. PowerShell is not needed for the Git or .NET commands.
- Network access to the public package sources listed in the committed [`NuGet.config`](../../NuGet.config).
  The verified Workbench restore needed no maintainer credentials.

The repository's [`global.json`](../../global.json) sets the SDK floor at `10.0.300`. Its `latestFeature`
setting selects the latest installed stable SDK in the same or a later .NET 10.0 feature band; it does
not roll forward to another major/minor line. Prerelease SDKs are excluded. From the repository root,
`dotnet --version` prints the SDK selected for this checkout; it may be newer than `10.0.300` when a
later compatible SDK is installed. If the CLI reports that no compatible SDK is installed, install a
stable .NET 10.0 SDK at `10.0.300` or later from the [official .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0),
then open a new terminal and check again. This selects the SDK used by the .NET CLI; it does not change
the projects' target frameworks.

Start with a separate checkout for disposable onboarding data:

```bash
git clone https://github.com/elsa-workflows/elsa-foundation.git elsa-foundation-contributor
cd elsa-foundation-contributor
```

The basic build, start, and workflow smoke below do not require the EF command-line tool. If your task
uses EF migrations, run this optional restore from the repository root to restore the pinned tool from
the committed public feeds:

```bash
dotnet tool restore --tool-manifest .config/dotnet-tools.json --configfile NuGet.config
dotnet ef --version
```

This restores and verifies the manifest-pinned `dotnet-ef` for this checkout; it does not install a global
tool.

An existing disposable checkout or Git worktree also works when its databases are compatible with the source
revision. The host creates or migrates its SQLite databases at startup and keeps them in that checkout.
After switching to newer source, old data can be incompatible and publish requests can return `500`.
For this onboarding path, reset only disposable Workbench data after stopping your own server; keep valuable
data in its existing checkout and use a separate disposable checkout for the new source. The exact reset
steps and file allowlist are below.

## Build, start, and verify

From the Foundation repository root, build the focused backend project:

```bash
dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true
```

The build still performs its normal restore when needed, but locked mode keeps that restore aligned with the
committed dependency versions required by CI. When dependencies change, update and review the lock files using
the [NuGet lock-file instructions](../reference/nuget-lock-files.md#updating-lock-files-on-a-package-bump).

Start the HTTP launch profile in one terminal and leave it running:

```bash
dotnet run --no-build --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
```

This Development/SQLite profile listens at `http://localhost:5095`. If that port is occupied, leave the
existing listener alone and use the [alternate-port instructions](#port-already-in-use).

In a second terminal, open the same repository directory and check readiness using your shell's command.
The response should report `"status":"ready"`. If startup is still in progress, retry after the server
has finished activating its shell. Once it is ready, run the workflow smoke test.

**macOS or Linux:**

```bash
curl -fsS http://localhost:5095/health/ready
pwsh -NoProfile -File ./e2e-tests/Test-WorkflowFlow.ps1 -BaseUrl http://localhost:5095
```

**Windows PowerShell:** use `curl.exe` to avoid PowerShell's `curl` alias.

```powershell
curl.exe -fsS http://localhost:5095/health/ready
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\e2e-tests\Test-WorkflowFlow.ps1 -BaseUrl http://localhost:5095
```

The script signs in with the local Development seed (`admin` / `Password123!`), submits and publishes a
single `WriteLine` workflow, executes it, and checks the result. Expect `SUCCESS`, a completed workflow
with one completed `WriteLine` activity, and no incidents. These are local Development credentials;
do not reuse them outside the disposable local host. Stop the backend with Ctrl+C in its terminal.

Next, try the [backend source-edit exercise](backend-source-edit.md) to make a small change, see a test
fail and pass, rebuild, and run the changed workflow.

To resume later at the same source revision, run the same start command from the same checkout. Its local
workflow data is retained.
For a new empty environment, use another disposable checkout unless you are deliberately resetting this
disposable onboarding data as described below.

## Reset disposable SQLite data

Use this only for disposable onboarding data whose workflows you are willing to remove. First stop the
Workbench process you started and confirm it has exited. Preserve valuable workflows by keeping their
checkout and using a separate disposable checkout; this procedure is a reset, not a data migration or export.

From the repository root, inventory SQLite databases and sidecars under the Workbench directory, including
nested paths and journal files.

**macOS/Linux (Bash):**

```bash
find src/apps/Elsa.Workbench -type f \( -name '*.db' -o -name '*.db-*' -o -name '*.sqlite*' -o -name '*-journal' \) -print
```

**Windows PowerShell:**

```powershell
Get-ChildItem -LiteralPath ./src/apps/Elsa.Workbench -Recurse -File -Force |
    Where-Object { $_.Name -like '*.db' -or $_.Name -like '*.db-*' -or $_.Name -like '*.sqlite*' -or $_.Name -like '*-journal' } |
    Select-Object -ExpandProperty FullName
```

Review every result before deleting anything. The only allowed paths are these six files directly under
`src/apps/Elsa.Workbench/`: `elsa.db`, `elsa.db-wal`, `elsa.db-shm`, `elsa-diagnostics.db`,
`elsa-diagnostics.db-wal`, and `elsa-diagnostics.db-shm`. If configuration points to a database elsewhere,
or the listing shows another database, a nested path, or any journal file, stop and identify its owner; do not
widen this list or delete it. When the inventory contains only the allowed files, remove those exact names
using the command for your shell.

**macOS/Linux (Bash):**

```bash
rm -f -- \
  src/apps/Elsa.Workbench/elsa.db \
  src/apps/Elsa.Workbench/elsa.db-wal \
  src/apps/Elsa.Workbench/elsa.db-shm \
  src/apps/Elsa.Workbench/elsa-diagnostics.db \
  src/apps/Elsa.Workbench/elsa-diagnostics.db-wal \
  src/apps/Elsa.Workbench/elsa-diagnostics.db-shm
```

**Windows PowerShell:**

```powershell
@(
    'elsa.db', 'elsa.db-wal', 'elsa.db-shm',
    'elsa-diagnostics.db', 'elsa-diagnostics.db-wal', 'elsa-diagnostics.db-shm'
) | ForEach-Object {
    $databasePath = Join-Path './src/apps/Elsa.Workbench' $_
    if (Test-Path -LiteralPath $databasePath -PathType Leaf) {
        Remove-Item -LiteralPath $databasePath
    }
}
```

Start Workbench again; it creates fresh databases and schema for the current source. Do not use a wildcard
or remove the directory.

## If a step fails

| Symptom | Next step |
|---|---|
| SDK missing or target framework unsupported | Install a .NET 10 SDK, then check `dotnet --version` from the repository root. |
| PowerShell command not found | Install PowerShell 7 (`pwsh`) on macOS/Linux, or use Windows PowerShell 5.1 (`powershell.exe`) on Windows. The backend can remain running. |
| Restore cannot reach a source or find a package | Check the source/version named in the error against `NuGet.config` and your network access. Keep the error and source revision when reporting it; do not add private credentials or suppress restore errors to get past it. |
| Readiness cannot connect | Check the server terminal for startup errors and confirm it is listening on HTTP port 5095. Use the HTTP profile shown above. |
| The smoke test cannot authenticate | Confirm the URL points to your Development host and its disposable data, then inspect the server error. Existing databases may have different credentials. |
| Behavior changes or publish returns `500` after switching source revisions | Stop your own server and rebuild. For disposable data, follow [Reset disposable SQLite data](#reset-disposable-sqlite-data); otherwise use a fresh disposable checkout and preserve valuable data in the old one. |

### Port already in use

If port 5095 is occupied, the default profile reports that it cannot bind because the address is already in
use. Do not stop a process you do not own. After the build above, start on this verified alternate port in
the first terminal:

```bash
dotnet run --no-build --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --no-launch-profile -- --urls http://localhost:5295 --environment Development
```

In a second terminal at the repository root, check readiness and then run the smoke test with the same
base URL. Retry readiness until the shell has finished activating.

**macOS or Linux:**

```bash
curl -fsS http://localhost:5295/health/ready
pwsh -NoProfile -File ./e2e-tests/Test-WorkflowFlow.ps1 -BaseUrl http://localhost:5295
```

**Windows PowerShell:**

```powershell
curl.exe -fsS http://localhost:5295/health/ready
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\e2e-tests\Test-WorkflowFlow.ps1 -BaseUrl http://localhost:5295
```

Use another free port you own if 5295 is also occupied; never stop a foreign listener. For additional backend
suites and their process-ownership rules, use the [e2e guide](../../e2e-tests/README.md). For task-oriented
build/test filters and the full-solution completion gate, see
[developer solution filters](../reference/developer-solution-filters.md).

## Pair with Studio

Follow the [Studio source quickstart](https://github.com/elsa-workflows/elsa-foundation-studio/blob/main/docs/contributing/studio-source-quickstart.md)
for the frontend prerequisites, startup, and browser walkthrough. Studio's configured default backend is
HTTPS `https://localhost:7243`; when pairing with this guide's HTTP profile, use
`Studio__BackendBaseUrl=http://localhost:5095` (or the alternate port you chose).

The paired source/browser/edit walkthrough was accepted in
[Studio #566](https://github.com/elsa-workflows/elsa-foundation-studio/issues/566#issuecomment-6040280827).
That acceptance covers the recorded technical checks; the deferred human newcomer trials were not performed.

## Verified baseline

The commands above are contributor instructions. These historical results record what was tested at specific
revisions; they do not establish that every later checkout or allowed SDK patch has passed.

| Check | Recorded environment and evidence | Limits |
|---|---|---|
| Original build, startup, readiness, and workflow smoke | macOS, SDK `10.0.300`, Foundation `bc94b1a3694e02acefcfd4d0229cd5624a8b3a18`; [#2430](https://github.com/elsa-workflows/elsa-foundation/issues/2430) | Plain build, before the SDK selector and the documented locked-mode build. |
| Locked restore with empty NuGet package and HTTP caches | [#2432](https://github.com/elsa-workflows/elsa-foundation/issues/2432) | No maintainer credentials or package cache; installed SDK packs remained available. |
| Original hosted platform journey | Ubuntu 24.04 and Windows Server 2025, SDK `10.0.401`; [#2443](https://github.com/elsa-workflows/elsa-foundation/issues/2443) | Automated DLL host, before the SDK selector; not a replay of the interactive launch command. |
| SDK selector and hosted platform checks | Windows/Linux selected SDK `10.0.401`; [main verification for #2455](https://github.com/elsa-workflows/elsa-foundation/issues/2455#issuecomment-6024054462) | Evidence for the recorded runs, not every permitted SDK patch. |
| Tool restore, focused build, occupied-port recovery, alternate-port smoke, and SQLite reset | macOS 26.6 ARM64, SDK `10.0.300`, Foundation `0e7e7f4b23ba9c55bdb6d9062348f12cf25e4101`; [#2458](https://github.com/elsa-workflows/elsa-foundation/issues/2458#issuecomment-6024342838) | Automated in an existing environment; build used `--no-restore`. The readiness and exact-file removal examples were not executed verbatim in that run. |

The current [backend source platform workflow](../../.github/workflows/backend-source-platform.yml) checks
Windows and Linux using the locked-mode build, HTTP launch profile, and source-edit exercise. Check its run
for your revision when evaluating CI evidence. The source-edit exercise also has its own
[recorded baseline](backend-source-edit.md#verified-baseline).

The original macOS walkthrough was manually executed. No fresh operating-system installation, manual
Windows/Linux walkthrough, or human newcomer trial is claimed by these records. The PowerShell reset example
is provided for Windows use; the historical macOS recovery run does not establish a Windows recovery result.
