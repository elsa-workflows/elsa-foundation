# Package version calculator

Computes every package's version, the affected set and each package's input fingerprint for one commit, against
one revision of the last-published record
([spec 150](../../specs/150-package-version-computation/spec.md),
[ADR 0067](../../docs/adr/0067-package-versioning-uses-two-lines-with-computed-patch.md)). Packing (#2080) and
publishing (#2082) read its output; it packs, pushes and records nothing itself.

```bash
dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
  --record-ref origin/publish-state [--commit HEAD] [--output computation.json] \
  [--pack-properties package-versions.props --branch main]

dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
  --record published-versions.json [--commit <revision>] [--repo <dir>] [--output <file>]
```

`--record-ref` reads `published-versions.json` (or `--record-path`) from a revision without checking it out;
`--record` reads a file. `--commit` defaults to `HEAD`, `--repo` to the current directory, and the JSON goes to
standard output unless `--output` names a file. `--pack-properties` also writes the MSBuild file that packs the
computation from the branch `--branch` names ([Packing](#packing)); the two go together.

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

## Packing

`dotnet pack` takes each package's version and input fingerprint from one MSBuild file the calculator writes with
`--pack-properties`, for the commit, the record revision and the branch. The build and the pack both import it
through MSBuild's `CustomBeforeDirectoryBuildProps` hook, which takes a full path; the build must see it too,
because it stamps the version into the assemblies and `elsa-package.json`:

```bash
dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
  --record-ref origin/publish-state --output computation.json \
  --pack-properties "$PWD/package-versions.props" --branch "$BRANCH"
dotnet build Elsa.Server.pack.slnf -c Release -p:CustomBeforeDirectoryBuildProps="$PWD/package-versions.props"
dotnet pack Elsa.Server.pack.slnf -c Release --no-build -p:CustomBeforeDirectoryBuildProps="$PWD/package-versions.props"
```

The file sets `ElsaVersionComputationCommit`, and for each packable project, selected by project name,
`ElsaComputedPackageVersion` and `ElsaInputFingerprint`. [`PackageVersioning.props`](../../PackageVersioning.props) at
the repository root reads them for every project under `src/`, and makes the computed version both the package's and
its assemblies' version, as `packages.yml`'s `/p:Version` does today. The file is a pure function of its inputs and is
never committed, so the calculator never reads it as an input; a hook rather than an import in the root build files,
because the calculator refuses an import whose path it cannot resolve statically.

- **Version.** A package in the affected set carries its computed `major.minor.patch` with the branch's label. Every
  other package keeps the exact version its record names, label and all: that is the version on the feed, so the
  ranges of packages referencing it start there (SC-002). Packing a package outside the affected set is therefore
  harmless but pointless, and FR-006a leaves it out.
- **Label (FR-008).** A build from `main` carries `ElsaPrereleaseLabel` from `VersionLines.props` at the commit:
  `preview`, with no counter, while the lines are unreleased, and nothing once that property is emptied. Any other
  branch carries `branch-<name>`: a leading `refs/heads/` dropped, lowercased, every run of characters other than
  `a-z` and `0-9` made one `-`, `-` trimmed from both ends, cut to 40 characters and trimmed again. `feat/Issue_2080`
  gives `4.0.9-branch-feat-issue-2080`. No `main` build carries that prefix, and it sorts below `preview`.
  [`PrereleaseLabel`](Elsa.Versioning.Calculator/PrereleaseLabel.cs) is the one implementation.
- **Ranges (FR-007).** Every dependency range stops below its floor's next major, `[x.y.z, (x+1).0.0)`. A project
  reference's floor is the referenced package's version above; a package's is its `Directory.Packages.props` version,
  bounded when a packable project restores. Resolution does not change: NuGet still restores the lowest version a
  range admits. After pack writes the nuspec, a last check fails the pack and deletes the package when any range is
  bounded anywhere else.
- **Fingerprint and source commit (FR-018).** The fingerprint is `elsa-input-fingerprint.json` at the package root,
  `{"schema_version":1,"fingerprint":"sha256:<hex>"}`: NuGet has no custom nuspec metadata, and silently drops an
  element it does not know. The source commit is the nuspec's `<repository commit="…"/>`, and the pack fails unless
  it is the commit the working tree holds.

Without the file, a pack is a dev pack: every package under `src/` is `<its line's major.minor>.0-dev`, which sorts
below every computed version, and carries no fingerprint. Its assemblies keep the SDK's default version, so a dev
build's activity versions and assembly-qualified names are what they were before package versions were computed.
Projects outside `src/` are left as their own properties have them. Nothing else may set `PackageVersion`, nor, with
the file, `Version`; the build fails when something did, except that without the file a global `/p:Version` becomes
the package version, because `packages.yml` still stamps one on every package until #2082 replaces it.
`PackageVersioning.props` lists each check and its code.

## Tests

`tests/essentials/Versioning/Calculator/Tests` builds synthetic histories in throwaway repositories — moves, renames,
deletions, re-adds, reverts, a rewritten `main`, partial publishes — and runs in CI's fast test job, as do the label and
pack-properties tests beside them. `tests/essentials/Architecture/PackageVersionBuildCheckTests.cs` and
`PackageVersioningPackTests.cs` prove the MSBuild side against real builds and packs.
