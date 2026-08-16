# Wave 2 Verification Quickstart

1. Restore the architecture project: `dotnet restore tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj`.
2. Run the legacy capture before production edits and review the emitted route count, 401 cases, authenticated bodies/errors, multipart binding, and OpenAPI projection.
3. Run focused Wave 2 compatibility and authorization tests through TestServer.
4. Run each affected feature test project, `Elsa.Api.Compatibility.Testing`, architecture tests, and the relevant backend E2E suites against a rebuilt Workbench/fresh database.
5. Run `dotnet build Elsa.Server.slnx --no-restore`, `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, and a diff/self-review.
6. After Wave 1 is available, rebase and remove exactly the 13 target transition entries, then rerun the architecture transition count (156 to 143).

The open nightly issue #1323 is tracked separately; only owner-specific failures from these runs belong to this work unit.
