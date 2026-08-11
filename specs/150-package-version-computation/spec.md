# Feature Specification: Package Version Computation and Selective Publishing

**Feature Branch**: `150-package-version-computation`
**Created**: 2026-08-07
**Status**: Draft
**Input**: Compute every package's version from the repository instead of injecting one, publish only the packages that changed, and let each nuspec dependency range state what the package actually needs.

Decision of record: [ADR 0067](../../docs/adr/0067-package-versioning-uses-two-lines-with-computed-patch.md).
Depends on [spec 149](../149-canonical-dependency-map/spec.md) for project-graph facts and ownership resolution.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install a feature into a host pinned at an older contract version (Priority: P1)

An operator runs a host pinned at contract surface 4.0.1. A bug is fixed in `Elsa.Tasks`. They install
the new `Elsa.Tasks` at runtime. The host is untouched, other domains are untouched, and other
features in the Tasks domain are untouched.

**Why this priority**: This is the whole point. Today a uniform version makes every feature declare it
needs the contracts at its own version, so a feature can only be installed into a host already pinned
at that version, which defeats runtime feature installation under Nuplane Strategy B.

**Independent Test**: Build a commit that changes only `src/Elsa/Tasks`, then inspect the produced
`.nupkg` files: exactly one package version advanced, and its declared floor on `Elsa.Tasks.Core` is
Core's current version rather than its own.

**Acceptance Scenarios**:

1. **Given** a commit touching only files owned by `Elsa.Tasks`, **When** the pipeline runs, **Then**
   `Elsa.Tasks` advances and no other package version changes.
2. **Given** that build, **When** the `Elsa.Tasks` nuspec is inspected, **Then** its dependency on
   `Elsa.Tasks.Core` states Core's current version, not the version of `Elsa.Tasks`.
3. **Given** a Line A contract change, **When** the pipeline runs, **Then** every Line A package
   moves to the same new version and no Line B version changes.
4. **Given** any produced package, **When** its dependency ranges are inspected, **Then** each range
   carries an upper bound at the next major.

---

### User Story 2 - Land a change without touching a version number (Priority: P1)

A contributor fixes a bug and opens a pull request. They edit no `<Version>` element, and two
concurrent pull requests touching the same package do not conflict on a version line or race each
other at publish time.

**Why this priority**: Hand-maintained per-package versions were the main objection to per-package
versioning, and they bring merge conflicts on hub packages plus a publish race where two pull requests
pass their gates independently and the second turns `main` red on merge.

**Independent Test**: Build the same commit twice on different machines and compare every produced
version; then merge two branches that both touch one package and confirm no version conflict.

**Acceptance Scenarios**:

1. **Given** a commit, **When** the version computation runs twice, **Then** it produces identical
   versions both times.
2. **Given** two branches that both touch `Elsa.Tasks`, **When** both merge, **Then** neither
   conflicts on a version and the resulting version reflects both commits.
3. **Given** any project in `src/`, **When** its `.csproj` is inspected, **Then** it declares no
   literal `<Version>`.
4. **Given** the packaging workflow, **When** it is inspected, **Then** it injects no global
   `/p:Version`.

---

### User Story 3 - Bump a third-party dependency and have dependents reflect it (Priority: P2)

A maintainer raises a third-party package version in `Directory.Packages.props`. The Elsa packages
that reference it advance, because their published nuspec content genuinely changed. Packages that do
not reference it stay where they are.

**Why this priority**: `Directory.Packages.props` sits at the repository root and is owned by no
project, so commit-height alone would miss it entirely and publish changed content at an unchanged
version.

**Independent Test**: Bump one third-party package used by a small number of projects, and confirm
exactly those projects advance.

**Acceptance Scenarios**:

1. **Given** a commit raising one `PackageVersion` entry, **When** the pipeline runs, **Then** every
   project with an external edge to that package advances and no other project does.
2. **Given** a commit changing `Directory.Build.props` or `NuGet.config`, **When** the pipeline runs,
   **Then** every project advances, because those inputs affect all of them and no edge exists to be
   precise with.
3. **Given** a commit editing only a comment in `Directory.Packages.props`, **When** the pipeline
   runs, **Then** no project advances.

---

### User Story 4 - Publish previews that behave like releases (Priority: P2)

A developer testing against the preview feed sees packages that carry the same version scheme and the
same dependency floors they will carry at release, so the runtime-install path can be validated before
4.0 ships.

**Why this priority**: A preview scheme that flattens everything onto one shared counter reproduces
the floor inflation this work removes, and we would not catch it until release.

**Independent Test**: Inspect two consecutive preview builds and confirm unchanged packages kept their
versions and their floors did not move.

**Acceptance Scenarios**:

1. **Given** a build from `main`, **When** packages are produced, **Then** each carries
   `<major>.<minor>.<height>-preview` with no run-number counter.
2. **Given** a build from a branch, **When** packages are produced, **Then** each carries a
   branch-scoped prerelease label that cannot collide with a future `main` version.
3. **Given** two consecutive `main` builds where one package changed, **When** the feed is inspected,
   **Then** only that package has a new version and the rest are unchanged.

---

### Edge Cases

- A package whose own files did not change but whose dependency floor moved: covered by FR-006a. It
  is not packed, and its published artifact stays correct because the newer dependency satisfies the
  floor it already declares.
- A publish that succeeds for some packages and fails for others: the baseline ref does not move, so
  the next run recomputes the same set and re-pushes. Pushes of identical versions are no-ops.
- A revert: height increases rather than decreasing, which is correct because the content changed
  again.
