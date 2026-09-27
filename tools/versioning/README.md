# Package version calculator

Computes every package's version, the affected set and each package's input fingerprint for one commit, against
one revision of the last-published record
([spec 150](../../specs/150-package-version-computation/spec.md),
[ADR 0067](../../docs/adr/0067-package-versioning-uses-two-lines-with-computed-patch.md)). Packing (#2080) and
publishing (#2082) read its output; it packs, pushes and writes nothing itself.

```bash
dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
  --record-ref origin/publish-state [--commit HEAD] [--output computation.json]

dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
  --record published-versions.json [--commit <revision>] [--repo <dir>] [--output <file>]
```

`--record-ref` reads `published-versions.json` (or `--record-path`) from a revision without checking it out;
`--record` reads a file. `--commit` defaults to `HEAD`, `--repo` to the current directory, and the JSON goes to
standard output unless `--output` names a file.

| Exit | Meaning |
|---|---|
| 0 | Computed. |
| 1 | A publish gate refused: monotonicity (FR-012), or forward-only (FR-021), which includes a latest publish missing from the repository. Nothing may be published. |
| 2 | Invalid input: usage, an unreadable record, a stale or unknown-schema dependency map, a record entry whose commit is missing, an import it cannot resolve. |

## What it reads

Only git objects: the commit being built, the commit each record entry names, and the
[dependency map](../../docs/maps/dependency-map.json) committed in each of them, which is its sole source of
package ids, version lines, project paths and ownership. It never reads the working tree, a clock, the
environment or a feed, and it can run only the read-only git commands `cat-file`, `ls-tree`, `merge-base` and
`rev-parse`, so the same commit and record revision give byte-identical output on any machine (FR-009, SC-008).
A shallow clone lacks the commits it compares; fetch full history.

Each line's `major.minor` comes from `ElsaVersion` (Line B) and `ElsaContractsVersion` (Line A) in
`VersionLines.props` at the commit being built (FR-010); the record supplies only the patch.

## The last-published record, `published-versions.json` (schema 1)

```json
{
  "schema_version": 1,
  "last_publish_commit": "<the main commit the most recent publish was built from>",
  "packages": [
    { "package_id": "Elsa.Tasks", "version": "4.0.7-preview", "commit": "<the commit that version was built from>" }
  ]
}
```

Entries are ordered by package id and never removed; package ids compare case-insensitively, as a feed compares
them. Commits are full lowercase object ids. `last_publish_commit` is null only while there are no entries, before
the first publish. Reading is strict: an unknown schema version or property, a duplicate id, or a malformed version
or commit is refused. `PublishedVersions.Serialize()` is the one serialization.

## Output (schema 1)

```json
{
  "schema_version": 1,
  "commit": "<the commit built>",
  "last_publish_commit": "<from the record>",
  "lines": { "A": "4.0", "B": "4.0" },
  "affected": ["Elsa.Tasks"],
  "packages": [
    {
      "package_id": "Elsa.Tasks",
      "path": "src/essentials/Tasks/Elsa.Tasks.csproj",
      "line": "B",
      "version": "4.0.8",
      "affected": true,
      "last_published": { "version": "4.0.7-preview", "commit": "<sha>" },
      "fingerprint": "sha256:<hex>",
      "reasons": ["src/essentials/Tasks/Scheduler.cs (changed)"]
    }
  ]
}
```

Every packable project is listed, ordered by package id. `version` is `major.minor.patch`; packing adds the
prerelease label (FR-008). An unaffected package keeps its recorded version and is not packed. `reasons` names the
inputs that differ from the package's last publish, or the rule that moved it. `fingerprint` is the input
fingerprint to stamp into the package (FR-018): a SHA-256 over exactly the inputs change detection compares.

## What counts as changed

A package is changed when its package-affecting inputs differ from those at the commit its record entry names —
compared as two trees, each read through its own dependency map, so a move is a change and never runs a version
backwards (SC-010). Its inputs are the files its project owns, the build files every build of it reads, and the
`Directory.Packages.props` entries of the packages it references. Spec 150's
[Decisions](../../specs/150-package-version-computation/spec.md#decisions) record exactly which files those are, and
the rules on top: Line A moving as one, tool packages, and a major change reaching the packages that reference it.

## Tests

`tests/essentials/Versioning/Calculator/Tests` builds synthetic histories in throwaway repositories — moves, renames,
deletions, re-adds, reverts, a rewritten `main`, partial publishes — and runs in CI's fast test job.
