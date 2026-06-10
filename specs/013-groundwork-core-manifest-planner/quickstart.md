# Quickstart: Groundwork Core Manifest And Planner Kernel

Use this guide to validate G1 after implementation.

## 1. Build Groundwork Kernel Projects

```bash
dotnet build src/Groundwork/Core/Groundwork.Core.csproj
dotnet build src/Groundwork/Relational/Groundwork.Relational.csproj
dotnet build src/Groundwork/Documents/Groundwork.Documents.csproj
```

Expected result:

- All three projects build.
- No provider package is required.
- No Elsa project is referenced.

## 2. Run Groundwork Tests

```bash
dotnet test tests/Groundwork/Groundwork.Tests/Groundwork.Tests.csproj
```

Expected result:

- Manifest validation tests pass.
- Provider capability tests pass.
- Relational and document planner contract tests pass.
- Groundwork dependency boundary tests pass.

## 3. Confirm Solution Wiring

```bash
dotnet test Elsa.Server.slnx --no-restore
```

Expected result:

- The solution recognizes the new Groundwork projects and test project.
- Existing Elsa projects are not required by generic Groundwork projects.

## 4. G2 Readiness Check

G2 can start when:

- A sample manifest can produce document planner output.
- Document plan output includes envelope and index requirements.
- Schema-history intent is represented.
- Unindexed portable queries fail clearly.
- Provider capability gaps are structured and test-covered.
