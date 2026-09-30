# Validation journey (planned implementation)

This specification does not implement composition inspect. Commands below become runnable in the linked implementation leaf. Use actual installed build output containing runtimeconfig/deps/host assembly and the new host capability; do not invoke them now expecting behavior. No database or package restore required for this proof.

## Developer workflow

1. Prepare temporary source files in the existing supported Workbench JSON layout, including selected environment overlay. Seed primary and observability resource references; keep private connection values and unknown-value canaries only in the fixture files.
2. Exercise existing interactive import and acceptance in the CLI pseudo-terminal fixture, then separately the init/workspace-profile path. Use the published Embedded profile/diagnostics group or reviewed test catalog and actual host descriptor closure. Do not claim raw authoring/worker research fixtures are released profiles.
3. Edit accepted intent to add/remove the real diagnostic consumers; reaccept through existing command. Keep all supplied workspace profiles, including an unused file, captured.
4. Run the proposed command:

```text
dotnet elsa composition inspect --host ./built-host --host-dir ./source --shell default --environment Production --catalog ./catalog.json --composition ./accepted.json --trust-host-code --format json
dotnet elsa composition plan --catalog ./catalog.json --composition ./accepted.json --format json
```

Supply --workspace-profile/--setting-review where the accepted document uses them. Inspection has a separate configurationResolution; plan still labels its broader persistence evidence unchecked. No --output-dir, --restore or connection arguments.

5. Confirm exact candidate consumers/targets and honest scope fields, no public values, no source changes, no candidate directory, no DB file/open action or host activation. Actual runtime shell preparer over identical candidate inputs must agree. Existing-host v2 list is the negative control, not the result source.
6. Independently run later generation only through its own capture/PTY review if testing retained unknown local data; preview does not authorize it.

## Required automated gates

```text
dotnet test tests/essentials/Modularity/Planning/Tests/Elsa.Modularity.Planning.Tests.csproj
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj
dotnet test tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Use normal dotnet build-slot wrapper, serialize heavy suites, record TRX and actual discovered/selected cases. Existing helpers/fixtures own teardown. Logs/results must not include canaries. Native DB behavior is outside this config-only claim; no new suite/provider matrix.

Run every [matrix](contracts/acceptance-proof-matrix.md) row, including actual child timeout/cancellation and malformed/oversize limits, exact host-default absent-file removal and descriptor-edge refusal, distinct configured values, full input drift, real producer→consumer lifecycle and old v2 compatibility. Root must see mutation controls fail for changed selection/capture/affinity or lifecycle behavior, then restored final affected suites pass. Retain only fixed safe observations in evidence; raw requests and sensitive fixture artifacts are cleaned up.
