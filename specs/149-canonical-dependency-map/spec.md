# Feature Specification: Canonical Dependency Map

**Feature Branch**: `149-canonical-dependency-map`
**Created**: 2026-08-07
**Status**: Draft
**Input**: Make one generated machine-readable dataset the source of truth for the repository's project graph, with the existing markdown maps becoming projections of it, so that publishing can resolve which project owns a file without depending on a documentation tool.

Decision of record: [ADR 0067](../../docs/adr/0067-package-versioning-uses-two-lines-with-computed-patch.md), which records that the dependency map "resolves file ownership for the patch computation and generates the `SharedAssemblies` list and the host compatibility manifest". Its Decision, as amended 2026-09-22, records each package's last published version per package id in the dependency map.
Spec 150 writes and consumes the last-published record this spec carries.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resolve which project owns a changed file (Priority: P1)

The publishing pipeline needs to know, for any repository-relative path in a commit, which project
owns it, so spec 150 can decide whether a package changed since it was last published, by comparing
ownership at two commits. Project directories nest, so `src/Elsa/Workflows/Runtime/Core/Foo.cs`
belongs to `Elsa.Workflows.Runtime.Core` and not to `Elsa.Workflows.Runtime`.

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

The dataset holds two kinds of content. Tree-derived facts are generated from the repository and
cannot drift from it. The last-published record is not derived from the tree at all; it records what
the feed received, and generation carries it forward unchanged. The freshness check vouches for the
first and says nothing about the second; spec 150 guards the record.

**Why this priority**: The maps are already the repository's shared mental model, and today each is
produced by its own generator pass. One dataset with projections removes the class of bug where two
maps disagree.

**Independent Test**: Regenerate, confirm every markdown map is reproducible from the dataset alone,
and confirm no generator reads `.csproj` files a second time to build a projection.

**Acceptance Scenarios**:

1. **Given** the dataset, **When** the markdown projections are generated, **Then** every fact in
   them is present in the dataset.
2. **Given** an unchanged tree and unchanged committed records, **When** generation runs twice,
   **Then** the dataset and every projection are byte-identical, with no embedded timestamp or run
   identifier that churns.
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
4. **Given** a committed dataset whose records, attached or retired, differ from what any
   regeneration would produce, such as a publish write-back that changed only a record, **When** the
   freshness check runs, **Then** it passes, because records are not compared.
5. **Given** a committed dataset carrying records, attached or retired, **When** the check runs,
   **Then** no record is rewritten, dropped or reset.

---

### User Story 4 - Keep publish state through regeneration (Priority: P1)

Generation runs again, because a project was added, removed or moved, and every last-published record
already in the committed dataset survives untouched, keyed to the package id it was written for.

**Why this priority**: Spec 150 computes every patch from the record. A regeneration that wiped or
reset it would make the next publish compute versions the feed already holds, and spec 150's FR-011
would fail the push.

**Independent Test**: Regenerate a dataset that carries records, and confirm every record is
byte-identical to the one committed before regeneration.

**Acceptance Scenarios**:

1. **Given** a committed dataset carrying records, **When** generation runs, **Then** every record,
   attached or retired, is byte-identical to the one it replaces.
2. **Given** a project whose directory moves but whose package id is unchanged, **When** generation
   runs, **Then** its node's path changes and it keeps its record, because records are carried by
   package id, not by path.
3. **Given** a project whose package id changes, **When** generation runs, **Then** the new id has no
   record, because spec 150 treats it as a new package, and the old id's record becomes a retired
   record.
4. **Given** a newly added packable project, **When** generation runs, **Then** its node has no
   record, and that is valid.
5. **Given** any generation run, **When** it executes, **Then** it makes no request to the feed.
6. **Given** a project deleted while its package id has a record, **When** generation runs, **Then**
   the record moves to the retired records rather than being dropped.
7. **Given** a project deleted while its package id had a record, **When** the project is later
   re-added with the same package id, **Then** its node carries the old record again, reattached from
   the retired records.

---

### User Story 5 - Reject a pull request that edits a record (Priority: P2)

A contributor's branch regenerated the map before a publish wrote new records to `main`. The merge
conflicts on `dependency-map.json`, and the conflict is resolved by taking the branch's copy, which
would roll records back. CI rejects it before merge.

