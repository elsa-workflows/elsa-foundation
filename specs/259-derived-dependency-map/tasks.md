# Tasks: The calculator derives the dependency map, so it is never committed

**Input**: Design documents from `specs/259-derived-dependency-map/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/cli.md](contracts/cli.md), [quickstart.md](quickstart.md)

**Tests**: xunit only, with no FluentAssertions. The tests here are risk-based (framework §2.23) and cover the logic-bearing changes: dataset source selection, the git-tree view, `IsProjectPath`, the narrowed `check` and the published set. The repository declares no TDD cadence for tooling, so tests sit beside their implementation task.

**Order**: one PR. Phase 4 (US2) contains the gate that must pass before Phase 5 (US1) removes the file, even though both stories are P1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on unfinished tasks).
- **[Story]**: US1–US4 from spec.md.

---

## Phase 1: Setup

- [ ] T001 Create the library project `tools/maps/Elsa.Maps.ProjectGraph/Elsa.Maps.ProjectGraph.csproj` (`Sdk="Microsoft.NET.Sdk"`, `RootNamespace` `Elsa.Maps.ProjectGraph`, `IsPackable` false, inheriting the repo's TargetFramework). Add it to `Elsa.Server.slnx` under `/tools/maps/`.
- [ ] T002 Add `<ProjectReference>` to the library from `tools/maps/Elsa.Maps.Generator/Elsa.Maps.Generator.csproj` and `tools/versioning/Elsa.Versioning.Calculator/Elsa.Versioning.Calculator.csproj`. Run `dotnet restore Elsa.Server.slnx` and commit the changed `packages.lock.json` files: library, generator, calculator, publisher, `Essentials/tests/Versioning/Calculator/Tests`, `Essentials/tests/Architecture/Tests`, and any other project the restore rewrites.
- [ ] T003 Regenerate the solution filters with `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters` and commit `Elsa.Server.*.slnf` if they change. `solution-filters-check` must pass.

---

## Phase 2: Foundational — extract the scanner (blocks all stories; FR-006, FR-007)

**Goal**: one scan, read through `IRepositoryTree`, with generator output byte-identical to before.

- [ ] T004 Add `tools/maps/Elsa.Maps.ProjectGraph/IRepositoryTree.cs` with `ProjectFiles()`, `Exists(path)` and `Read(path)`, as in data-model.md "Repository tree view", with XML docs in the generator's comment style.
- [ ] T005 Move `RepoContext.cs` into the library, make it implement `IRepositoryTree`, and keep `Discover`, `ListFiles`, `Absolute` and its `--others` listing. `ProjectFiles()` is `ListFiles(RepoLayout.ProjectPathspecs)`.
- [ ] T006 Move the project-path part of `RepoLayout` (`ProjectPathspecs`, `IsTestPath`, `IsTestProjectPath`, `Kind`) into `tools/maps/Elsa.Maps.ProjectGraph/RepoLayout.cs` and add `IsProjectPath(string path)`, the layout rule from research R3: a `.csproj` under `Essentials/{src,apps,tests}/` or `Extensions/<capability>/{src,tests}/`, at any depth. `RepoContext.ProjectFiles()` filters its `ls-files` output through it, so both tree views use one rule. The source and catalog pathspecs and `SourceFiles` stay in the generator, in a generator-side type such as `SourceLayout`, or as a second static class. Update every caller in `tools/maps/Elsa.Maps.Generator/*.cs`.
- [ ] T007 Move `ProjectFacts.cs` (records and `ProjectGraph`), `ProjectFile.cs`, `PackageVersions.cs` and `PinnedTransitiveDependencies.cs` into the library. Replace every `File.Exists`, `File.ReadAllBytes`, `XDocument.Load(path)` and `repo.Absolute` with `IRepositoryTree` calls. `ProjectFile.Load(IRepositoryTree, path)` parses from bytes. `ProjectGraph.Read(IRepositoryTree)` uses `tree.ProjectFiles()`. Keep every exception message as it is. Change the namespace to `Elsa.Maps.ProjectGraph`, and add `using` lines in the generator.
- [ ] T008 Add `tools/maps/Elsa.Maps.ProjectGraph/DependencyMapJson.cs` with the dataset record, `SchemaVersion = 2`, the JSON options from `DependencyMap.cs:40-46`, and `string Serialize(IRepositoryTree tree)`. The result is the JSON with a trailing `\n`, the generator string still `tools/maps/Elsa.Maps.Generator`, built from `PinnedTransitiveDependencies.Attach(tree, ProjectGraph.Read(tree))`. Reduce `tools/maps/Elsa.Maps.Generator/DependencyMap.cs` to writing `DependencyMapJson.Serialize(repo)` through `MarkdownTable.Write`, and keep `RelativePath`.
- [ ] T009 Point `Essentials/tests/Architecture/Tests` at the moved types. `PinnedTransitivePackTests.cs` (`using Elsa.Maps.Generator`; `RepoContext.Discover`, `PinnedTransitiveDependencies.Attach`) and `SpecScannerTests.cs` (`RepoContext.Discover`) gain `using Elsa.Maps.ProjectGraph`. Add a direct library `ProjectReference` in `Elsa.Architecture.Tests.csproj` if transitive access is not enough. If `ArchitectureGuardTests.InternalsVisibleTo.cs` (`:243`, `MapsGenerator`) allow-lists test-to-tool references by path, add `tools/maps/Elsa.Maps.ProjectGraph/Elsa.Maps.ProjectGraph.csproj` there too. (`ProjectGraph.ReferencedProjectPaths` and `ProjectGraph.ElsaProjectPaths` in the tests are the test suite's own helper, not the generator's type; check for a name clash after the `using` change.)
- [ ] T010 Verify FR-007: on a clean tree, run `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, then `git diff --stat -- docs/maps/dependency-map.json` (empty), and `-- project-facts-self-test`, `-- feature-facts-self-test`, `-- repo-layout-self-test` and `-- solution-filters-self-test` (all exit 0). Record the result for the PR evidence.

**Checkpoint**: the generator behaves as before, and the scanner lives in one place.

---

## Phase 3: User Story 3 — the generator and the calculator cannot disagree (P2, foundational for US2)

**Goal**: pin the shared scanner's contract so a second implementation cannot creep back.

- [ ] T011 [US3] Add `tools/maps/Elsa.Maps.Generator/ProjectGraphContractTests.cs`, following the existing `*ContractTests.cs` self-test pattern. It asserts that `RepoLayout.IsProjectPath` selects what `git ls-files -- <ProjectPathspecs>` selects on the live tree. It also covers synthetic paths: `Extensions/A/src/X.csproj` (in), `Extensions/A/src/Sub/X.csproj` (in), `Extensions/A/tests/Sub/X.csproj` (in), `Extensions/A/studio/X.csproj` (out), `Extensions/A/studio/src/X.csproj` (out, though git's pathspec would match it), `Studio/src/X.csproj` (out), `tools/x/X.csproj` (out) and `Essentials/tests/A/B/X.csproj` (in).
- [ ] T012 [US3] Wire `project-graph-self-test` into `tools/maps/Elsa.Maps.Generator/Program.cs`, with its dispatch, the usage string at `:121` and the header comment.
- [ ] T013 [US3] In `.github/workflows/ci.yml`, add the step "Exercise project graph contract" (`-- project-graph-self-test`) to the `generated-maps` job, beside the other self-tests, with the same `if:` guard.

**Checkpoint**: `rg -l "class PinnedTransitiveDependencies|class PackageVersions|class ProjectGraph" tools/` lists only `tools/maps/Elsa.Maps.ProjectGraph/` (SC-004).

---

## Phase 4: User Story 2 — publishing computes the same versions without the committed file (P1)

**Goal**: the calculator derives a commit's dataset from its git objects, and both sources are proven equal before the switch.

**Independent test**: quickstart §2 and §3.

- [ ] T014 [US2] Add `tools/versioning/Elsa.Versioning.Calculator/CommitTree.cs`, an internal `IRepositoryTree` over one commit. It wraps the tree-entry dictionary and the shared `ObjectCache` that `CommitSnapshot` already has. `ProjectFiles()` filters tree paths with `RepoLayout.IsProjectPath`, ordinal. `Read` throws `InvalidOperationException` naming the commit and path when the entry is absent.
- [ ] T015 [US2] Add `tools/versioning/Elsa.Versioning.Calculator/DatasetSource.cs` (`Auto`, `Committed`, `Derived`). In `CommitSnapshot.cs`:
  - take a `DatasetSource`;
  - replace the eager `Map` read at `:50-53` with: committed bytes when the source is `Committed`, or when it is `Auto` and the tree has the file;
  - otherwise `DependencyMapDataset.Parse(DependencyMapJson.Serialize(new CommitTree(...)), $"derived dependency map at {commit}")`;
  - wrap a scanner `InvalidOperationException` as `Cannot derive the dependency map at <sha>: <message>`;
  - for `Committed` with no file: `Commit <sha> has no docs/maps/dependency-map.json; use --dataset auto or derived.`;
  - rewrite the class remarks (`:15-19`) to describe both sources.
- [ ] T016 [US2] In `tools/versioning/Elsa.Versioning.Calculator/VersionCalculator.cs`:
  - add `DatasetSource source = DatasetSource.Auto` to `Compute` and pass it to every `CommitSnapshot`;
  - update the class remarks (`:13-16`, "the dependency map committed in them");
  - in `RequireMapDescribesTree` (`:138-155`), name the source in the message, and give the "Regenerate it with" hint only for a committed dataset. For a derived one, list the projects each side found that the other did not, with their paths, and point to the layout rule in `RepoLayout.IsProjectPath`. Assign no blame: the cause can be a project on an unsupported path or a scanner defect.
- [ ] T017 [US2] Add `--dataset auto|committed|derived` to `tools/versioning/Elsa.Versioning.Calculator/Program.cs`: the argument whitelist at `:21-22`, the usage at `:6-14`, parsing with exit 2 on an unknown value, and passing it to `Compute`. `Elsa.Versioning.Publisher` does not change (contracts/cli.md).
- [ ] T018 [US2] Update `Essentials/tests/Versioning/Calculator/Tests/Support/SyntheticRepository.cs` (`:48`, `:164`). Write a minimal version-2 `packages.lock.json` beside each synthetic project, as the scanner requires: `Direct` entries for its package references, `Project` entries for its reference closure, and `CentralTransitive` for pinned transitive packages the test declares. Stop writing `docs/maps/dependency-map.json` by default. Keep an opt-in that writes the committed dataset, generated through `DependencyMapJson.Serialize` over the synthetic working tree. Existing calculator tests must pass unchanged in intent.
- [ ] T019 [P] [US2] Add `Essentials/tests/Versioning/Calculator/Tests/DerivedDatasetTests.cs` with xunit facts:
  - (a) `Auto` on a commit with a committed dataset reads it, which a hand-edited committed file distinguishes;
  - (b) `Auto` on a commit without one derives it;
  - (c) `Committed` without a file exits with the FR-004 message;
  - (d) a derivation failure (a stale lock file) names the commit and the lock file, and computes nothing;
  - (e) on the real repository, `DependencyMapJson.Serialize(new CommitTree(HEAD))` equals `DependencyMapJson.Serialize(RepoContext.Discover())` byte for byte (FR-003). Because `RepoContext` also lists untracked files, the fact returns early, saying why, when `git status --porcelain -- Essentials Extensions Directory.Build.props Directory.Packages.props VersionLines.props` shows any change. CI checkouts are clean, so it always runs there;
  - (f) a record commit with a committed dataset and a built commit without one give an unchanged package its recorded version and no `affected` flag.
- [ ] T020 [P] [US2] Add CLI cases to `Essentials/tests/Versioning/Calculator/Tests/CliTests.cs` for `--dataset derived`, `--dataset committed` on a commit without a file (exit 2), and `--dataset bogus` (exit 2 with usage).
- [ ] T021 [US2] Write `specs/259-derived-dependency-map/validate-dataset-sources.sh` (research R8). The argument is N, default 30. For each of the last N `git rev-list --first-parent origin/main` commits, it:
  - picks the newest `origin/publish-state` revision whose `last_publish_commit` is an ancestor or self of the commit;
  - builds the calculator once (`-c Release`) and runs it with `--dataset committed` and `--dataset derived` into a scratch directory;
  - byte-compares the outputs, and also compares the committed and derived dataset bytes for diagnosis;
  - times both runs;
  - for the commit and every commit its record revision names, compares the committed dataset bytes with `DependencyMapJson.Serialize` over that commit's tree. Byte equality covers the mixed post-switch configuration (research R8). An old-layout record commit stops the script with a message; none is expected;
  - prints a Markdown table (commit, record revision, versions/fingerprints `same|differs`, dataset bytes `same|differs` for the commit and its record commits, seconds committed/derived) and the mean time ratio, and exits 1 on any `differs` in either column.

  It uses `python`/`python3` detection for JSON and needs no `jq`.
- [ ] T022 [US2] **Gate.** Run `bash specs/259-derived-dependency-map/validate-dataset-sources.sh 30` before any Phase 5 commit. Post the table as a PR comment headed "Spec 259 dataset-source validation (FR-008)". If any row differs, in versions or in dataset bytes, stop: diagnose with the dataset diff, fix the scanner or the tree view, and re-run. Do not continue to Phase 5 on a difference. If the mean derived/committed time ratio exceeds 1.5, apply the research R5 fallback in `tools/versioning/Elsa.Versioning.Calculator/GitRepository.cs` (one long-lived `git cat-file --batch`), and re-run.

**Checkpoint**: US2 is proven; the file can be removed safely.

---

## Phase 5: User Story 1 — concurrent project PRs never conflict on the dependency map (P1)

**Goal**: the file leaves `main`, the required job stays green, and readers move to the published copy.

**Independent test**: quickstart §4–§6; spec US1 acceptance scenarios.

- [ ] T023 [US1] Point `Essentials/tests/Architecture/Tests/DependencyMapTests.cs` `ReadNodes` (`:212-220`) at `DependencyMapJson.Serialize(RepoContext.Discover(RepoRoot))`, parsed with the test's own DTOs. Keep the filesystem-walk oracle. Reword the class summary (`:9-18`): the oracle never reads the scanner's internals, and freshness no longer exists because the dataset is derived.
- [ ] T024 [P] [US1] Point `Essentials/tests/Architecture/Tests/NuGetLockFileTests.cs` (`:46-57`) at the same serialization. Reword the summary at `:46-53`: "regenerate the same wrong map the maps check compares with" becomes "derive the same wrong dataset the calculator reads".
- [ ] T025 [P] [US1] Reword `Essentials/tests/Architecture/Tests/VersionLineOwnershipGuardTests.cs:13` to "the dependency-map dataset (derived from the same file)". No logic change.
- [ ] T026 [US1] **Removal commit** (one commit, after T022 passed):
  - `git rm --cached docs/maps/dependency-map.json`;
  - in `.gitignore` (`:488-495`), add `docs/maps/dependency-map.json` and change the comment to "README.md stays committed; dependency-map.json is derived by the versioning calculator (spec 259)";
  - in `tools/maps/Elsa.Maps.Generator/MapFreshness.cs`, `Check` runs the solution-filter freshness check (`SolutionFilterGenerator`'s check entry point, which `solution-filters-check` calls), with rewritten remarks and messages;
  - in `tools/maps/Elsa.Maps.Generator/Program.cs` (`:7-17`, `:106-114`), update the comments and the `check` description;
  - in `tools/maps/Elsa.Maps.Generator/MapSetPublisher.cs` (`:30-41`), add `DependencyMap.Generate(repo, projects)` first in the published list and update the comment;
  - in `tools/maps/Elsa.Maps.Generator/MapSetPublisherContractTests.cs:25`, the expectation is that `[ds](dependency-map.json)` stays relative;
  - in `.github/workflows/ci.yml`, remove the "Check the committed dependency map against the tree" step and the "Explain how to refresh the dependency map" step (`:587-600`), rewrite the job comment block (`:478-500`), and keep `name: Generated maps fresh`;
  - in `tools/ci/change_scope.py`, drop `:39` and the self-test case at `:105`. Add `(["Essentials/src/X/packages.lock.json"], False)` if it is not already covered.
- [ ] T027 [US1] Run quickstart §4–§6 locally: `git ls-files` is empty, `check-ignore` matches, `-- check` exits 0, `change_scope.py --self-test` passes, the calculator computes at `HEAD` with `--record-ref origin/publish-state`, and the three architecture test classes pass after a restore. Record the results for the PR evidence.
- [ ] T027a [US1] SC-005 check: from the PR head, create two local throwaway branches that each add one packable project with its lock file under `Extensions/Probe/src/` (different names). Merge the first into the second and confirm that `git merge` reports no conflict and that `dotnet run --project tools/maps/Elsa.Maps.Generator -- check` passes. Push neither branch; delete both afterwards and record the result for the PR evidence.

**Checkpoint**: the PR's `Generated maps fresh` and `Build & test` pass, and no file under `docs/maps/` is tracked except `README.md`.

---

## Phase 6: User Story 4 — agents and people still find the dataset (P3)

**Goal**: no doc or skill describes the dataset as committed (FR-015).

- [ ] T028 [P] [US4] `AGENTS.md`: in the "Refresh generated maps" paragraph, drop "`-- check` guards only the committed `docs/maps/dependency-map.json`; commit it and the solution filters only when they change". Say that `-- check` guards the committed solution filters and the dataset is derived by the versioning calculator. Check the source-of-truth row that mentions the maps.
- [ ] T029 [P] [US4] Rewrite the dataset paragraphs in `docs/maps/README.md` and `docs/contributing/generated-maps.md`: the dataset is derived per commit by the calculator, published on `generated-maps`, and generated locally with `-- dependency-map`.
- [ ] T030 [P] [US4] Update the dataset mentions in `docs/contributing/merging.md`, `docs/contributing/validation-matrix.md`, `docs/reference/nuget-lock-files.md`, `docs/reference/developer-solution-filters.md`, `Extensions/README.md` and `VersionLines.props:13` (comment only).
- [ ] T031 [P] [US4] `tools/versioning/README.md`: "What it reads" (`:33-46`), the exit-code 2 list (`:31`), `:36`, `:97` and `:100` describe the committed-or-derived source and `--dataset`. Add the option to the usage block.
- [ ] T032 [P] [US4] Update `tools/maps/generate-maps.sh`, `tools/maps/generate-maps.ps1`, and the comments in `tools/maps/Elsa.Maps.Generator/DependencyMap.cs` (`:7-28`) and `tools/maps/Elsa.Maps.Generator/FeatureDependencyMapGenerator.cs` that call the dataset committed.
- [ ] T033 [US4] Update the skills that mention the committed dataset or staging it: `.claude/skills/elsa-auto-review-merge/SKILL.md`, `.claude/skills/elsa-feature-dependency-map/SKILL.md`, `.claude/skills/elsa-quality-fix/SKILL.md` and `.claude/skills/elsa-refresh-generated-maps/SKILL.md`, plus their entries in `docs/skills/catalog.md`. Update each wrapper's SHA256 in `.specify/integrations/claude.manifest.json`, and run `pwsh tools/skills/validate-claude-wrapper-drift.ps1`, which must pass.
- [ ] T034 [US4] Run `git grep -n "dependency-map.json" -- ':!specs' ':!docs/reports'`. Every remaining hit must describe the derived or published dataset or the local output path; none may call it committed.

---

## Phase 7: Polish & cross-cutting

- [ ] T035 Constitution (FR-016): re-read `.specify/memory/constitution.md` §E5 (`:103-107`), its gate-index row (`:42`) and `.specify/memory/constitution-framework.md` for any rule naming the committed dataset. Research R10 found none. If one turns up, amend it in this PR as a PATCH, with a sync-impact note, and update the `AGENTS.md` constitution-version line.
- [ ] T036 Run `dotnet run --project tools/maps/Elsa.Maps.Generator -- solution-filters-check` and the touched test projects only (`Essentials/tests/Versioning/Calculator/Tests`, `Essentials/tests/Architecture/Tests` filtered to the three classes). Leave the full matrix to CI, per AGENTS.md "Builds on a shared machine".
- [ ] T036a Add one line under the **Input** line of `specs/149-canonical-dependency-map/spec.md`: "Superseded in part by [spec 259](../259-derived-dependency-map/spec.md): the dataset is no longer committed; the versioning calculator derives it per commit with the shared scanner, so SC-005 now holds as 'the calculator never runs the generator', not 'never scans a project file'." Change nothing else in spec 149.
- [ ] T037 Set `**Status**: Implemented` in `specs/259-derived-dependency-map/spec.md` in this same PR. Post the gate evidence comment (T010, T022, T027 and T027a results). List as pending, to check after merge: SC-001 (no generated `docs/maps/` file changes on `main`), SC-003 (the first `packages.yml` run advances only packages with real input changes) and the `generated-maps` copy of the dataset (quickstart §7). Then request review with `@elsa-workflows-bot review`.

---

## Dependencies & execution order

- Phase 1 → Phase 2 → Phase 3 (US3) → Phase 4 (US2, ending with the T022 gate) → Phase 5 (US1) → Phase 6 (US4) → Phase 7.
- US2 depends on US3's shared library. US1 depends on US2's gate. US4 depends on US1, because the docs describe the post-removal state.
- Within Phase 4: T014 → T015 → T016 → T017. T018 comes before T019 and T020, which run in parallel. T021 → T022.
- Within Phase 5: T023, T024 and T025 run in parallel, then T026, then T027, then T027a.
- Within Phase 6: T028–T032 run in parallel, and T033 and T034 follow.

## Parallel examples

```text
Phase 4, after T018:  T019 DerivedDatasetTests.cs  ||  T020 CliTests.cs
Phase 5:              T023 DependencyMapTests.cs   ||  T024 NuGetLockFileTests.cs  ||  T025 VersionLineOwnershipGuardTests.cs
Phase 6:              T028 AGENTS.md || T029 maps docs || T030 reference docs || T031 versioning README || T032 scripts/comments
```

## Implementation strategy

- **MVP**: Phases 1–4. The shared scanner and the `--dataset` option are useful and safe on their own: with `auto`, nothing changes while every commit still has its file.
- **Switch**: Phase 5, only on a clean T022 table.
- **Finish**: Phases 6–7 in the same PR, so `main` never has docs that describe a file that no longer exists.
