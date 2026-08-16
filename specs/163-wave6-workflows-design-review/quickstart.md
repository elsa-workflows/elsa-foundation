# Quickstart: Wave 6 Review Corrections

From the repository root:

```bash
dotnet test tests/Elsa/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj --no-restore -v:minimal
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore -v:minimal
dotnet build Elsa.Server.slnx --no-restore -v:minimal
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

The focused suite must report all Workflows Design compatibility, binding, authorization, semantic,
and lifecycle cases passing. Architecture must report no transition-ratchet or composition failure.
The maps check must report no stale generated files.

For backend E2E, follow `e2e-tests/README.md`: rebuild Workbench, stop it, create a fresh SQLite DB,
apply the reference-composition schema, start the HTTP profile, and run the Workflows Design backend
scenario. Record the exact command, commit, database reset, server URL, and result in
`docs/reports/workflows-design-api-migration-2026-08.md`.
