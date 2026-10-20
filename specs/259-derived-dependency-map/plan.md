# Implementation Plan: The calculator derives the dependency map, so it is never committed

**Branch**: `agent/claude/259-derived-dependency-map` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/259-derived-dependency-map/spec.md`

## Summary

The scan that writes `docs/maps/dependency-map.json` moves out of the maps generator into a small library, `tools/maps/Elsa.Maps.ProjectGraph`. The library reads files through one tree view, and has two implementations: the working tree for the generator, and a commit's git objects for the calculator. When a commit has no committed dataset, the calculator derives the dataset by serializing that commit's tree with the shared scanner, then parses it with its existing validator. A commit that has a committed dataset keeps using it, so every recorded fingerprint stays stable.

A recorded comparison over 30 recent `main` commits proves both sources give identical versions and fingerprints. After that, the file is untracked, git-ignored and published with the other maps on `generated-maps`.

`-- check` and `Generated maps fresh` keep their names. They guard the solution filters and run the self-tests. Details are in [research.md](research.md).

## Technical Context

- **Language/Version**: C# on .NET 10 (`tools/maps`, `tools/versioning`); Bash for the validation script; Python 3 (`tools/ci/change_scope.py`); GitHub Actions YAML.
- **Primary Dependencies**:
  - the git CLI;
  - `System.Text.Json` and `System.Xml.Linq`, already used by both tools;
  - no new NuGet packages.
- **Storage**: git objects (read-only). `publish-state` and `generated-maps` refs are unchanged in shape.
- **Testing**:
  - xunit only, no FluentAssertions;
  - `Elsa.Versioning.Calculator.Tests`: new derived-dataset tests, and `SyntheticRepository` writes lock files;
  - `Elsa.Architecture.Tests`: `DependencyMapTests` and `NuGetLockFileTests` read the scanner output;
  - the generator's `*-self-test` commands, plus the new `project-graph-self-test`;
  - `change_scope.py --self-test`;
  - `MapSetPublisherContractTests`;
  - the validation script in [quickstart.md](quickstart.md) §3.
- **Target Platform**: GitHub-hosted `ubuntu-latest`, and contributor machines on Windows, macOS and Linux.
- **Project Type**: repository tooling and CI.
- **Performance Goals**: a calculator run with `--dataset derived` takes no more than 1.5× the `--dataset committed` run. The measure is the mean ratio over the 30 timed pairs in the validation script, on the machine that runs it (research R5, R8).
- **Constraints**:
  - calculator results are a function of commit and record only (spec 150 FR-009);
  - no checkout or restore inside the calculator;
  - required check names `Generated maps fresh` and `Build & test` unchanged;
  - no force pushes;
  - lock files restore in locked mode (`-p:RestoreLockedMode=true`), so every touched tool and test project commits its updated `packages.lock.json`.
- **Scale/Scope**:
  - 1 new library project and about 7 files moved into it;
  - 1 calculator source option and tree view;
  - 1 generator command added and 1 narrowed;
  - 1 file untracked;
  - about 4 test classes, 1 CI script, 1 workflow job, about 14 docs and 4 skills edited;
  - `Elsa.Server.slnx` and regenerated `.slnf` files.

## Constitution Check

*Both constitutions loaded, plus `docs/contributing/review-checklist.md`.*

| Gate | Applies? | Result |
|---|---|---|
| Framework §2.1, §2.2, §2.3, §2.5/§2.23.1, §2.9, §2.20, §2.23.3 (product project, feature, persistence shape) | No: tooling only, with no product package, feature or persistence change | pass (n/a) |
| Elsa §E2.2 Runtime ↛ Design; §E2.3; §E2.7 | No | pass (n/a) |
| Elsa §E5: versions computed, not authored; bounded ranges | The calculator still computes every version from the commit and the record. The only change is where the per-project facts come from, and FR-008 proves they are identical. §E5's text names no dataset location (research R10) | pass; no amendment |
| Elsa §E6 type naming | New types: `IRepositoryTree`, `CommitTree`, `DatasetSource`, `DependencyMapJson`, `Elsa.Maps.ProjectGraph`. Each has at most 4 components and no banned words. R4: none uses a reserved suffix in another sense | pass, re-checked at review |
| Repo rule: duplication beats dependency only for 5–10 lines | The scan is about 600 lines, so it is shared through a library rather than copied (research R3) | pass |
| Sanctioned patterns (framework §2.24.2) | No runtime pattern. A tool library shared by two tools has a precedent: the Publisher references the Calculator | pass |
| Governance: constitution changes land in-unit | §E5 and the framework were checked; neither names the committed file | no change needed (FR-016) |
| Unit tests (`elsa-add-unit-tests`) | Logic-bearing changes: the dataset source selection, the git-tree view, `IsProjectPath`, the narrowed `check` | covered by T-tasks in each phase |

**Post-design re-check**: unchanged. There are no violations, so Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/259-derived-dependency-map/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/cli.md
├── checklists/requirements.md
├── validate-dataset-sources.sh   # implementation, before the removal commit
└── tasks.md                      # /speckit-tasks
```

