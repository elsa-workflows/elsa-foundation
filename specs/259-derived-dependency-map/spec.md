# Feature Specification: The calculator derives the dependency map, so it is never committed

**Feature Branch**: `agent/claude/259-derived-dependency-map`
**Created**: 2026-10-20
**Status**: Implemented
**Input**: Follow-up to spec 257 ([#3116](https://github.com/elsa-workflows/elsa-foundation/pull/3116), merged as `d628fc0da`): "the versioning calculator derives the dependency-map dataset from each commit's own tree, so `docs/maps/dependency-map.json` no longer has to be committed." Spec 257 kept the file committed (its Decision 2 and research R10) because the calculator reads it from every commit it compares.

## Evidence and Context

All findings were verified against `main` at `d628fc0da` on 2026-10-20.

- `docs/maps/dependency-map.json` is the last generated documentation file on `main`. It has changed in 89 commits since it was added on 2026-09-26 (#2088). It changes whenever a project, a project reference, a package reference, a central pin, a lock file's pinned-transitive set or the Line A list changes. Two PRs that each add a project both edit it, so the second one conflicts, and `Generated maps fresh` turns red on any PR that forgot to regenerate it.
- The versioning calculator (`tools/versioning/Elsa.Versioning.Calculator`) reads the file from the git tree of the commit being built and of every commit the last-published record names (`CommitSnapshot.cs:50-53`). It refuses a commit that has no such file (`:51-53`) and a built commit whose file does not describe its projects or its Line A list (`VersionCalculator.cs:138-155`). The file gives it each project's path, whether it packs, its package id, its version line, and its internal, external and pinned-transitive edges (`DependencyMapDataset.cs`).
- The maps generator (`tools/maps/Elsa.Maps.Generator`) writes the file from one scan of the project files (`ProjectGraph.Read`, `ProjectFacts.cs`; `ProjectFile.cs`; `PackageVersions.cs`) plus the pinned-transitive edges it reads from the committed `packages.lock.json` beside each packable project (`PinnedTransitiveDependencies.cs`). It reads `VersionLines.props`, `Directory.Packages.props`, each project file and the nearest `Directory.Build.props`. It needs no restore. Today it reads them from the working tree.
- The last-published record on the `publish-state` branch has had 150 revisions. Across them it names 149 distinct commits, each as the last publish of some package. Every one of those commits has a committed dataset with schema version 2. 104 of them use the pre-consolidation layout (`src/`, `tests/`), which today's generator no longer scans; 45 use the `Essentials/` and `Extensions/` layout.
- The calculator writes each package's input fingerprint into the package, and the publisher compares fingerprints when a feed rejects a push of an existing version (spec 150 FR-018). A package's inputs are resolved through the dataset (file ownership by longest project directory, edges, line). If the dataset for a commit changed, the inputs computed for it would change too. The calculator could then advance a package that did not change, or settle a rejected push wrongly.

## Decisions

1. The calculator obtains the dataset for a commit from that commit's own git objects. No checkout, restore, working tree, network or clock is involved. The result is a function of the commit, as today (spec 150 FR-009).
2. A commit that has a committed dataset keeps using it, byte for byte. Only a commit that has no committed dataset has its dataset derived. History stays exactly as it was read when those packages were published, including the 104 pre-consolidation commits that today's scanner cannot read. This keeps every recorded fingerprint stable by construction. After the switch, every new `main` commit is derived.
3. The calculator and the generator use one implementation of the scan. It is extracted from the generator into a library that both reference. Neither keeps its own copy. The repository rule that duplication beats a dependency applies only to 5–10 lines; this scan is about 600.
4. After the switch, the generator keeps writing the dataset locally with `-- dependency-map` and `-- all`, at the same git-ignored path. CI publishes it on the `generated-maps` branch with the other maps.
5. `Generated maps fresh` keeps its name and stays required. It guards the committed solution filters and runs the generator's self-tests. It no longer compares a committed dataset, because there is none.
6. The switch lands only after a recorded comparison shows identical results both ways: for recent `main` commits, the versions, affected sets and fingerprints computed from the committed dataset equal those computed from the derived one.

## User Scenarios & Testing

### User Story 1 - Concurrent project PRs never conflict on the dependency map (Priority: P1)

A contributor or agent opens a PR that adds a project, a project reference or a package reference while other such PRs are in flight. After another PR merges, theirs still merges cleanly. No generated file has to be regenerated or resolved first, and `Generated maps fresh` does not go red because a dataset is stale.

**Why this priority**: This removes the last generated file that conflicts between concurrent PRs, which is the reason for the spec.

**Independent Test**: Create two branches from the same `main` commit. Each adds one new packable project with its lock file. Merge the first, then confirm the second merges cleanly and passes `Generated maps fresh` and `Build & test` without regenerating anything.

**Acceptance Scenarios**:

1. **Given** two PRs from the same base that each add a project, **when** the first merges, **then** the second shows no conflict caused by `docs/maps/dependency-map.json`.
2. **Given** a PR that changes a project reference and does not touch `docs/maps/`, **when** its checks run, **then** `Generated maps fresh` passes.
3. **Given** a clean checkout of `main` after the switch, **when** `git ls-files docs/maps/dependency-map.json` runs, **then** it prints nothing, and the path is git-ignored.

---

### User Story 2 - Publishing computes the same versions without the committed file (Priority: P1)

The package publish workflow runs on a `main` commit that has no committed dataset. The calculator derives the dataset from that commit and reads the record's older commits as before. It publishes the same versions, with the same fingerprints, as it would have from a committed, fresh dataset.

**Why this priority**: Publishing must not change behavior. A wrong dataset publishes wrong versions, or advances packages that did not change.

**Independent Test**: For each of the last 30 first-parent `main` commits, run the calculator against the current record twice, once reading the committed dataset and once deriving it. Compare the two outputs: every package's version, affected flag and fingerprint must match.

**Acceptance Scenarios**:

1. **Given** a commit that has a committed dataset, **when** the calculator computes, **then** it reads that file, as today.
2. **Given** a commit that has no committed dataset, **when** the calculator computes, **then** it derives the dataset from that commit's tree and needs no checkout, restore or network access.
3. **Given** a record whose entries name pre-consolidation commits, **when** a post-switch commit is built against it, **then** each entry's inputs at its old commit are resolved from that commit's committed dataset, and a package whose inputs did not change keeps its version.
4. **Given** a commit whose tree the scanner cannot read (a stale lock file, a computed package id, an unresolvable project reference), **when** the calculator computes, **then** it exits with invalid input and a message naming the commit, the file and the reason. It never computes from a partial dataset.

---

### User Story 3 - The generator and the calculator cannot disagree about the project graph (Priority: P2)

A maintainer changes how a project fact is read, for example how `IsPackable` is inherited. The change takes effect for the maps, the architecture tests and publishing at once, because they share one scan.

**Why this priority**: Two scans would drift. Drift would show up as wrong versions, which are expensive to correct after publishing.

**Independent Test**: Search both tool projects for the scanning logic and confirm that only the shared library contains it. Confirm that a test which changes a synthetic repository's project fact sees the change through both the generator output and the calculator's dataset.

**Acceptance Scenarios**:

1. **Given** the shared library, **when** the generator writes the dataset from the working tree and the calculator derives it from the git tree of the same clean commit, **then** the two datasets are equal.
2. **Given** the repository after the change, **when** anyone searches for a second implementation of project-file, central-pin or lock-file scanning in the generator or the calculator, **then** none exists outside the shared library.

---

### User Story 4 - Agents and people still find the dataset (Priority: P3)

An agent or a person who used to read `docs/maps/dependency-map.json` on `main` reads it from the `generated-maps` branch or generates it locally. The docs, `AGENTS.md` and the skill catalog say where it is.

**Why this priority**: The file is a navigation aid for some readers, but nothing in the build reads it any more.

**Independent Test**: After a push to `main`, fetch `docs/maps/dependency-map.json` from the `generated-maps` branch's raw URL. Confirm that it matches a local `-- dependency-map` run at the same commit.

**Acceptance Scenarios**:

1. **Given** a push to `main`, **when** the publish job finishes, **then** the `generated-maps` branch and the `generated-maps-<sha>` artifact contain `docs/maps/dependency-map.json` for that commit.
2. **Given** `AGENTS.md`, `docs/maps/README.md`, `docs/contributing/generated-maps.md` and the skill catalog, **when** read after the change, **then** none of them describes the dataset as committed or `-- check` as guarding it.

### Edge Cases

- **A commit from before the file existed** (before 2026-09-26): no record names one, and the forward-only gate refuses building one. If one is computed anyway, derivation is attempted. If the scanner cannot read it (pre-consolidation layout, or no lock files yet), the calculator refuses with a message naming the commit. It never treats the commit as having no projects.
- **A commit that has both a committed dataset and a tree that has moved past it**: before the switch, `Generated maps fresh` made every `main` commit's file fresh. The committed file wins, as Decision 2 says. The built commit is still held to its own projects and Line A list, as today.
- **The switch commit itself**: it deletes the file, so it is the first derived commit. A record entry naming the last pre-switch commit compares that commit's committed dataset with the switch commit's derived one. The validation in User Story 2 is what proves these agree.
- **A project where the pathspec rule and the layout rule disagree**: git's pathspec `Extensions/*/src/*.csproj` also matches `Extensions/A/studio/src/X.csproj`, because `*` crosses `/`. The calculator's layout rule, and the generator's own description of its layout, exclude it. The shared scanner adopts the layout rule: `Essentials/{src,apps,tests}/**` and `Extensions/<capability>/{src,tests}/**`. No project sits on such a path today, so no output changes.
- **Untracked files in a local checkout**: the generator still includes untracked, non-ignored files, as `git ls-files --others` does today. The calculator reads only the commit's tree. The equality in User Story 3 holds for a clean checkout.
- **Line endings**: the calculator reads git blobs, the generator reads working-tree files. The scan parses XML and JSON and records no file bytes, so CRLF checkouts produce the same dataset. Today's false "stale" report on Windows comes from byte-comparing the written JSON, and it disappears with the comparison.
- **A local `docs/maps/dependency-map.json` left over from before the switch**: it is git-ignored after the switch. Nothing reads it.
- **An open PR that still edits the file when the switch merges**: merging `main` into it resolves to the deletion. The file is ignored, so it cannot be re-added by accident without `git add -f`.

## Requirements

### Functional Requirements

- **FR-001**: For a commit that has no `docs/maps/dependency-map.json` in its tree, the calculator MUST derive the dataset from that commit's git objects alone: its project files, `Directory.Build.props` files, `VersionLines.props`, `Directory.Packages.props` and lock files. It MUST NOT read the working tree, run a restore, use the network, or check out the commit.
- **FR-002**: For a commit that has the file in its tree, the calculator MUST read the committed file exactly as it does today, with the same schema check and the same validation.
- **FR-003**: A derived dataset MUST hold the same facts per project as one the generator would write for the same tree: path, name, packable, package id, version line, and internal, external and pinned-transitive edges, in the same order.
- **FR-004**: When derivation fails, the calculator MUST exit with its invalid-input code (2), naming the commit and the scanner's reason. It MUST NOT fall back to an empty or partial dataset.
- **FR-005**: The calculator MUST hold the built commit's dataset, committed or derived, to that commit's projects (its own `ProjectFiles` discovery) and to its `VersionLines.props` Line A list, as it does today. For a derived dataset this guards the scanner's project discovery against the calculator's.
- **FR-006**: The scanning logic MUST live in one library that both the generator and the calculator reference: project discovery, project-file reading, packable and line resolution, central pin resolution, and lock-file pinned-transitive reading. The library MUST read files through one abstraction that has a working-tree implementation and a git-tree implementation. Neither tool MAY keep a second copy of that logic.
- **FR-007**: Extracting the library MUST NOT change the generator's output. `-- dependency-map` and every markdown layer MUST be byte-identical before and after the extraction at the same commit.
- **FR-008**: Before the file leaves `main`, the implementation MUST record a comparison of both dataset sources. For each of the last 30 first-parent `main` commits, the calculator's versions, affected flags and fingerprints computed against the current `publish-state` record MUST be identical whether the dataset is read from the commit or derived from it. In addition, for each of those commits and every commit their record revisions name, the derived dataset bytes MUST equal the committed bytes. Byte equality makes every mix of sources equal, including the post-switch case of a derived built commit against committed record commits. Any difference blocks the switch.
- **FR-009**: The calculator MUST offer a way to choose the dataset source for that comparison: committed, derived, or automatic. Automatic follows FR-001 and FR-002 and is the default.
- **FR-010**: `docs/maps/dependency-map.json` MUST be removed from the `main` tree and added to `.gitignore`. `-- dependency-map` and `-- all` MUST keep writing it at that path.
- **FR-011**: The `publish` command MUST include `docs/maps/dependency-map.json` in the published set, so the `generated-maps` branch and the `generated-maps-<sha>` artifact carry it. A link from a published map to it MUST stay relative.
- **FR-012**: `-- check` MUST no longer compare a committed dataset. It MUST check the committed solution filters, the only committed generated output left. The `Generated maps fresh` job MUST keep its name, drop its dataset-comparison step and its dataset-refresh hint, and keep its other steps.
- **FR-013**: `DependencyMapTests` and `NuGetLockFileTests` MUST run against a dataset produced by the shared library from the checkout under test, not against a committed file. They MUST keep what they assert today: longest-directory ownership, package ids and lines, and pinned-transitive edges matching NuGet's own list. Because `DependencyMapTests` runs the shared scanner on the PR's tree in `Build & test`, a tree the scanner cannot read fails the PR there. `VersionLineOwnershipGuardTests` and the calculator test support (`SyntheticRepository`) MUST stop relying on a committed dataset.
- **FR-014**: `tools/ci/change_scope.py` MUST drop its `docs/maps/dependency-map.json` exclusion and the self-test case for it. The calculator's inputs (project files, props files, lock files) MUST stay outside the documentation-only set.
- **FR-015**: `AGENTS.md`, `docs/maps/README.md`, `docs/contributing/generated-maps.md`, `docs/skills/catalog.md`, the affected skill wrappers and their manifest hashes, `tools/versioning/README.md`, `docs/reference/nuget-lock-files.md`, `VersionLines.props` comments and the `ci.yml` comments MUST describe the derived dataset and the published copy. None of them may describe the file as committed.
- **FR-016**: The unit MUST check `.specify/memory/constitution.md` §E5 and the framework constitution for any rule that names the committed dataset, and amend in the same unit any that does.

### Key Entities

- **Dependency-map dataset**: the per-project facts the calculator resolves ownership, lines and edges from (schema version 2). Its source is either committed or derived; its content is the same either way.
- **Dataset source**: committed (the file in the commit's tree) or derived (scanned from the commit's tree). Automatic selection prefers the committed file.
- **Repository tree view**: what the shared scanner reads. It lists files and reads their bytes, from either the working tree or a git commit.
- **Last-published record**: the `publish-state` record. It names a commit for each package. Its fingerprints must not move because of this change.

## Success Criteria

### Measurable Outcomes

- **SC-001**: After the switch, the number of commits on `main` that change a generated file under `docs/maps/` is 0. Before, 89 commits changed `docs/maps/dependency-map.json` from 2026-09-26 to 2026-10-20.
- **SC-002**: For the 30 most recent first-parent `main` commits before the switch, both dataset sources give identical versions, affected sets and fingerprints for all packages, 100% of the time.
- **SC-003**: The first package publish after the switch advances only packages whose inputs changed since their recorded commit. No package advances only because its dataset source changed.
- **SC-004**: The scanning logic exists once in the repository; a search finds no second implementation in the generator or the calculator.
- **SC-005**: Two PRs that each add a project from the same base merge one after the other with no conflict and no regeneration.
- **SC-006**: `Generated maps fresh` still reports under that name on every PR and push to `main`.

## Assumptions

- Every `main` commit since 2026-09-26 has a fresh committed dataset, because `Generated maps fresh` has been required since then. The committed file is therefore a correct description of its own commit.
- The calculator runs with full history, as today (`fetch-depth: 0`). Derivation reads blobs that every full clone has.
- Pre-consolidation commits never need derivation, because they all have a committed dataset (Decision 2).
- Deleting the file changes no package's inputs. No project directory contains `docs/maps/`, and the file is not an ambient build file.

## Out of Scope

- Merging the calculator's own MSBuild reader (`MsBuildFile`, `RepositoryPath`) with the scanner's project-file reader. They answer different questions (imports and content digests against project facts). Merging them is a separate unit if it is wanted.
- Re-deriving datasets for historical commits or rewriting `publish-state`.
- The solution filters, which stay committed and checked.
- Any change to how versions are computed from the dataset.