- A rewritten history on `main`: versions renumber. Accepted, and recorded as an operational
  constraint rather than defended against.
- A commit touching only files owned by no project: no package advances.
- A commit editing only documentation inside a project directory: no package advances, per FR-002a.
- A project that later starts shipping a file kind currently treated as non-affecting: that kind
  becomes package-affecting for that project, which is why FR-002a defines the exclusion by effect
  rather than by extension.
- A newly added project: its height starts from the base tag like any other, not from its first
  commit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Line A packages MUST take their version from a single `ElsaContractsVersion` property.
  Line B packages MUST take `major.minor` from a single `ElsaVersion` property. Membership comes from
  the dependency map.
- **FR-002**: The patch digit MUST be computed as the count of commits since the base tag that touch
  files owned by that project, with ownership resolved from the dependency map.
- **FR-002a**: A change to an owned file that does not affect the produced package MUST NOT advance
  the version. Documentation inside a project directory is the motivating case: a README edit ships
  nothing and must not oblige consumers to take a new package. The excluded set MUST be defined as
  files that do not contribute to the package artifact, rather than as a fixed list of extensions, so
  that if a file kind starts shipping it stops being excluded. A README packed through
  `PackageReadmeFile` is the worked example: no project sets that property today, so READMEs ship
  nothing; were one to set it, that project's README would become package-affecting.
- **FR-003**: A change to an entry in `Directory.Packages.props` MUST advance every project that has
  an external edge to the affected package, and only those projects.
- **FR-004**: A change to a repository-wide build input that has no external edge, specifically
  `Directory.Build.props` and `NuGet.config`, MUST advance every project.
- **FR-005**: No `.csproj` under `src/` may declare a literal `<Version>`, and the packaging workflow
  MUST NOT inject a global `/p:Version`.
- **FR-006**: The pipeline MUST pack and push only the affected set, and MUST derive that set locally
  from the repository. The baseline is a git ref recording the last commit published from that
  branch, moved only after every push in a run has succeeded. The set MUST NOT be derived from a
  query against the target feed, because that would make a correct build depend on network
  availability and feed consistency, nor from state held in CI, because that would make the same
  commit produce different results across re-runs.
- **FR-006a**: A package whose own files did not change MUST NOT be packed, even when a package it
  references has advanced. Its published artifact already declares a floor that the newer dependency
  satisfies, so repacking it would publish different content at an unchanged version and would raise
  its floor for no reason.
- **FR-007**: Every nuspec dependency range MUST carry an upper bound at the next major.
- **FR-008**: While the 4.0 line is unreleased, every produced package MUST carry a `-preview` label
  with no counter. Builds from a branch MUST carry a branch-scoped label instead.
- **FR-009**: Version computation MUST be deterministic for a given commit, independent of machine,
  clock, build number and working-directory state.
- **FR-010**: The base tag MUST supply only the commit range. Major and minor MUST come from the
  MSBuild properties, so the version is defined in one place.
- **FR-011**: `--skip-duplicate` MUST remain only as re-run idempotency for packages the pipeline has
  determined are unchanged. Pushing changed content at an already-published version MUST fail.

### Key Entities

- **Version line**: Line A or Line B, declared per project in the dependency map.
- **Base tag**: a non-version-shaped tag marking the counting origin. Replaced by the real release tag
  once 4.0.0 ships, after which the mechanism is unchanged.
- **Affected set**: the projects whose computed version differs from what is published, derived from
  owned-file changes plus the repository-wide input rules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A commit touching one project advances exactly one package version.
- **SC-002**: No produced nuspec declares a dependency floor higher than the referenced package's
  actual current version.
- **SC-003**: Building the same commit twice produces identical versions for every package.
- **SC-004**: A third-party version bump advances exactly the projects that reference it.
- **SC-005**: Two pull requests touching the same package merge without a version conflict, and
  neither turns `main` red at publish.
- **SC-006**: A preview build republishes no package whose owned files did not change.
- **SC-007**: A build advancing only a contract package leaves every dependent package's published
  artifact untouched, and those artifacts still resolve against the new contract version.
- **SC-008**: The affected set is computed with no network access, and is identical when the same
  commit and baseline ref are built on a machine with no feed connectivity.
- **SC-009**: A commit that changes only documentation advances no package version and publishes
  nothing.

## Assumptions

- Spec 149 has landed, so the dependency map supplies project nodes, ownership resolution and typed
  external edges.
- A base tag exists before the mechanism is enabled. It is not version-shaped, so it cannot be
  mistaken for a published release.
- At the 4.0.0 release the release commit is tagged and heights reset, so every package ships as a
  clean `4.0.0` and the `-preview` label is dropped. This is the one point where a release is a
  rebuild rather than a promotion of an already-built artifact, and that cost is accepted.
- Central package management is in use, so external versions are declared in one file.

## Out of Scope

- The magnitude gate that checks a bump against the real public API delta. It needs released baselines
  to compare against and is its own work unit.
- The generated release manifest recording which package versions constitute a release.
- Promote-not-rebuild at release time, which is untestable until there is a release to promote.
- Ratifying Line A membership beyond the eight packages in ADR 0067; the remainder is deferred to the
  clean host specification (#1145).

## Open Questions

- FR-006 puts the publish baseline in a git ref. Is a ref the right home, against the alternative of
  a committed file? A ref adds no commit noise, but it is less visible in review and can drift from
  the feed if a publish fails in a way the run does not detect.
- What is the base tag's name? It must not be version-shaped.
