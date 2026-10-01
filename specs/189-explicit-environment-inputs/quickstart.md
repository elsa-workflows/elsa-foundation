# Planned validation journey

This feature is not implemented by the planning artifacts. The commands below define implementation validation; no feature acceptance journey has run during authoring. Local architecture and maps checks ran separately as authoring gates and do not execute this future proof matrix. Use the [proof matrix](contracts/acceptance-proof-matrix.md) for the complete case assignment.

## Prerequisites

- A clean implementation build of the public CLI and the actual `src/apps/Elsa.Workbench/Elsa.Workbench.csproj` output containing the declared v1 capability and Workbench enrollment attribute.
- The actual `src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj` output for the unenrolled-host control; missing build artifacts fail the proof instead of skipping it.
- Existing fixture/test output for controlled old-host, unenrolled-host, malformed-input, lifecycle, and source-drift cases.
- A temporary source directory containing the selected Workbench JSON layers and an accepted composition.
- A private regular environment input file. Keep its values and any canary private to the temporary fixture; do not put them in a command argument or check them into artifacts.

The inspection is configuration-only. It must not start Workbench, activate services, open a database, apply migrations, publish, or save application state. The existing provider/database CLI acceptance journey remains a separate persistence proof.

## Minimal explicit input

Create a private file such as `environment-input.json` in the temporary fixture directory:

```json
{
  "version": 1,
  "entries": [
    { "key": "ConnectionStrings__Primary", "value": "<fixture-private-value>" },
    { "key": "EmptyValue", "value": "" }
  ]
}
```

The first run should use the actual Workbench closure and the public CLI wrapper. A fixture host may be used afterward for controlled refusal cases, but it cannot replace the actual Workbench enrollment proof.

Declare the resource and connection names in the captured source before selecting them or supplying their private values. A syntactically valid name introduced only by the private overlay must not become a public target identity. The automated Workbench fixture retains all four real JSON layers, trims feature selections in both shell layers, and imports/accepts the four intended requested IDs; host-implied dependencies are inspected separately.

## Public command journey

Run the planned command with a built Workbench output directory and a separate source directory:

```text
dotnet elsa composition inspect \
  --host <built-workbench-output> \
  --host-dir <temporary-source-directory> \
  --shell default \
  --environment Production \
  --composition <accepted-composition.json> \
  --environment-input <private-environment-input.json> \
  --catalog <catalog.json> \
  --trust-host-code \
  --format json
```

The implementation must produce one safe validated projection whose source is `captured-workbench-json-explicit-environment-v1` and whose external input state is `supplied-intended`. It may report safe logical targets and truthful unverified/not-performed evidence. It must not print the private values, raw configuration, private input path, exception excerpt, service-prefix expansion, or a private-input fingerprint.

Run the existing command without `--environment-input` as the compatibility control:

```text
dotnet elsa composition inspect \
  --host <built-workbench-output> \
  --host-dir <temporary-source-directory> \
  --shell default \
  --environment Production \
  --composition <accepted-composition.json> \
  --catalog <catalog.json> \
  --trust-host-code \
  --format json
```

The old command must retain candidate-v1/file-only behavior. A host without the new declaration must refuse the new flag without falling back to ambient or command-line input.

## Recovery journey

1. Start with an accepted file-only composition and a valid explicit environment document.
2. Change the explicit overlay so that the effective feature selection diverges from accepted intent.
3. Confirm inspection refuses before EF preparation and does not rewrite the accepted file.
4. Edit the authored selection intent and use the existing interactive `composition accept` workflow to publish a fresh accepted file.
5. Start a fresh inspection capture with the same selected context and explicit file.
6. Confirm a matching selection can proceed and that the source files and operator-owned private input remain byte-identical.

This journey proves an achievable external-toggle recovery. It must not assume that `composition accept` silently observes or authorizes an environment change.

## Planned automated commands

Run the existing affected projects after implementation, using the repository's normal build-slot wrapper and recording discovered/selected cases:

```text
bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603
dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
dotnet build src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603
dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj -c Release --no-restore
dotnet test tests/essentials/Modularity/Planning/Tests/Elsa.Modularity.Planning.Tests.csproj -c Release --no-restore
dotnet test tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/Elsa.Persistence.EntityFrameworkCore.Migrations.Tests.csproj -c Release --no-restore
dotnet test tests/essentials/Architecture/Elsa.Architecture.Tests.csproj -c Release --no-restore
dotnet test tests/essentials/Persistence/EntityFrameworkCore/CliAcceptance/ProviderTests/Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests.csproj -c Release --no-restore
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

The architecture guard compares evaluated Release and isolated Debug restore graphs. The existing restore script supplies both; a fresh worktree without those graphs cannot run the full guard. Use the normal build-slot wrapper and preserve the two graph locations rather than bypassing the guard.

The implementation proof must include the public `DotnetElsa.cs` process wrapper against the built Workbench output, not only in-process fixture calls. Existing fixture hosts cover controlled protocol and refusal cases. The real database/provider acceptance project remains separate and must not be used to claim that the inspection operation accessed a database.

## Required negative and boundary journeys

Execute the rows in [acceptance-proof-matrix.md](contracts/acceptance-proof-matrix.md), including:

- all eleven service-prefix families, ordinary `ConnectionStrings__` support, aliases, duplicates, Unicode/control/`=`/surrogate/NUL/null/tombstone/blank/omitted values;
- exact and one-over raw 1 MiB, per-file 1 MiB, aggregate 4 MiB, key 1,024-byte, value 65,536-byte, entry 1,024, combined participant-plus-finding projection 1,024 (1,022+2 accepted and 1,023+2 refused), serialized request 8 MiB, response 4 MiB, selection, JSON depth, and timeout limits;
- stable reload of a detached frozen root and caller-owned document/dictionary mutation after defensive copying; separately, stale/mixed/disposed/reused captures, source drift before launch and after response, old candidate parser with an extra field, new capability absent, and actual host unenrolled;
- cancellation during stdin write, response read, after capture before launch, and bounded child cleanup;
- private canaries inspected across public output, logs, diagnostics, child process arguments, generated/public artifacts, and fingerprints while allowing only finite existing CLI location and assembly-loader/deps/package-root metadata needed for file checks/loading. Private values, raw configuration, private input paths, and private-input-derived fingerprints remain absent.

The expected outcome for each refusal row is a fixed refusal with no partial public result, no side effect, and owned cleanup. Frozen-root reload and post-copy caller mutation are the stability controls and must remain unchanged, not refusals. This quickstart records planned validation only; it does not mark any row passed.
