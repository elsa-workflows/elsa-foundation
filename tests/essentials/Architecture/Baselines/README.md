# Architecture baselines

The frozen ASP.NET Core Identity EF oracle, its ratchet, and the repository-wide `ef-core-surface.json`
inventory were all retired (issue #1482). EF Core absence is now asserted by
`EfCoreDependencyGuardTests`, which walks the declared csproj graph and needs no baseline or restore.

`schema-upcaster-fixtures.sha256` freezes every committed upcaster fixture pair (spec 180, FR-022): one
`<sha-256>  <repo-relative path>` line per file under a `Fixtures/SchemaUpcasters` directory, hashed with line endings
normalized. `EfSchemaFamilyFixtureProofGuardTests` fails the build when a fixture is edited or deleted, or committed
without a line here. Add the line once the fixture's version ships; never change one.

Each fixture is one row of one table, every content column the family declares for it, at
`<family>/<from>-to-<to>/<table>.source.json` and `.expected.json`, the table named by the type its `[EfSchemaContent]`
declaration maps to it (#2144). The synthetic family's per-column fixtures were replaced by per-row ones when upcasters
moved from columns to rows; no version of that family ever shipped, so no shipped fixture was changed.