### Source code (repository root)

```text
tools/maps/Elsa.Maps.ProjectGraph/                   # new library (moved from the generator)
├── Elsa.Maps.ProjectGraph.csproj, packages.lock.json
├── IRepositoryTree.cs                               # new
├── RepoContext.cs                                   # moved; implements IRepositoryTree
├── RepoLayout.cs                                    # project-path part moved; + IsProjectPath
├── ProjectFacts.cs, ProjectFile.cs, PackageVersions.cs, PinnedTransitiveDependencies.cs   # moved; read via IRepositoryTree
└── DependencyMapJson.cs                             # dataset record + JSON options (from DependencyMap.cs)
tools/maps/Elsa.Maps.Generator/
├── Elsa.Maps.Generator.csproj, packages.lock.json   # + ProjectReference
├── RepoLayout.cs                                    # keeps source/catalog pathspecs, or a partial
├── DependencyMap.cs                                 # writes DependencyMapJson.Serialize(repo)
├── MapFreshness.cs                                  # check → solution filters
├── MapSetPublisher.cs, MapSetPublisherContractTests.cs   # dataset joins the published set
├── ProjectGraphContractTests.cs                     # new: project-graph-self-test
└── Program.cs                                       # usage, check, project-graph-self-test
tools/versioning/Elsa.Versioning.Calculator/
├── Elsa.Versioning.Calculator.csproj, packages.lock.json   # + ProjectReference
├── CommitTree.cs                                    # new: IRepositoryTree over a commit
├── DatasetSource.cs                                 # new
├── CommitSnapshot.cs                                # Map from committed or derived source
├── DependencyMapDataset.cs                          # Parse unchanged; doc comment
├── VersionCalculator.cs                             # Compute(…, DatasetSource); messages
└── Program.cs                                       # --dataset
tools/versioning/Elsa.Versioning.Publisher/packages.lock.json
Essentials/tests/Versioning/Calculator/Tests/        # DerivedDatasetTests.cs (new); Support/SyntheticRepository.cs; packages.lock.json
Essentials/tests/Architecture/Tests/                 # DependencyMapTests, NuGetLockFileTests, VersionLineOwnershipGuardTests; packages.lock.json
Elsa.Server.slnx, Elsa.Server.*.slnf                 # add the library; regenerate filters
.gitignore, docs/maps/dependency-map.json (git rm --cached)
.github/workflows/ci.yml                             # Generated maps fresh steps + comments
tools/ci/change_scope.py
AGENTS.md, docs/maps/README.md, docs/contributing/{generated-maps,merging,validation-matrix}.md,
docs/reference/{nuget-lock-files,developer-solution-filters}.md, docs/skills/catalog.md, Extensions/README.md,
VersionLines.props, tools/versioning/README.md, tools/maps/generate-maps.{sh,ps1}
.claude/skills/{elsa-auto-review-merge,elsa-feature-dependency-map,elsa-quality-fix,elsa-refresh-generated-maps}/SKILL.md
.specify/integrations/claude.manifest.json
```

**Structure Decision**: one new library under `tools/maps/`, which owns the project-graph facts. The calculator gains a tree view and a source option. Everything else is edits in place.

## Delivery order

Everything ships in one PR, and the commits are ordered so that the gate in step 4 runs before the file disappears.

1. **Extract.** Move the scan into `Elsa.Maps.ProjectGraph` behind `IRepositoryTree`, with no behavior change. The generator output must be byte-identical (FR-007). Add `project-graph-self-test`, update the slnx, the filters and the lock files.
2. **Derive.** Add `CommitTree`, `DatasetSource`, `--dataset` and the derived-dataset tests. Teach `SyntheticRepository` to write lock files. With `auto`, nothing changes yet, because every commit still has its file.
3. **Consumers.** Point `DependencyMapTests` and `NuGetLockFileTests` at the scanner.
4. **Validate (gate).** Run `validate-dataset-sources.sh 30` and record the table and the timings in the PR. Stop if any row differs.
5. **Remove.** In one commit: `git rm --cached` the file, update `.gitignore`, add the dataset to the published set, narrow `check`, change the `ci.yml` job, update `change_scope.py`.
6. **Docs and skills.** Update every doc and skill, plus the manifest hashes. Record the constitution check result.
7. **Finish.** Run the quickstart. Set `Status: Implemented` in this PR.

## Complexity Tracking

None.
