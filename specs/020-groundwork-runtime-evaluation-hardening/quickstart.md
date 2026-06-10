# Quickstart: Groundwork Runtime Evaluation And Hardening

## Validate Runtime Evaluation Tests

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
```

Expected result: evaluator tests classify runtime-defined business data as Groundwork default and classify workflow runtime hot paths as benchmark-gated or specialized.

## Validate Full Regression

```bash
dotnet test Elsa.Server.slnx --no-restore
```

Expected result: all Groundwork and Elsa tests pass.

## Review Decision Artifact

Open `docs/reports/groundwork-runtime-evaluation.md`.

Expected result: the report lists go/no-go recommendations and required evidence gates before runtime migration.
