# Validation guide: Offline composition plan

Use the focused fixture files in the owning CLI and planner tests. The examples below describe the review scenarios; they do not imply a published production profile catalog.

1. Run `dotnet elsa composition plan --catalog <catalog.json> --composition <composition.json> --format json` for a profile `{A,B}`, group `{B,C}`, addition `D`, removal `B`. Expect exact candidate `{A,C,D}`, intact accepted IDs, sorted source reasons, `inventory-unverified`, and `persistence-unverified`. No input file changes.
2. Add `--inventory <inventory.json>` with loaded `A -> B` required and a reviewed optional `A -> C`. Expect a missing required edge sourced to runtime descriptor, a separate reviewed optional row, and no implicit `B` or `C` addition. An empty loaded descriptor list is authoritative; absent descriptor evidence is not.
3. Add `--persistence-evidence <resource-hints.json>` naming `primary`. Expect only the safe name and `unchecked` status. A file claiming `checked` or containing a connection string refuses.
4. Repeat equivalent inputs with reordered set members and object keys. Expect identical JSON bytes when inventory identity/time is unchanged. Change inventory time and expect evidence identity to change.
5. Put `Server=db;Password=sentinel` in opaque authored settings/resources, definition rationale, malformed labels and a path. Expect zero occurrences of `sentinel` on stdout/stderr, including refusals.
6. Delete one input file and corrupt another. Expect exit 3 for missing file, exit 2 for malformed content, and no partial JSON. Run from a directory with read-only inputs and no host/DB installed; planning still completes.

Run whole test projects:

```bash
dotnet test tests/essentials/Modularity/Planning/Tests/Elsa.Modularity.Planning.Tests.csproj

dotnet test tests/essentials/Cli/Tests/Elsa.Cli.Tests.csproj
```

Then run the architecture restore/guard and generated maps check described in [plan.md](plan.md). No EF container suite is required locally for this command; CI still exercises its configured matrix until [#1993](https://github.com/elsa-workflows/elsa-foundation/issues/1993) changes PR selection.
