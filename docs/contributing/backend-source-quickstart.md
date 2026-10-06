# Run the backend from source on macOS

Build the default `Elsa.Workbench` backend, check that it is ready, and run your first workflow. The
Development profile uses SQLite, so this path needs no separate database server or Docker.

## Prerequisites

- Git, a stable .NET 10 SDK, and PowerShell 7 (`pwsh`). Check with `dotnet --version` and `pwsh --version`.
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

An existing disposable checkout or Git worktree also works when its databases are compatible with the source
revision. The host creates or migrates its SQLite databases at startup and keeps them in that checkout.
After switching to newer source, old data can be incompatible and publish requests can return `500`.
For this onboarding path, use a fresh disposable checkout after a source update; preserve any valuable data
in the previous checkout. The [e2e rebuild guidance](../../e2e-tests/README.md) describes
resetting only disposable Workbench databases after stopping your own server.

## Build, start, and verify

From the Foundation repository root, build the focused backend project:

```bash
dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj
```

Start the HTTP launch profile in one terminal and leave it running:

```bash
dotnet run --no-build --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
```

This Development/SQLite profile listens at `http://localhost:5095`. In a second terminal, wait for readiness:

```bash
curl -fsS http://localhost:5095/health/ready
```

The response should report `"status":"ready"`. If startup is still in progress, retry after the server has
finished activating its shell. Then run the REST workflow smoke test:

```bash
pwsh -NoProfile -File ./e2e-tests/Test-WorkflowFlow.ps1 -BaseUrl http://localhost:5095
```

The script signs in with the local Development seed (`admin` / `Password123!`), submits and publishes a single
`WriteLine` workflow, executes it, and checks the result. The recorded run printed `SUCCESS`: the workflow
completed with one completed `WriteLine` activity and no incidents. These are local Development credentials;
do not reuse them outside the disposable local host. Stop the backend with Ctrl+C in the terminal where you
started it.

To resume later at the same source revision, run the same start command from the same checkout. Its local
workflow data is retained.
For a new empty environment, use another disposable checkout rather than deleting data from an existing one.

## If a step fails

| Symptom | Next step |
|---|---|
| SDK missing or target framework unsupported | Install a .NET 10 SDK, then check `dotnet --version` from the repository root. |
| `pwsh` not found | Install PowerShell 7 before running the workflow smoke test. The backend can remain running. |
| Restore cannot reach a source or find a package | Check the source/version named in the error against `NuGet.config` and your network access. Keep the error and source revision when reporting it; do not add private credentials or suppress restore errors to get past it. |
| Readiness cannot connect | Check the server terminal for startup errors and confirm it is listening on HTTP port 5095. Use the HTTP profile shown above. |
| The smoke test cannot authenticate | Confirm the URL points to your Development host and its disposable data, then inspect the server error. Existing databases may have different credentials. |
| Behavior changes or publish returns `500` after switching source revisions | Stop your own server, rebuild before using `--no-build`, and use a fresh disposable checkout to avoid retaining an incompatible database. Preserve valuable data in the old checkout. |

If port 5095 is occupied, do not stop a process you do not own. The [e2e guide](../../e2e-tests/README.md)
documents a custom-port start; choose a free port you own and pass the same `-BaseUrl` to the smoke test. The
custom-port route was not exercised in this baseline. For additional backend suites and their process-ownership
rules, use the e2e guide. For task-oriented build/test filters and the full-solution completion gate, see
[developer solution filters](../reference/developer-solution-filters.md).

## Studio pairing status

Studio's configured default backend is HTTPS `https://localhost:7243`, so a
Studio run paired with this HTTP profile needs `Studio__BackendBaseUrl=http://localhost:5095`.
The earlier Studio restore failure for `ConsoleLogStreaming.AspNetCore` `1.0.0-preview.13` was corrected
by [Studio PR #580](https://github.com/elsa-workflows/elsa-foundation-studio/pull/580), which pins `1.1.0`.
A new Studio.Web restore passed at merged Studio revision `2eabb6dc7112211b141d129f8849165f1816cbd7`
using empty package-cache, HTTP-cache, and CLI-home paths; installed SDK packs remained available
([evidence #579](https://github.com/elsa-workflows/elsa-foundation-studio/issues/579#issuecomment-6014605683)).
That resolves the package restore blocker. The complete paired source/browser/edit walkthrough remains
tracked by [Studio #566](https://github.com/elsa-workflows/elsa-foundation-studio/issues/566).

## Verified baseline

The build, startup, readiness and REST workflow commands were executed on macOS with SDK `10.0.300` at
Foundation revision `bc94b1a3694e02acefcfd4d0229cd5624a8b3a18` ([evidence #2430](https://github.com/elsa-workflows/elsa-foundation/issues/2430)).
A separate locked restore passed with fresh NuGet package and HTTP caches, without the maintainer package
cache or credentials; installed SDK library packs remained available
([evidence #2432](https://github.com/elsa-workflows/elsa-foundation/issues/2432)).
These are historical baselines from before the root SDK selector was added. The manually executed
commands above used SDK `10.0.300` on macOS. The separate hosted Windows/Linux source journey used SDK
`10.0.401` on Ubuntu 24.04 and Windows Server 2025; it builds and runs the Workbench DLL through an
automated host rather than replaying these interactive commands ([evidence #2443](https://github.com/elsa-workflows/elsa-foundation/issues/2443)).
They do not verify resolution through the new selector. Candidate SDK-selection and platform checks are
tracked separately in [#2455](https://github.com/elsa-workflows/elsa-foundation/issues/2455).
The selector defines the toolchain choice; it does not promise every allowed SDK patch or every platform.
Your clone may contain later source changes than these baselines.

The interactive commands in this guide remain manually verified only on macOS. Other operating systems,
a fresh OS installation, literal manual replay on Windows/Linux, and a human newcomer trial remain to
be verified.
The separate Studio browser proof used an existing developer environment; the later isolated Studio restore
above does not establish a fresh OS installation or the complete paired source-edit journey. No human newcomer
trial or complete contribution workflow is claimed here.

For a throwaway test-red/test-green source edit followed by a rebuilt workflow run, see the
[backend source-edit exercise](backend-source-edit.md). Its separate
[verified baseline](backend-source-edit.md#verified-baseline) records the execution date, source revision,
toolchain and evidence; it does not establish a fresh-install, cross-platform, or human-newcomer result.
