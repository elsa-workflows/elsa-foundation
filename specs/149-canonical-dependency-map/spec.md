# Feature Specification: Canonical Dependency Map

**Feature Branch**: `149-canonical-dependency-map`
**Created**: 2026-08-07
**Status**: Draft
**Input**: Make one generated machine-readable dataset the source of truth for the repository's project graph, with the existing markdown maps becoming projections of it, so that publishing can resolve which project owns a file without depending on a documentation tool.

Decision of record: [ADR 0067](../../docs/adr/0067-package-versioning-uses-two-lines-with-computed-patch.md), which records that the dependency map "resolves file ownership for the patch computation and generates the `SharedAssemblies` list and the host compatibility manifest".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resolve which project owns a changed file (Priority: P1)

The publishing pipeline needs to know, for any repository-relative path in a commit, which project
owns it, so a later work unit can compute a per-package version from commit history. Project
directories nest, so `src/Elsa/Workflows/Runtime/Core/Foo.cs` belongs to
`Elsa.Workflows.Runtime.Core` and not to `Elsa.Workflows.Runtime`.

**Why this priority**: This is the capability that unblocks spec 150. Without it, either the
publishing pipeline takes a dependency on the documentation generator, or the ownership rule is
implemented twice and the two can disagree about the same file.

**Independent Test**: Given the dataset alone, with no repository scan, resolve the owning project of
a set of paths including nested-project cases, and compare against the project each file actually
compiles into.

**Acceptance Scenarios**:

1. **Given** the dataset, **When** a consumer resolves `src/Elsa/Workflows/Runtime/Core/Foo.cs`,
   **Then** the owning project is `Elsa.Workflows.Runtime.Core`, the longest matching project path.
2. **Given** the dataset, **When** a consumer resolves a path under `src/` that no project directory
   contains, **Then** the result is "no owner" rather than a nearest-ancestor guess.
3. **Given** the dataset, **When** a consumer resolves a path under `tests/`, **Then** the owning
   test project is returned, because impact analysis needs test nodes.
4. **Given** the dataset, **When** ownership is resolved, **Then** no filesystem access beyond the
   dataset itself is required.

---

### User Story 2 - Read a map that cannot drift from the tree (Priority: P1)

A maintainer opens `docs/maps/project-reference-map.md` and trusts it, because it is generated from
the same dataset as every other map rather than from its own independent scan.

**Why this priority**: The maps are already the repository's shared mental model, and today each is
produced by its own generator pass. One dataset with projections removes the class of bug where two
maps disagree.

**Independent Test**: Regenerate, confirm every markdown map is reproducible from the dataset alone,
and confirm no generator reads `.csproj` files a second time to build a projection.

**Acceptance Scenarios**:

1. **Given** the dataset, **When** the markdown projections are generated, **Then** every fact in
   them is present in the dataset.
2. **Given** an unchanged tree, **When** generation runs twice, **Then** the dataset and every
   projection are byte-identical, with no embedded timestamp or run identifier that churns.
3. **Given** a project added, removed or renamed, **When** generation runs, **Then** the dataset and
   all projections reflect it in one pass.

---

### User Story 3 - Fail the build when the map no longer describes the tree (Priority: P2)

CI rejects a change whose committed dataset no longer matches the repository, the same way it already
guards the markdown maps.

**Why this priority**: The dataset becomes an input to publishing, so a stale dataset would mean
wrong versions rather than only stale documentation. The existing freshness mechanism extends to
cover it.

**Independent Test**: Modify a project reference without regenerating, and confirm the freshness
check fails naming the dataset.

**Acceptance Scenarios**:

1. **Given** a committed dataset matching the tree, **When** the freshness check runs, **Then** it
   passes.
2. **Given** a project reference added without regeneration, **When** the check runs, **Then** it
   fails and names the dataset.
3. **Given** a documentation-only edit that touches no project input, **When** the check runs,
   **Then** it passes.

