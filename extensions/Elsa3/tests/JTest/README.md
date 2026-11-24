# JTest acceptance suite (Elsa 3 to Elsa 4)

The JTest corpus from the JTest-Nexxbiz repository at commit `b1cde13`, used as the final acceptance run of the Elsa 3
to Elsa 4 conversion (spec 1235, initiative #3052). The suites run unchanged against an Elsa 3.6.3 baseline server and
against the Elsa 4 Workbench, each with its own template set. Run results and decisions live in [`ledger/`](ledger/).

This README holds the run procedures; task T044 completes it.

## Layout

| Path | Content |
|---|---|
| `workflow-definitions/` | The 36 Elsa 3 definitions (32 in the acceptance set, spec Q13 and Q15) |
| `tests/` | 42 suite files (39 in the acceptance set) |
| `import-and-publish-workflows.json` | Elsa 3 setup suite: imports and publishes the definitions |
| `templates-elsa3/`, `environments/elsa3.json`, `global-configuration.elsa3.json` | Elsa 3 template set and configuration |
| `test-files/` | Files the suites upload |
| `ledger/` | Suites, baseline failures, refactor scope, suite changes, dispositions and runs |

The corpus was copied byte for byte (`git archive` with `core.autocrlf=false`; `.gitattributes` marks this folder
`-text`). It holds no secrets: credentials and base URLs are `${VARIABLE}` references (T002).

**Excluded from acceptance, to re-evaluate on Elsa 4**: `tests/branching/implicit-tests.json` and
`tests/branching/join-wait-all-active-tests.json`, with `branching-implicit.json` and `branching-wait-all-active.json`
(coordinator decision 2026-11-24, spec Q15). They encode implicit-merge and `WaitAllActive` join behaviour that
Elsa 3.6.3 does not show; what Elsa 4 does there is decided with the JOIN work (#4193), and these suites, or Elsa 4-only
suites written for that decision, come back then. The baseline-dispatch case stays excluded too (spec Q13).

Fixes to the original corpus files (ledger `baseline-failures.md`, `suite-changes.md`) go back to the JTest-Nexxbiz
repository at the end of the initiative (task T047).

## Tools

JTest CLI 2.0.3 (`dotnet tool update --global JTest.Cli`, run outside this repository: its NuGet source mapping does
not allow the package). Docker for the baseline server.

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

From this folder, on a fresh container for every run:

```powershell
docker run -d --rm --name elsa3-jtest -e ASPNETCORE_ENVIRONMENT=Development -e HTTP_PORTS=8080 -e HTTP__BASEURL=http://localhost:13000 -p 13000:8080 elsa-server:3.6.3-jtest
# ready when GET http://localhost:13000/ returns "Healthy"
$env:ELSA_API_BASE_URL = 'http://localhost:13000'; $env:ELSA_EXECUTOR_BASE_URL = 'http://localhost:13000/workflows'
$env:ADMIN_USERNAME = 'admin'; $env:ADMIN_PASSWORD = 'password'
$env:CORRELATION_ID = [guid]::NewGuid().ToString('N'); $env:DATE_NOW_ISO_STRING = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$env:JTEST_CONFIG_FILE = "$PWD/global-configuration.elsa3.json"
jtest run import-and-publish-workflows.json --env-file environments/elsa3.json
jtest run "tests/**/*" "!tests/performance/baseline-dispatch-tests.json" "!tests/branching/implicit-tests.json" "!tests/branching/join-wait-all-active-tests.json" --env-file environments/elsa3.json
docker stop elsa3-jtest
```

## Elsa 4 Workbench

The Workbench has the `Elsa3JsonReconciliation` feature in its catalog but leaves it off in `shells.json`. A run turns it
on with environment variables (they override `shells.json`, see `apps/Elsa.Workbench/Program.cs`), so the committed
shell stays unchanged. See *Elsa 4 run configuration* below (task T007).

### Elsa 4 run configuration (T007)

From this folder, in a PowerShell session of its own. The block deletes the Workbench's SQLite stores, then sets the run's
configuration as process environment variables, which override `shells.json`: `Elsa3JsonReconciliation` with
`SourceId` `jtest` and a `Files` list of the 32 acceptance definitions (the files the Elsa 3 setup suite imports, in
its order, without the two excluded by spec Q15; an adjusted copy replaces its original here, task T036), `Elsa3ImportActivitiesEntityFrameworkCore` on the
shared `Elsa` connection (the import commits in one transaction with the Activities Design store, so it must use the same
database), and `WorkflowsRuntimeCheckpointPersistence` in `Durable` mode.

```powershell
$wb = (Resolve-Path ../../../../apps/Elsa.Workbench).Path
Remove-Item "$wb/*.db", "$wb/*.db-shm", "$wb/*.db-wal" -ErrorAction SilentlyContinue
$feature = 'CShells__Shells__default__Features__'
function Set-RunVariable([string] $name, [string] $value) { [Environment]::SetEnvironmentVariable($name, $value, 'Process') }
Set-RunVariable 'ASPNETCORE_ENVIRONMENT' 'Development'
Set-RunVariable 'ASPNETCORE_URLS' 'http://localhost:5095'
Set-RunVariable "${feature}Elsa3JsonReconciliation__Options__SourceId" 'jtest'
Set-RunVariable "${feature}Elsa3ImportActivitiesEntityFrameworkCore__ConnectionName" 'Elsa'
Set-RunVariable "${feature}WorkflowsRuntimeCheckpointPersistence__Mode" 'Durable'
# The acceptance set (spec Q13, Q15): the definitions the Elsa 3 setup suite imports, in its order, without the two excluded.
$excluded = 'workflow-definitions/branching-implicit.json', 'workflow-definitions/branching-wait-all-active.json'
$files = @([regex]::Matches((Get-Content import-and-publish-workflows.json -Raw), '"filePath":\s*"(workflow-definitions/[^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -notin $excluded })
for ($i = 0; $i -lt $files.Count; $i++) {
    Set-RunVariable "${feature}Elsa3JsonReconciliation__Options__Files__${i}__Order" "$i"
    Set-RunVariable "${feature}Elsa3JsonReconciliation__Options__Files__${i}__FilePath" (Join-Path $PWD $files[$i])
}
dotnet run --project $wb --no-launch-profile
```

Ready when the log shows the summary line `Elsa 3 import 'jtest' in persistence scope 'default': …`. After every
start, set capture (it lives in memory): snapshot level for activity inputs and outputs, and the `variableWrites`
override. The Workbench's seeded development admin comes from `apps/Elsa.Workbench/shells.json`. The identity cookie
is larger than curl's 8 KB cookie limit, so this uses a PowerShell web session:

```powershell
$base = 'http://localhost:5095'
$seed = (Get-Content ../../../../apps/Elsa.Workbench/shells.json -Raw | ConvertFrom-Json).CShells.Shells.default.Features.FoundationIdentityAspNetCoreIdentityEntityFrameworkCore
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

## For the Nexxbiz workspace (T047)

Changes and decisions to take back to the JTest-Nexxbiz repository and the Nexxbiz workflows at the end of the
initiative. The ledger rows hold the evidence.

**Corpus fixes to bring back** (`ledger/baseline-failures.md`, applied in this folder):

- B9 `tests/branching/join-wait-all-tests.json`: expect instance status `Running`, not `Finished` (Elsa 3.5.2+ `WaitAll`).
- B10, B11 the two dispatch `channels` suites: typo `chanels` → `channels`. The Elsa 3 environment's channel values are
  empty here because a stock 3.6.3 server registers no dispatch channels; the Nexxbiz executor keeps its own channel names.
- B12, B13 `tests/http/headers-tests.json`, `tests/logging/logging-tests.json`: read response headers as
  `$.this.headers['<name>']` (JTest 2.x).
- B14 `javascript-script-handler-functions.json`: `base64Encode(...)` → `stringToBase64(...)`, `base64Decode(...)` →
  `stringFromBase64(...)`; its suite no longer has the `getConfiguration` cases.
- Q14 `storage-drivers-variables-root-workflow.json`: version 1.

**To reconsider for Nexxbiz on Elsa 4**:

- Replace the Executor V3 `base64Encode`/`base64Decode` calls in all Nexxbiz workflows with the Elsa 3 natives
  `stringToBase64`/`stringFromBase64` (same UTF-8 Base64). Elsa 4 has both as built-ins and the conversion keeps them as
  written; the Executor V3 names are refused as unknown host functions.
- `getConfiguration()` (for example `getConfiguration("BusinessSettings")[name]`) has no Elsa 3 or Elsa 4 equivalent:
  Elsa 3's `getConfig` returns one string value, and the Elsa 4 conversion refuses configuration reads. Direction
  (coordinator, 2026-11-24): a Nexxbiz feature of its own on Elsa 4 with purpose-specific functions such as
  `getBusinessSettings()`, so workflows stop indexing raw configuration. Until then workflows that call it are refused
  on import.
- The implicit-merge and `WaitAllActive` join suites are excluded here (spec Q15) and come back, or are replaced by
  Elsa 4-only suites, once the Elsa 4 join behaviour is decided (#4193).
