# Quickstart: running the JTest acceptance suite

Run from `extensions/Elsa3/tests/JTest/`. Install or update the JTest CLI outside this repository: its NuGet package
source mapping does not allow `JTest.Cli`. Prerequisites: Docker running, the .NET SDK, and the JTest CLI:

```powershell
Push-Location $env:TEMP; dotnet tool update --global JTest.Cli; Pop-Location
```

## Elsa 3.6.3 baseline

Build the baseline image once, from elsa-core tag `3.6.3` as committed. Use the Git URL as the build context, not a
Windows working tree: with `core.autocrlf` on, `docker/entrypoint.sh` would get CRLF line endings and the container
would not start.

```powershell
docker build -f docker/ElsaServer.Dockerfile -t elsa-server:3.6.3-jtest https://github.com/elsa-workflows/elsa-core.git#3.6.3
```

Start a fresh container and run the setup suite, then the suites:

```powershell
docker run -d --rm --name elsa3-jtest -e ASPNETCORE_ENVIRONMENT=Development -e HTTP_PORTS=8080 -e HTTP__BASEURL=http://localhost:13000 -p 13000:8080 elsa-server:3.6.3-jtest
$env:ELSA_API_BASE_URL = 'http://localhost:13000'; $env:ELSA_EXECUTOR_BASE_URL = 'http://localhost:13000/workflows'
$env:ADMIN_USERNAME = 'admin'; $env:ADMIN_PASSWORD = '<baseline admin password>'
$env:CORRELATION_ID = [guid]::NewGuid().ToString('N'); $env:DATE_NOW_ISO_STRING = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$env:JTEST_CONFIG_FILE = "$PWD/global-configuration.json"
jtest run import-and-publish-workflows.json --env-file environments/dev.json
jtest run "tests/**/*" "!tests/performance/baseline-dispatch-tests.json" --env-file environments/dev.json
docker stop elsa3-jtest
```

## Elsa 4

Delete the Workbench's SQLite store, start the Workbench with the reconciliation enabled for the run (the exact
configuration override is fixed by task T007 and written into the suite README), then:

```powershell
$env:ELSA_API_BASE_URL = 'http://localhost:5095'; $env:ELSA_EXECUTOR_BASE_URL = 'http://localhost:5095/workflows/http'
$env:ADMIN_USERNAME = 'admin'; $env:ADMIN_PASSWORD = '<workbench development admin password>'
$env:CORRELATION_ID = [guid]::NewGuid().ToString('N'); $env:DATE_NOW_ISO_STRING = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
$env:JTEST_CONFIG_FILE = "$PWD/global-configuration.elsa4.json"
# after every start: set capture (in memory) - variableWrites override and activity snapshot level
#   PUT $env:ELSA_API_BASE_URL/runtime/workflows/diagnostics/settings   (exact body recorded by task T007)
jtest run "tests/**/*" "!tests/performance/baseline-dispatch-tests.json" --env-file environments/elsa4.json
```

There is no setup suite on Elsa 4: the reconciliation published the definitions at startup. Check its summary in the
Workbench log before running suites.

## Recording a run

Add a row to `ledger/runs.md` and update `ledger/suites.md`. A failed run stays in the ledger.
