# Quickstart: Finite JSON Lines ETL input

## Focused tests

```bash
dotnet test Extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj --filter "FullyQualifiedName~Jsonl|FullyQualifiedName~Zero_record|FullyQualifiedName~Empty_file_preparation|FullyQualifiedName~One_source_file_approval|FullyQualifiedName~Persisted_v2_artifacts_keep"
pnpm --dir Studio --filter @elsa-workflows/studio-etl test
pnpm --dir Studio test:browser -- etl-connector-authoring
```

## Actual host journey

```bash
dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true
python3 specs/1190-etl-jsonl-input/evidence/jsonl_workbench_journey.py --output <fresh-directory>
```

The driver picks a free loopback port and never touches a port it did not open (5095 is the owner's own Workbench). It writes the fixture files, settings, logs and a `summary.json` to the output directory, and stops only the host it started.
