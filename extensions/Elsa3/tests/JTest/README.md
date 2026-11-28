# JTest acceptance suite (Elsa 3 to Elsa 4)

The JTest corpus from the JTest-Nexxbiz repository at commit `b1cde13`, used as the final acceptance run of the Elsa 3
to Elsa 4 conversion (spec 1235, initiative #3052). The suites run unchanged against an Elsa 3.6.3 baseline server and
against Elsa 4 through the migration CLI and the migration host (spec Q16), each engine with its own template set. Run results and decisions live in [`ledger/`](ledger/).

This README holds the run procedures; task T044 completes it.

## Layout

| Path | Content |
|---|---|
| `workflow-definitions/` | The 36 Elsa 3 corpus definitions (32 in the acceptance set, spec Q13 and Q15), plus `javascript-variable-changes.json`, an S-15 addition (ledger `suite-changes.md`, W3) |
| `tests/` | 42 corpus suite files (39 in the acceptance set), plus `javascript/variable-changes-tests.json` (W3), which runs on both engines |
| `import-and-publish-workflows.json` | Elsa 3 setup suite: imports and publishes the definitions |
| `templates-elsa3/`, `environments/elsa3.json`, `global-configuration.elsa3.json` | Elsa 3 template set and configuration |
| `test-files/` | Files the suites upload |
| `ledger/` | Suites, baseline failures, refactor scope, suite changes, dispositions and runs |

The corpus was copied byte for byte (`git archive` with `core.autocrlf=false`; `.gitattributes` marks this folder
`-text`). It holds no secrets: credentials and base URLs are `${VARIABLE}` references (T002).

**The run set** (spec Q17, coordinator 2026-11-28). Both engines run the acceptance definitions with each approved
adjusted copy (`adjusted/`) in place of its original, plus the approved additions. The approved changes are the source
of truth; the originals stay as history (what the converter refuses, and the hand-over to the original repository),
not as a baseline.

**Excluded from acceptance, to re-evaluate on Elsa 4**: `tests/branching/implicit-tests.json` and
`tests/branching/join-wait-all-active-tests.json`, with `branching-implicit.json` and `branching-wait-all-active.json`
(coordinator decision 2026-11-24, spec Q15). They encode implicit-merge and `WaitAllActive` join behaviour that
Elsa 3.6.3 does not show; what Elsa 4 does there is decided with the JOIN work (#4193), and these suites, or Elsa 4-only
suites written for that decision, come back then. The baseline-dispatch case stays excluded too (spec Q13).

Fixes to the original corpus files (ledger `baseline-failures.md`, `suite-changes.md`) go back to the JTest-Nexxbiz
repository at the end of the initiative (task T047).

## Tools

JTest CLI 2.0.4 or later (`dotnet tool update --global JTest.Cli`, run outside this repository: its NuGet source mapping
does not allow the package). The Elsa 4 templates need 2.0.4: array-append `save` targets (`"$.instances[]"`), object
save values resolved at save time, and `for` `items` read as a JSONPath node list. The Elsa 3 runs were made with 2.0.3
(runs 1–11) and behave the same on 2.0.4. Docker for the baseline server.

## Elsa 3.6.3 baseline server

elsa-core tag `3.6.3` (commit `7eac7de6`) no longer builds with its own `docker/ElsaServer.Dockerfile`: its runtime
base `mcr.microsoft.com/dotnet/aspnet:10.0` is now Ubuntu 24.04, which has no `python3.11` packages. The image is built
from the tag's committed bytes (Git URL context), with the tag's Dockerfile piped in and only the Python package
version changed from 3.11 to 3.12 (four lines; no corpus definition uses Python):

```powershell
$df = (Invoke-WebRequest https://raw.githubusercontent.com/elsa-workflows/elsa-core/3.6.3/docker/ElsaServer.Dockerfile).Content -replace 'python3\.11','python3.12'
$df | docker build -f - -t elsa-server:3.6.3-jtest https://github.com/elsa-workflows/elsa-core.git#3.6.3
```

Image built 2026-11-24: `sha256:f30e8c78de392a600b0928e5c74ed017b5bda8b44144689f766da2e5b5f42973`.

The server's development admin is elsa-core's default user from `src/apps/Elsa.Server.Web/appsettings.json`: user
`admin`, password `password`.

### Run

Every run, on either engine, uses the run set (spec Q17). From this folder, on a fresh container for every run. The
setup suite imports the run set: each approved adjusted copy takes the place of its original.

```powershell
docker run -d --rm --name elsa3-jtest -e ASPNETCORE_ENVIRONMENT=Development -e HTTP_PORTS=8080 -e HTTP__BASEURL=http://localhost:13000 -p 13000:8080 elsa-server:3.6.3-jtest
# ready when GET http://localhost:13000/ returns "Healthy"
$env:ELSA_API_BASE_URL = 'http://localhost:13000'; $env:ELSA_EXECUTOR_BASE_URL = 'http://localhost:13000/workflows'
$env:ADMIN_USERNAME = 'admin'; $env:ADMIN_PASSWORD = 'password'
$env:CORRELATION_ID = [guid]::NewGuid().ToString('N'); $env:DATE_NOW_ISO_STRING = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$env:JTEST_CONFIG_FILE = "$PWD/global-configuration.elsa3.json"
$setup = [regex]::Replace((Get-Content import-and-publish-workflows.json -Raw), '"workflow-definitions/([^"]+)"', { param($m) $copy = "adjusted/$($m.Groups[1].Value)"; if (Test-Path $copy) { '"' + $copy + '"' } else { $m.Value } })
Set-Content import-run-set.tmp.json $setup
jtest run import-run-set.tmp.json --env-file environments/elsa3.json
Remove-Item import-run-set.tmp.json
jtest run "tests/**/*" "!tests/performance/baseline-dispatch-tests.json" "!tests/branching/implicit-tests.json" "!tests/branching/join-wait-all-active-tests.json" --env-file environments/elsa3.json
docker stop elsa3-jtest
```

To see what Elsa 3 does with an original definition, run the setup suite unchanged
(`jtest run import-and-publish-workflows.json …`). That is history, not the baseline.

## Elsa 4: the migration CLI and the migration host

The Elsa 4 side takes the path an Elsa 3 user takes (spec Q16): the
[migration CLI](../../src/Migration/Tooling/README.md) (spec 1237) converts the run set into a `migrated/` folder, and
the [migration host](../../../../apps/Elsa.Migration.Host/README.md) (spec 1236) loads that folder on an empty store
and runs it. Ledger runs 15 to 43 used the Workbench with the Elsa 3 startup reconciliation; that procedure is in this
file's history.

### 1. Export the run set and convert it

From this folder. The export folder holds the run set: the definitions the Elsa 3 setup suite imports, without the two
excluded by spec Q15, each approved adjusted copy in place of its original.

```powershell
$run = "$env:TEMP/elsa3-jtest-run"
Remove-Item -Recurse -Force $run -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$run/export" | Out-Null
$excluded = 'workflow-definitions/branching-implicit.json', 'workflow-definitions/branching-wait-all-active.json'
$files = @([regex]::Matches((Get-Content import-and-publish-workflows.json -Raw), '"filePath":\s*"(workflow-definitions/[^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -notin $excluded })
foreach ($f in $files) {
    $copy = $f -replace '^workflow-definitions/', 'adjusted/'
    Copy-Item $(if (Test-Path $copy) { $copy } else { $f }) "$run/export/$(Split-Path $f -Leaf)"
}
Push-Location ../../../..
dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory "$run/export" --out "$run/out"
Pop-Location
```

The CLI exits with 1 when its report has Must fix or Reconsider findings; for this corpus that is expected (the refused
definitions). Keep `report.json` with the run: it lists what published and what was refused, with guidance.

### 2. Start the migration host

A fresh store for every run (delete `data/`), with the host's two settings pointed at the CLI's output. Durable
checkpoints are already part of the host's composition.

```powershell
$hostDir = (Resolve-Path ../../../../apps/Elsa.Migration.Host).Path
Remove-Item -Recurse -Force "$hostDir/data" -ErrorAction SilentlyContinue
$feature = 'CShells__Shells__default__Features__'
[Environment]::SetEnvironmentVariable("${feature}JsonWorkflowReconciliation__Options__FolderPath", "$run/out/migrated/workflows", 'Process')
[Environment]::SetEnvironmentVariable("${feature}JsonActivityReconciliation__Options__FilePath", "$run/out/migrated/activities/activities.json", 'Process')
dotnet run --project $hostDir
```

Ready when `GET http://localhost:5096/health/ready` returns 200. After every start, set capture (it lives in memory):
snapshot level for activity inputs and outputs, and the `variableWrites` override. The seeded development admin comes
from `apps/Elsa.Migration.Host/shells.json`. The identity cookie is larger than curl's 8 KB cookie limit, so this uses a
PowerShell web session:

```powershell
$base = 'http://localhost:5096'
$seed = (Get-Content ../../../../apps/Elsa.Migration.Host/shells.json -Raw | ConvertFrom-Json).CShells.Shells.default.Features.FoundationIdentityAspNetCoreIdentityEntityFrameworkCore
$login = @{ username = $seed.SeedAdminUserName; password = $seed.SeedAdminPassword } | ConvertTo-Json
Invoke-RestMethod -Method Post "$base/_elsa/identity/login" -ContentType 'application/json' -Body $login -SessionVariable session | Out-Null
$token = (Invoke-RestMethod "$base/_elsa/identity/token" -WebSession $session).accessToken
$capture = @{
    defaultLevel     = 'DiagnosticSnapshot'
    subjectOverrides = @{
        activityInputs  = 'DiagnosticSnapshot'
        activityOutputs = 'DiagnosticSnapshot'
        variableWrites  = 'DiagnosticSnapshot'
        durableValues   = 'Metadata'
        incidents       = 'Metadata'
        diagnostics     = 'Metadata'
    }
} | ConvertTo-Json
Invoke-RestMethod -Method Put "$base/runtime/workflows/diagnostics/settings" -Headers @{ Authorization = "Bearer $token" } -ContentType 'application/json' -Body $capture | ConvertTo-Json -Depth 5
```

The response's `effective` block shows the levels in force; host policy caps every value at bounded snapshots
(`snapshotLimits`).

### 3. Run the suites

```powershell
$env:ELSA_API_BASE_URL = 'http://localhost:5096'; $env:ELSA_EXECUTOR_BASE_URL = 'http://localhost:5096/workflows/http'
$env:ADMIN_USERNAME = $seed.SeedAdminUserName; $env:ADMIN_PASSWORD = $seed.SeedAdminPassword
$env:CORRELATION_ID = [guid]::NewGuid().ToString('N'); $env:DATE_NOW_ISO_STRING = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$env:JTEST_CONFIG_FILE = "$PWD/global-configuration.elsa4.json"
jtest run "tests/**/*" "!tests/performance/baseline-dispatch-tests.json" "!tests/branching/implicit-tests.json" "!tests/branching/join-wait-all-active-tests.json" --env-file environments/elsa4.json
```

A synchronous HTTP workflow answers within the host's `ActivitiesHttp:SyncRequestTimeout` (30 s by default); a run
that takes longer under load answers 408 or `workflow-response-not-produced` although it completes. Record such a
result as load, with the instance timings.