**Why this priority**: A record rolled back makes the next publish compute a version the feed already
holds. Spec 150's FR-011 only catches that after merge, as a red publish on `main`, whose only repair
is a hand edit to a record, which the rules forbid.

**Independent Test**: Open a pull request whose dataset differs from its merge base only in a record,
and confirm the guard fails naming the package id and both values; open one that only moves, adds or
deletes projects, and confirm it passes.

**Acceptance Scenarios**:

1. **Given** a pull request that changes a record's version or commit, **When** the guard runs,
   **Then** it fails, naming the package id.
2. **Given** a pull request that resolves a merge conflict by taking a stale record, **When** the
   guard runs, **Then** it fails.
3. **Given** a pull request that moves, adds or deletes projects, **When** the guard runs, **Then**
   it passes.
4. **Given** a pull request that touches no record, **When** the guard runs, **Then** it passes.
5. **Given** the one-off bootstrap's records reaching `main` through a pull request, **When** the
   guard runs on that pull request, **Then** it is exempted explicitly, and no other pull request is.

---

### Edge Cases

- A project whose directory contains another project's directory: the deeper project owns its own
  subtree, and the outer project owns everything else beneath it.
- A file outside any project directory, such as a repository-root configuration file: no owner. Such
  paths are the caller's problem, not the dataset's.
- Two projects that share a name in different directories: nodes are keyed by path, not by name.
- Projects excluded from packing: recorded with their packable state rather than omitted, because
  impact analysis still needs them.
- A project's directory moves but its package id does not change: the node's path changes and it
  keeps its last-published record, because the record is keyed by package id, not by path.
- A non-packable node: it carries no last-published record.
- A package id changes: spec 150 treats it as a new package. The new id starts with no record, and
  the old id's record becomes a retired record, carried forward apart from the nodes; it reattaches
  if a node with that id appears again.
- A project is deleted while its package id has a record: the node goes and the record becomes a
  retired record, carried forward apart from the nodes; it reattaches if a project with that package
  id is re-added.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A single scan of the repository MUST produce one machine-readable dataset at
  `docs/maps/dependency-map.json`.
- **FR-002**: Every markdown map that today derives from the project graph MUST be generated as a
  projection of that dataset, and MUST NOT independently scan `.csproj` files.
- **FR-003**: Each node MUST record the project name, repository-relative project path, kind
  (source or test), packable state, domain and sub-domain, and role. Each packable node MUST
  additionally record its package id.
- **FR-004**: Each node MUST record which version line it belongs to, per ADR 0067. Line A membership
  is the set of packages named there (ADR 0067, Decision); every other packable project is Line B.
- **FR-005**: Each edge MUST be typed `internal` or `external`, and MUST record the target package
  identity. Internal edges MUST record the target project path; external edges MUST record the
  declared version.
- **FR-006**: Ownership of a repository-relative path MUST be resolvable from the dataset alone, by
  longest matching project path, with no filesystem access.
- **FR-007**: Generation MUST be deterministic: the same tree and the same committed records MUST
  produce a byte-identical dataset, with stable ordering and no timestamps or run identifiers.
- **FR-008**: The existing maps freshness check MUST compare only the tree-derived parts of the
  dataset and fail when they no longer describe the tree. It MUST preserve the last-published
  records, attached or retired, and MUST NOT regenerate, reset or wipe them, and a difference
  confined to records MUST NOT fail it. Correctness of the record is guarded by spec 150 (FR-011,
  FR-012 monotonicity gate), not by this check.
- **FR-009**: The dataset MUST NOT enumerate individual source files. Ownership is derived from
  project paths, so that the dataset changes only when the project graph changes or a publish writes
  a last-published record.
- **FR-010**: `feature-dependency-map.md` MUST remain separate, because CShells `DependsOn`
  attributes carry literal feature-id strings that no reference graph can capture.
- **FR-011**: A packable node whose package id has been published MUST carry its last-published
  record: the version last pushed to the feed and the commit that version was built from. A node
  whose package id has never been published carries none, and neither does a non-packable node.
- **FR-012**: The last-published record is publish-written state, written by spec 150's publish from
  `main` and its bootstrap. Generation MUST carry each record forward from the committed dataset
  unchanged, matched by package id, and MUST NOT create, modify or infer a record, and MUST NOT drop
  one. Generation MUST move a record to the retired records when its package id loses its node, and
  MUST reattach it when the id reappears; moving or reattaching a record is not modifying it, because
  its content is unchanged. It MUST NOT read the feed.
