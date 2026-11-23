# Architecture baselines

The frozen ASP.NET Core Identity EF oracle, its ratchet, and the repository-wide `ef-core-surface.json`
inventory were all retired (issue #1482). EF Core absence is now asserted by
`EfCoreDependencyGuardTests`, which walks the declared csproj graph and needs no baseline or restore.

`schema-upcaster-fixtures.sha256` freezes every committed upcaster fixture pair (spec 180, FR-022): one
`<sha-256>  <repo-relative path>` line per file under a `Fixtures/SchemaUpcasters` directory, hashed with line endings
normalized. Nothing checks it today: the guard that read it scanned source and was deleted (#2573), and #2606 decides
whether a compiled check restores it or the file goes. Until then, add the line once the fixture's version ships and
never change one; review holds that.

Each fixture is one row of one table, every content column the family declares for it, at
`<family>/<from>-to-<to>/<table>.source.json` and `.expected.json`, the table named by the type its `[EfSchemaContent]`
declaration maps to it (#2144). The synthetic family's per-column fixtures were replaced by per-row ones when upcasters
moved from columns to rows; no version of that family ever shipped, so no shipped fixture was changed.
