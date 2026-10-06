# Run the backend from source on macOS

Build the default `Elsa.Workbench` backend, check that it is ready, and run your first workflow. The
Development profile uses SQLite, so this path needs no separate database server or Docker.

## Prerequisites

- Git, a .NET 10 SDK, and PowerShell 7 (`pwsh`). Check with `dotnet --version` and `pwsh --version`.
- Network access to the public package sources listed in the committed [`NuGet.config`](../../NuGet.config).
  The verified Workbench restore needed no maintainer credentials.

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
Studio run paired with this HTTP profile needs `Studio__BackendBaseUrl=http://localhost:5095`. The separate
cold Studio.Web restore at the same baseline failed with NU1603 because
`ConsoleLogStreaming.AspNetCore` `1.0.0-preview.13` was unavailable from the configured sources. That finding
is tracked by [Studio #551](https://github.com/elsa-workflows/elsa-foundation-studio/issues/551); its proposed
pin update is in [draft PR #557](https://github.com/elsa-workflows/elsa-foundation-studio/pull/557), which was
unmerged when this baseline was recorded. Check that work before expecting the Studio source restore to pass.

## Verified baseline

The build, startup, readiness and REST workflow commands were executed on macOS with SDK `10.0.300` at
Foundation revision `bc94b1a3694e02acefcfd4d0229cd5624a8b3a18` ([evidence #2430](https://github.com/elsa-workflows/elsa-foundation/issues/2430)).
A separate locked restore passed with fresh NuGet package and HTTP caches, without the maintainer package
cache or credentials; installed SDK library packs remained available
([evidence #2432](https://github.com/elsa-workflows/elsa-foundation/issues/2432)).
CI selects `10.x`; the repository does not pin the tested patch version. This records a tested toolchain,
not a new support policy. Your clone may contain later changes than this baseline.

Other operating systems and a fresh OS installation remain to be verified.
The separate Studio browser proof used an existing developer environment; it does not remove the cold-restore
blocker above. No human newcomer trial or complete contribution workflow is claimed here.

For a throwaway test-red/test-green source edit followed by a rebuilt workflow run, see the
[backend source-edit exercise](backend-source-edit.md). It was verified separately at Foundation revision
`70f8db49f4f33a5a2a64eda23d5b0437548e2227` on macOS 26.6 arm64 with .NET SDK 10.0.300; this does not
establish a fresh-install, cross-platform, or human-newcomer result.