- **FR-013**: The markdown projections MUST NOT include the last-published record, so a publish
  write-back changes only the dataset and never obliges regenerating a projection.
- **FR-014**: When a package id that has a record no longer has a node, because the project was
  deleted or its package id was renamed, the record MUST be kept in the dataset as a retired record,
  apart from the nodes, keyed by package id, and carried forward by generation like any other record.
  If a node with that package id appears again, the record reattaches to it, so the next publish is
  one past the last published version rather than a version the feed already holds. Retired records
  are never pruned. The dataset then lists every package id ever published.
- **FR-015**: A pull-request check, separate from the freshness check, MUST fail when any record in
  the pull request's dataset, attached or retired, differs from the record for the same package id at
  the pull request's merge base. That covers a record added, changed or removed. The failure names
  the package id and both values. Records are compared by package id, not by position, so a pull
  request that moves, adds or deletes projects passes: moving keeps the record, adding creates none,
  and deleting retires it unchanged. The freshness check stays tree-only (FR-008). Publishes write
  records back to `main` without a pull request, so they are not subject to it; how the write-back
  reaches `main` is spec 150's open question. If the one-off bootstrap's records ever reach `main`
  through a pull request, that single pull request MUST be exempted explicitly, and no other.
- **FR-016**: The dataset's serialization MUST place each record, attached or retired, on its own
  lines apart from the tree-derived fields. A pull request that changes the project graph and a
  publish write-back that changes records then touch different lines, and git merges them without a
  conflict. The FR-015 guard catches any conflict that is still resolved by taking a stale copy.

### Key Entities

- **Project node**: one per project in `src/` and `tests/`; identity is its repository-relative
  path. A packable node also carries its last-published record.
- **Dependency edge**: a directed relation from a node to a package identity, typed by whether the
  target resolves inside this repository.
- **Last-published record**: per package id, the version last pushed to the feed and the commit
  that version was built from. Committed state, written only by publishing from `main` and the
  one-off bootstrap publish, never by a contributor, a pull request or the generator.
- **Retired record**: a last-published record whose package id has no node, kept apart from the
  nodes, keyed by package id. Reattaches to a node with the same package id if one appears again.
  Never pruned.
- **Dataset**: the tree-derived nodes and edges, the last-published records, the retired records, and
  the freshness fingerprint, which covers only the tree-derived parts, versioned by a schema version
  so consumers can detect an incompatible shape.

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
- **SC-006**: Regenerating and running the freshness check on a dataset that carries records leaves
  every record byte-identical, and a difference confined to records passes the check.
- **SC-007**: A project moved to a new directory keeps its last-published record.
- **SC-008**: Deleting a project and later re-adding one with the same package id restores its
  last-published record.
- **SC-009**: A pull request that alters any record fails the guard, and one that only adds, moves or
  deletes projects passes.

## Assumptions

- `ProjectFacts` already models most node data (name, path, kind, domain, sub-domain, role,
  references), so this is largely a refactor of existing logic plus a new emission.
- `ProjectGraph.OwningProject` already implements longest-prefix resolution and is the behaviour
  FR-006 codifies.
- External package versions are available from `Directory.Packages.props` through central package
  management.
- Spec 150's bootstrap publish seeds the last-published records. Until it runs, packable nodes carry
  none, and the dataset is still valid and fresh.

## Out of Scope

- Version computation and selective publishing. That is spec 150, which consumes this dataset.
- Generating the `SharedAssemblies` list and the host compatibility manifest. Both are named in
  ADR 0067 as future consumers and neither is built here.
- Deciding Line A membership. ADR 0067's Decision names the members; this spec only records the line
  each project is on. Which assemblies each host shares is the clean host specification's concern
  (#1145).
- Merging `feature-dependency-map.md` into the dataset, per FR-010.
- Writing the last-published record. The publish, the bootstrap, and how the updated record reaches
  `main` are spec 150.

## Open Questions

- Should the dataset carry external package versions, so `package-map.md` becomes a projection too,
  or should external dependencies stay out and that map keep its own pass? Carrying them makes the
  dataset the single answer to "what do we depend on", at the cost of it changing whenever a
  third-party version bumps.
- Should the schema version be enforced by consumers at read time, or is a mismatch a build-time
  concern only?