---

### Edge Cases

- A project whose directory contains another project's directory: the deeper project owns its own
  subtree, and the outer project owns everything else beneath it.
- A file outside any project directory, such as a repository-root configuration file: no owner. Such
  paths are the caller's problem, not the dataset's.
- Two projects that share a name in different directories: nodes are keyed by path, not by name.
- Projects excluded from packing: recorded with their packable state rather than omitted, because
  impact analysis still needs them.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A single scan of the repository MUST produce one machine-readable dataset at
  `docs/maps/dependency-map.json`.
- **FR-002**: Every markdown map that today derives from the project graph MUST be generated as a
  projection of that dataset, and MUST NOT independently scan `.csproj` files.
- **FR-003**: Each node MUST record the project name, repository-relative project path, kind
  (source or test), packable state, domain and sub-domain, and role.
- **FR-004**: Each node MUST record which version line it belongs to, per ADR 0067. Line A membership
  is the eight packages named there; every other packable project is Line B.
- **FR-005**: Each edge MUST be typed `internal` or `external`, and MUST record the target package
  identity. Internal edges MUST record the target project path; external edges MUST record the
  declared version.
- **FR-006**: Ownership of a repository-relative path MUST be resolvable from the dataset alone, by
  longest matching project path, with no filesystem access.
- **FR-007**: Generation MUST be deterministic: the same tree MUST produce a byte-identical dataset,
  with stable ordering and no timestamps or run identifiers.
- **FR-008**: The existing maps freshness check MUST cover the dataset and fail when it no longer
  describes the tree.
- **FR-009**: The dataset MUST NOT enumerate individual source files. Ownership is derived from
  project paths, so that the dataset changes only when the project graph changes.
- **FR-010**: `feature-dependency-map.md` MUST remain separate, because CShells `DependsOn`
  attributes carry literal feature-id strings that no reference graph can capture.

### Key Entities

- **Project node**: one per project in `src/` and `tests/`; identity is its repository-relative path.
- **Dependency edge**: a directed relation from a node to a package identity, typed by whether the
  target resolves inside this repository.
- **Dataset**: the set of nodes and edges plus the freshness fingerprint, versioned by a schema
  version so consumers can detect an incompatible shape.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Ownership resolves correctly for every tracked file under `src/` and `tests/`,
  verified against the project each file compiles into, including all nested-project cases.
- **SC-002**: Regenerating twice on an unchanged tree produces byte-identical output.
- **SC-003**: Every markdown map currently generated from the project graph is reproducible from the
  dataset, with no content lost relative to today's output.
- **SC-004**: The freshness check fails on a project-graph change made without regeneration, and
  passes on a documentation-only change.
- **SC-005**: No consumer of the dataset needs to reference or execute the documentation generator.

## Assumptions

- `ProjectFacts` already models most node data (name, path, kind, domain, sub-domain, role,
  references), so this is largely a refactor of existing logic plus a new emission.
- `ProjectGraph.OwningProject` already implements longest-prefix resolution and is the behaviour
  FR-006 codifies.
- External package versions are available from `Directory.Packages.props` through central package
  management.

## Out of Scope

- Version computation and selective publishing. That is spec 150, which consumes this dataset.
- Generating the `SharedAssemblies` list and the host compatibility manifest. Both are named in
  ADR 0067 as future consumers and neither is built here.
- Ratifying Line A membership beyond the eight packages in ADR 0067. The remaining candidates are
  deferred to the clean host specification (#1145).
- Merging `feature-dependency-map.md` into the dataset, per FR-010.

## Open Questions

- Should the dataset carry external package versions, so `package-map.md` becomes a projection too,
  or should external dependencies stay out and that map keep its own pass? Carrying them makes the
  dataset the single answer to "what do we depend on", at the cost of it changing whenever a
  third-party version bumps.
- Should the schema version be enforced by consumers at read time, or is a mismatch a build-time
  concern only?
