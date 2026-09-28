# Package version calculator and publisher

Computes every package's version, the affected set and each package's input fingerprint for one commit, against
one revision of the last-published record
([spec 150](../../specs/150-package-version-computation/spec.md),
[ADR 0067](../../docs/adr/0067-package-versioning-uses-two-lines-with-computed-patch.md)). Packing (#2080) and
publishing (#2082) read its output; it packs, pushes and records nothing itself. The publisher beside it does the
pushing and the recording, and the Packages workflow runs it ([Publishing](#publishing)).

```bash
dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
  --record-ref origin/publish-state [--commit HEAD] [--output computation.json] \
  [--pack-properties package-versions.props --branch main] \
  [--force-advance Elsa.Tasks,Elsa.Http --force-reason "<why>"]

dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
  --record published-versions.json [--commit <revision>] [--repo <dir>] [--output <file>]
```

`--record-ref` reads `published-versions.json` (or `--record-path`) from a revision without checking it out;
`--record` reads a file. `--commit` defaults to `HEAD`, `--repo` to the current directory, and the JSON goes to
standard output unless `--output` names a file. `--pack-properties` also writes the MSBuild file that packs the
computation from the branch `--branch` names ([Packing](#packing)); the two go together. `--force-advance` and
`--force-reason` advance named packages whose inputs did not change ([Force-advance](#force-advance)); they go
together too.

| Exit | Meaning |
|---|---|
| 0 | Computed. |
| 1 | A publish gate refused: monotonicity (FR-012), or forward-only (FR-021), which includes a latest publish missing from the repository. Nothing may be published. |
| 2 | Invalid input: usage, an unreadable record, a stale or unknown-schema dependency map, a record entry whose commit is missing, an import it cannot resolve, a force-advance naming no package, a package twice, a package the commit lacks, or no reason. |

## What it reads

Only git objects: the commit being built, the commit each record entry names, and the
[dependency map](../../docs/maps/dependency-map.json) committed in each of them, which is its sole source of
package ids, version lines, project paths, ownership and the packages each nuspec lists. It reads schema version 2
of the map and refuses any other, version 1 included: that one records no pinned-transitive edges, so it cannot say
which packages a pin reaches, and a record entry naming a commit that carries it stops the computation rather than
comparing against an empty set. It never reads the working tree, a clock, the
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
`Directory.Packages.props` entries of every external package its nuspec lists (FR-003): each it references
directly, and each the dependency map records as a pinned-transitive edge, a package it reaches only through its other
dependencies whose pin `CentralPackageTransitivePinningEnabled` writes into its nuspec. So a pin bump advances exactly
the packages whose nuspec lists that package, and a package whose set of pinned-transitive edges changes advances
although no pin moved; a reason such as `Directory.Packages.props: Microsoft.OpenApi, pinned transitively (changed)`
names the edge. Spec 150's [Decisions](../../specs/150-package-version-computation/spec.md#decisions) record exactly
which files those are, and the rules on top: Line A moving as one, tool packages, and a major change reaching the
packages that reference it.

## Force-advance

`--force-advance` names packages to advance although none of their inputs changed, separated by commas or whitespace,
and `--force-reason` says why: the escape hatch at publish time for a change no input shows (FR-003). Each named
package advances exactly as a changed one does — to one past its recorded patch, with Line A moving as one and a tool
carrying it moving too — and carries `force-advanced: <reason>` among its `reasons` in the output, beside any input
that did change, so it still advances once. The monotonicity gate (FR-012) holds for it like any other package.
Package ids compare case-insensitively; a name the commit has no packable project for, a name given twice, or a blank
reason is refused with exit 2 rather than advancing nothing. The force is not recorded anywhere but the output: the
next computation, without it, compares the package against the record as usual.

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

The Docker Images workflow builds the Workbench image through the same file, so the image's Elsa assemblies and its
`deps.json` carry the versions the feed does (#2084). Its restore reads the file too: `deps.json` takes a project
reference's version from the restore, not the build. A pull request plans its own file, at branch-scoped versions,
exactly as above. A push to main does not plan again - planning twice for the same commit would race the Packages
workflow's own plan and write-back to `publish-state` (spec 150 FR-020) - it instead takes the file the Packages
run for that commit already produced, from that run's `elsa-foundation-nuget-packages` artifact, so its Elsa package
versions are exactly what Packages computed (and, on a publish, exactly what it wrote to the feed).

The file sets `ElsaVersionComputationCommit`, and for each packable project, selected by project name,
`ElsaComputedPackageVersion` and `ElsaInputFingerprint`. [`PackageVersioning.props`](../../PackageVersioning.props) at
the repository root reads them for every project under `src/`, and makes the computed version both the package's and
its assemblies' version. The file is a pure function of its inputs and is
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
  [`PrereleaseLabel`](Elsa.Versioning.Calculator/PrereleaseLabel.cs) is the one implementation. A branch build packs
  for CI artifacts and never pushes: its label names the branch, not the commit.
- **Ranges (FR-007), on Elsa packages only.** Every range on a project reference stops below its floor's
  next major, `[x.y.z, (x+1).0.0)`; the floor is the referenced package's version above. A third party's range is left
  exactly as central package management restores it - the plain version `Directory.Packages.props` names, with no
  upper bound - as the owner decided (spec 150 Decisions). After pack writes the nuspec, a last check fails
  the pack and deletes the package when a range on one of this repository's own packages is bounded anywhere else.
  Every one of these effects is gated on this file's own `ElsaVersionComputationCommit`: a pack that does not import
  it - a dev pack, or a local pack with only a global `/p:Version` - bounds nothing and carries no fingerprint, the
  same shape packing has always had.
- **Fingerprint and source commit (FR-018).** The fingerprint is `elsa-input-fingerprint.json` at the package root,
  `{"schema_version":1,"fingerprint":"sha256:<hex>"}`: NuGet has no custom nuspec metadata, and silently drops an
  element it does not know. The source commit is the nuspec's `<repository commit="…"/>`, and the pack fails unless
  it is the commit the working tree holds.

Without the file, a pack is a dev pack: every package under `src/` is `<its line's major.minor>.0-dev`, which sorts
below every computed version, and carries no fingerprint. Its assemblies keep the SDK's default version, so a dev
build's activity versions and assembly-qualified names are what they were before package versions were computed.
Projects outside `src/` are left as their own properties have them. Nothing else may set `PackageVersion`, nor, with
the file, `Version`; the build fails when something did, except that without the file a global `/p:Version` becomes
the package version, so a local pack can still name one. `packages.yml` passes none (FR-005).
`PackageVersioning.props` lists each check and its code.

## Publishing

[`Elsa.Versioning.Publisher`](Elsa.Versioning.Publisher) holds the logic of
[`.github/workflows/packages.yml`](../../.github/workflows/packages.yml), with the feed (`IPackageFeed`, the NuGet V3
protocol) and the record branch (`IPublishState`, git) behind interfaces; the workflow only runs its two commands.

```bash
dotnet run --project tools/versioning/Elsa.Versioning.Publisher -- plan \
  --ref "$GITHUB_REF" --commit "$GITHUB_SHA" --output "$RUNNER_TEMP/versions" [--bootstrap true|false] \
  [--force-advance <ids> --force-reason <text>] [--github-output <file>] [--summary <file>] [--remote origin]
dotnet run --project tools/versioning/Elsa.Versioning.Publisher -- publish \
  --ref "$GITHUB_REF" --commit "$GITHUB_SHA" --plan-dir "$RUNNER_TEMP/versions" \
  --feed <service index URL> --api-key-env FEEDZ_API_KEY [--summary <file>] [--remote origin]
```

Both exit 0 when done, 1 when they refused or a publish failed, and 2 on invalid input. An empty `--force-advance`
with an empty `--force-reason` means none, so the workflow passes its dispatch inputs as they are.

**`plan`**, the pack job's first step, asks the remote whether `publish-state` exists (an unanswered question fails;
it is never taken for "no") and decides the run's mode:

| Run | `publish-state` | Mode | Computed against | Pushes |
|---|---|---|---|---|
| Any branch but `main` | either | `branch` | the newest record revision whose latest publish the commit descends from, else an empty record | never (FR-008) |
| `main` | absent | `dry-run` | an empty record: the bootstrap's versions | nothing; the job summary says the bootstrap is pending |
| `main`, `bootstrap: true` | absent | `bootstrap` | an empty record | every package, then creates the record |
| `main` | exists | `publish` | its tip | the affected set |

The bootstrap is refused off `main` and once `publish-state` exists. The plan writes `plan.json`, the calculator's
`computation.json`, its `package-versions.props` for the branch, and `packages.slnf`, a solution filter holding
exactly the affected set, which the workflow restores, builds and packs through the props file into `nuget/`.

**`publish`**, the publish job, pushes only for a plan of `main` in the `publish` or `bootstrap` mode. Before pushing
anything it asks the remote for `publish-state` again, and refuses when a publish finds it moved since the plan (the
versions may no longer be the ones to publish) or the bootstrap finds it existing; it recomputes and requires the
calculator's output to be the plan's bytes, which runs the forward-only gate (FR-021) again; and it requires the
packages to be exactly the affected set, each at its computed version with its computed fingerprint. Then:

- **The bootstrap** also requires an empty record and every package in the set, and lists each package's versions on
  the feed: it refuses, naming them, while any version does not sort below the one it would push — the old
  `4.0.0-preview.N` and branch builds, or anything newer — and when the feed cannot list them.
- **Pushes** go in dependency order, each package after every package of the set its nuspec depends on, and the
  publish stops at the first failure, so no package is pushed with a range on a version the feed does not hold as
  computed. No push skips a duplicate (FR-011). A 409 is settled by the input fingerprint of the feed's copy (FR-018):
  the same fingerprint means an earlier run pushed it and did not record it, so it counts as published; another
  fingerprint, or one that cannot be read, fails the publish and leaves the feed's copy alone. 200, 201 and 202 are
  a push taken; any other answer, or none, is a failure.
- **The write-back** records what reached the feed, even when the publish stopped part way, and nothing else (FR-006,
  FR-014): each package's entry names the version and the commit built — for a package the feed already held, the
  commit built too, whose inputs the equal fingerprint shows are the same — and `last_publish_commit` names the commit
  built. It is one commit on `publish-state`, `published-versions.json` its only file, on top of the tip the plan
  named, or an orphan for the bootstrap, pushed without force with the checkout's `GITHUB_TOKEN` (FR-017). A rejected
  push fails the run.

A run that dies between its pushes and its write-back, or whose write-back is rejected, leaves packages on the feed that
the record does not name. The next run from `main` computes the same versions for them, the feed rejects them, and
the equal fingerprints record them, with nobody intervening. The next run must see the same inputs for those
packages; if a later commit changed one of them before any run recorded it, the feed's copy no longer matches and
the publish fails as a collision for the repair workflow (FR-019, #2083). A bootstrap that stops part way records what
it pushed, so `publish-state` exists and the next push to `main` publishes the rest at their first versions.

The workflow serializes every run from `main`, from its plan to its write-back, on the concurrency group
`publish-state`, and never cancels one in progress (FR-020); anything else that writes `publish-state` joins that
group. A GitHub Release publishes nothing until the 4.0 release cut (#2085): its job fails and says so.

To rehearse against a scratch feed, run `plan`, pack as the workflow does, and `publish` with `--feed` at the scratch
feed and `--remote` at a scratch repository, which then holds the record.

## Tests

`tests/essentials/Versioning/Calculator/Tests` builds synthetic histories in throwaway repositories — moves, renames,
deletions, re-adds, reverts, a rewritten `main`, partial publishes, pins reached only transitively, force-advances —
and runs in CI's fast test job, as do the label and pack-properties tests beside them.
`tests/essentials/Architecture/PackageVersionBuildCheckTests.cs` and `PackageVersioningPackTests.cs` prove the MSBuild
side against real builds and packs, and `PinnedTransitivePackTests.cs` that pack writes exactly the pinned-transitive
packages restore lists, which is what the dependency map records.

`tests/essentials/Versioning/Publisher/Tests` runs the publisher's plan and publish over the same synthetic histories,
with a bare repository as the remote holding `publish-state` and an in-memory feed that answers 201, 409 with the
same fingerprint and 409 with another: the dry run, the bootstrap and each of its refusals, one changed module publishing
alone, a crash between push and write-back recovered by the next run, a collision failing without overwriting, the
forward-only gate, and branch builds that never push. `NuGetFeedTests` pins how each HTTP answer maps, and
`PackagesWorkflowTests` holds the workflow to FR-005, FR-008, FR-011 and FR-020.
