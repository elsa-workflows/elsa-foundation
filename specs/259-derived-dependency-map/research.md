# Research: The calculator derives the dependency map, so it is never committed

All findings were verified against `main` at `d628fc0da` on 2026-10-20.

## R1 — Which commits the calculator reads, and what they hold

- **Finding**: the calculator builds a `CommitSnapshot` for the built commit and, lazily, for each commit a record entry names (`VersionCalculator.cs:57-70`). It reads the dataset from each one (`CommitSnapshot.cs:50-53`). The `publish-state` branch has 150 record revisions, which name 149 distinct commits in all. Every one of them has a schema-2 `docs/maps/dependency-map.json`. 104 use the pre-consolidation layout (`src/`, `tests/`) and 45 use `Essentials/` and `Extensions/`. The oldest is from 2026-09-28. The file was added on 2026-09-26 (`305a7a647`, #2088).
- **Method**: walked `git log origin/publish-state`, collected every `packages[].commit` from each revision of `published-versions.json`, and ran `git show <c>:docs/maps/dependency-map.json` and `git cat-file -e <c>:Essentials` on each.
- **Consequence**: no record commit lacks the file. A commit with no file is either the built commit after the switch or a pre-2026-09-26 commit, which the forward-only gate keeps from being built.

## R2 — Committed wins where present (spec Decision 2)

- **Decision**: `CommitSnapshot` reads the committed file when the commit's tree has one, and derives the dataset otherwise. This is the automatic source. An explicit `committed` or `derived` source exists only for the validation in R8.
- **Rationale**:
  - The pre-consolidation commits cannot be derived by today's scanner. It scans only `Essentials/` and `Extensions/` (`RepoLayout.ProjectPathspecs`). Teaching it the old layout would add a second, dead project-discovery rule only to re-read history.
  - Reading the committed bytes reproduces the inputs those publishes were computed from, so no recorded fingerprint can move (spec 150 FR-018).
  - Every committed file on `main` since #1274 was held fresh by the required `Generated maps fresh` check, so it describes its commit.
- **Alternatives**:
  - *Always derive.* Rejected. It needs the legacy layout and risks fingerprint movement on 104 record commits, for no benefit, because those commits keep their file forever.
  - *Derive, and fall back to the committed file when derivation fails.* Rejected. A silent fallback hides scanner regressions on new commits.

## R3 — One scanner, two tree views (FR-006)

- **Decision**: a new library project `tools/maps/Elsa.Maps.ProjectGraph` holds the scan. The generator and the calculator both reference it. It moves out of the generator unchanged in behavior:
  - `ProjectFacts`, the edge records and `ProjectGraph` (`ProjectFacts.cs`);
  - `ProjectFile`;
  - `PackageVersions`;
  - `PinnedTransitiveDependencies`;
  - the project-path part of `RepoLayout`;
  - the dataset shape and serializer from `DependencyMap`.

  The generator keeps writing the file, through `MarkdownTable`.
- **Tree view**: the scanner reads through one abstraction, `IRepositoryTree`, with three members:
  - `ProjectFiles()`: the backend `.csproj` paths, ordinal;
  - `Exists(path)`;
  - `Read(path)`: the file's bytes.

  `RepoContext` implements it over the working tree, as now (`git ls-files --cached --others --exclude-standard`, then the disk). The calculator implements it over a commit's tree entries and its shared `ObjectCache`.
- **Pathspec semantics**: git pathspecs without `:(glob)` let `*` match `/`, so `Extensions/*/src/*.csproj` matches nested paths. The git-tree view has no `ls-files`. So the library gains `RepoLayout.IsProjectPath(path)`, a predicate equal to `ProjectPathspecs` under git's default wildcard rules, and the git-tree view filters tree entries with it. A self-test pins that the predicate and `git ls-files -- <pathspecs>` select the same set on the live tree, plus synthetic nested and Studio paths.
- **Rationale**:
  - The scan is about 600 lines across four files. The "duplication beats dependency" rule covers 5–10 lines, so a second copy is out.
  - A library avoids an exe-to-exe reference. `Elsa.Architecture.Tests` already references the generator (`Elsa.Architecture.Tests.csproj:102`), and both tool projects are in `Elsa.Server.slnx`. So no new kind of dependency appears.
  - Placing it under `tools/maps/` keeps one owner for the project-graph facts.
- **Alternatives**:
  - *The calculator references `Elsa.Maps.Generator` directly.* Rejected: an exe reference drags in every map generator.
  - *Linked source files (`<Compile Include="..\..\maps\…" Link=…/>`).* Rejected: still one copy on disk, but no compile boundary or owner, and the internals of both tools would mix.
  - *Rewrite the scan inside the calculator over `MsBuildFile`.* Rejected: that would be two implementations.
- **Not merged**: the calculator's `MsBuildFile`/`RepositoryPath` (imports, content digests, `PacksAsTool`) and the scanner's `ProjectFile` read different facts. Merging them is out of scope (spec Out of Scope).

## R4 — Derived and generated datasets must be the same bytes (FR-003, FR-007)

- **Decision**: the library exposes `DependencyMapJson.Serialize(IRepositoryTree)`, which returns the exact JSON the generator writes today: `schema_version` 2, `generator`, `nodes`, snake_case, indented, relaxed escaping, trailing newline. The generator writes that string. The calculator passes the same string to its existing `DependencyMapDataset.Parse`, so a derived dataset goes through the same schema and validation as a committed one.
- **Rationale**: one serializer means "same facts, same order" is checked by comparing bytes, with no field-by-field equality code. The calculator's validator stays its only reader.
- **CRLF**: the scan parses XML and JSON (`XDocument`, `JsonDocument`), and nothing in the dataset carries file bytes. A Windows checkout with `core.autocrlf=true` and the git blobs therefore give the same JSON. The known false "stale" on Windows came from byte-comparing an LF blob with a CRLF working-tree file (memory note; spec 257 quickstart), not from the content.

## R5 — Blob reads stay cheap

- **Finding**: on this tree, derivation reads 300 `.csproj` files (`git ls-files` over `ProjectPathspecs`), about 300 lock files, `Directory.Packages.props`, `VersionLines.props` and a few `Directory.Build.props`. The calculator already reads every blob it compares through `ObjectCache`, keyed by object id. Each cache miss spawns one `git cat-file blob` (`GitRepository.cs:86`). Derived snapshots reuse that cache, so an unchanged lock file between the built commit and a record commit is read once. Lock files are new reads, though: about 300 more processes per distinct commit, on top of the thousands of owned-file reads the calculator already makes.
- **Decision**: derive lazily, only when a snapshot's `Map` is first needed, and reuse the cache. No persistent cache. The validation run (R8) records the calculator's wall time both ways. If derivation adds more than 50% to the time of a CI compute, the implementation switches `ReadBlob` to one long-lived `git cat-file --batch`. That is a contained change inside `GitRepository`.

## R6 — What replaces `-- check` and the `Generated maps fresh` dataset step (FR-012)

- **Decision**:
  - `MapFreshness.Check` and the `-- check` command now check the committed solution filters, by calling what `solution-filters-check` runs. That is the only committed generated output left, and `-- check` keeps meaning "check what is committed".
  - In `ci.yml`, the job `Generated maps fresh` drops the step "Check the committed dependency map against the tree" and the "Explain how to refresh the dependency map" summary. It keeps the generator build, `solution-filters-check`, every `*-self-test` and the `.NET CLI` filter validation, and it gains `project-graph-self-test` (R3, R4).
  - The job name is unchanged, so ruleset `main-2` is not touched.
- **Derivability still gates PRs**: a tree the scanner refuses, such as a stale lock file, a conditional `PackageId` or an unresolvable `ProjectReference`, would fail publishing on `main`. Today `-- check` catches it on the PR. After the switch two guards still run on every non-docs-only PR:
  - `DependencyMapTests`, in `Build & test`, derives the dataset from the checkout (FR-013);
  - `publish` in the `Generated maps fresh` job generates it.

  `change_scope.py` keeps `*.csproj` and `*.props` out of the docs-only set. Lock files are outside its include set. So no PR that can break derivation skips both.
- **Alternatives**: keep a derivability-only step under `-- check`. Rejected as redundant with the two guards above.

## R7 — Removing, ignoring and publishing the file (FR-010, FR-011)

- **Decision**:
  - In `.gitignore`, drop the comment line "dependency-map.json and README.md stay committed" and add `docs/maps/dependency-map.json`. A broader `docs/maps/*.json` is not used, so the ignore stays explicit.
  - `git rm --cached docs/maps/dependency-map.json`.
  - `MapSetPublisher.Publish` adds `DependencyMap.Generate` at the head of its layer list. The JSON becomes a member of the published set. The existing link rewrite then keeps `[…](dependency-map.json)` relative, and `MapSetPublisherContractTests` changes its expectation to match.
  - `publish-generated-maps.sh` already publishes whatever the directory holds. The branch README that `MapSetPublisher.Readme` writes lists the file.
- **Rationale**: every reader except the calculator (agents, people, `elsa-feature-dependency-map`) can fetch it from the `generated-maps` branch, which spec 257 set up for exactly this.

## R8 — Proving both ways agree before the switch (FR-008, SC-002)

- **Decision**: the calculator gains `--dataset auto|committed|derived` (default `auto`; contract in [contracts/cli.md](contracts/cli.md)). A script `specs/259-derived-dependency-map/validate-dataset-sources.sh` runs on the implementation branch before the removal commit. For each of the last 30 first-parent `main` commits it:
  1. picks the record revision to compute against: the newest `publish-state` revision whose `last_publish_commit` is the commit or an ancestor of it. Computing an old commit against today's record would be refused by the forward-only gate;
  2. runs the calculator twice, with `--commit <c> --record-ref <rev> --dataset committed` and `--dataset derived`;
  3. compares the two outputs byte for byte. The output already lists every package's version, affected flag, record and fingerprint (`VersionComputation.ToJson`).

  It also compares the committed and derived dataset bytes of each commit, and of the 45 consolidated-layout record commits, for diagnosis. The results table goes into the PR's gate evidence. Any difference stops the switch.
- **Rationale**: version and fingerprint equality is what publishing depends on (SC-002, SC-003). Dataset byte equality explains a difference when there is one, but it is stricter than needed. A `generator` field or a node-order change alone would show up there and nowhere else.
- **Why `committed` mode in derived snapshots too**: with `--dataset derived`, every snapshot is derived, the record commits included. That is the strongest form of the check for consolidated record commits. For a legacy record commit, derivation fails by design (R2), so the script picks record revisions whose referenced commits are all consolidated. When none is reachable, it falls back to `auto` for the record snapshots and says so in the table.
- **After the switch**: `--dataset committed` stays for audits of pre-switch history. On a commit that has no file, it fails, naming the path.

## R9 — Consumers to update (FR-013, FR-014, FR-015)

- **Tests**:
  - `DependencyMapTests.ReadNodes` (`:212-220`) parses `DependencyMapJson.Serialize(RepoContext)` from the library instead of the file. Its oracle, a filesystem walk, still never reads the scanner. The class comment changes from "never the generator" to "never the scanner's internals; the oracle walks the filesystem".
  - `NuGetLockFileTests` (`:57`) does the same.
  - `VersionLineOwnershipGuardTests:13` changes its comment only.
  - The calculator test support `SyntheticRepository` (`:48,164`) stops writing the file by default. It writes a lock file beside each synthetic project, which the scanner requires, and keeps an option to write a committed dataset for the `committed`/`auto` cases.
- **CI**: in `tools/ci/change_scope.py`, drop `:39` and the self-test case at `:105`. The case `Essentials/**/packages.lock.json → not docs-only` is added if it is missing. In `ci.yml`, update the `Generated maps fresh` comment block (`:478-500`) and the summary text (`:596-600`).
- **Docs**:
  - `AGENTS.md` ("Refresh generated maps" paragraph and the source-of-truth row);
  - `docs/maps/README.md`, `docs/contributing/generated-maps.md`, `docs/contributing/merging.md`, `docs/contributing/validation-matrix.md`;
  - `docs/reference/nuget-lock-files.md`, `docs/reference/developer-solution-filters.md`;
  - `docs/skills/catalog.md`, `Extensions/README.md`, `VersionLines.props:13`;
  - `tools/versioning/README.md` (`:31,36,97,100`, the "What it reads" section and the exit-code 2 list);
  - `tools/maps/generate-maps.{sh,ps1}`;
  - `tools/maps/Elsa.Maps.Generator/{Program,DependencyMap,MapFreshness,FeatureDependencyMapGenerator}.cs` comments;
  - `VersionCalculator.cs:138-155`, whose messages stop saying "regenerate" for a derived dataset.
- **Skills**: `elsa-auto-review-merge`, `elsa-feature-dependency-map`, `elsa-quality-fix` and `elsa-refresh-generated-maps` (`.claude/skills/*/SKILL.md`), with their SHA256 in `.specify/integrations/claude.manifest.json`. `tools/skills/validate-claude-wrapper-drift.ps1` must pass.
- **Leave as history**: `specs/**`, `docs/reports/**`, and `tools/consolidation/foundation-path-map.json`.

## R10 — Constitution (FR-016)

- **Finding**: `.specify/memory/constitution.md` §E5 (`:103-107`) says that versions are computed and that `PackageVersioning.props` derives them. It never names the dataset or says where it lives. The gate index row for §E5 (`:42`) names `NoLiteralProjectVersionGuardTests` and `PackageRangesBuildCheckTests`, and neither reads the file. `constitution-framework.md` does not mention the dependency map. §E2.1 links the domain map, which spec 257 already re-pointed.
- **Decision**: no amendment is needed. The plan's Constitution Check records the check. If implementation finds a reference this search missed, it amends that section in-unit as a PATCH.

## R11 — Out of scope, recorded

- Spec 149's SC-005 ("the calculator never scans a project file") and its FR-006 wording ("consumers never run the generator") are superseded in part. The calculator now runs the shared scanner, though never the generator. Spec 149 is history and is not edited. This spec records the supersession.
- The solution filters stay committed. Removing them is a separate question: CI and IDEs read them directly.
