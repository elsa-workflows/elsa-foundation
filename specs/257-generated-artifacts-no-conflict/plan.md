# Implementation Plan: Generated artifacts never block or conflict

**Branch**: `agent/claude/257-generated-artifacts-no-conflict` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/257-generated-artifacts-no-conflict/spec.md`

## Summary

The documentation maps, the manifest and the two findings reports stop being committed. Git ignores them on `main`, generates them locally on demand, and CI publishes them after every merge, both as a build artifact and as a commit on a CI-only `generated-maps` branch. That branch's links are pinned to the source commit, and `main` links to it.

`check` narrows to the one committed input that tooling reads, `dependency-map.json`, and the solution filters keep their own check.

A plain-git classifier inside `Build & test` gives PRs that only change documentation a fast path. It still runs the cheap guards and the catalog link check, and it excludes any path that a test or build reads. Both required check names stay. Details are in [research.md](research.md).

## Technical Context

- **Language/Version**: C# on .NET 10 (`tools/maps/Elsa.Maps.Generator`); Python 3 (CI guards, as in `tools/ci/`, `tools/contributor-checkpoints/`); Bash (CI scripts); GitHub Actions YAML.
- **Primary Dependencies**: git CLI; actions already on the org allow-list (`actions/checkout@v4`, `setup-dotnet@v4`, `upload-artifact@v4`). No new actions.
- **Storage**: git refs. `refs/heads/generated-maps` is a new publication branch.
- **Testing**:
  - the generator's built-in self-test commands, which follow the existing `*-self-test` pattern;
  - `change_scope.py --self-test`;
  - `tools/contributor-checkpoints/test_workflow_guards.py`;
  - the existing `Elsa.Architecture.Tests` (`DependencyMapTests`, `NuGetLockFileTests`, `CatalogLinkGuardTests`), which are unchanged;
  - the throwaway-PR checks in [quickstart.md](quickstart.md).
- **Target Platform**: GitHub-hosted `ubuntu-latest` runners; contributor machines on Windows, macOS and Linux.
- **Project Type**: repository tooling and CI.
- **Performance Goals**: a docs-only PR's required checks finish in under 10 minutes (SC-003). The `Build & test` fast path aims for under 5 minutes.
- **Constraints**:
  - required contexts `Build & test` and `Generated maps fresh` keep their names (ruleset `main-2`, strict=false);
  - no third-party actions;
  - no force pushes;
  - the literal `python3 tools/contributor-checkpoints/test_workflow_guards.py` stays in `ci.yml`.
- **Scale/Scope**: 12 files untracked, 1 generator command added and 1 narrowed, 1 classifier script, 1 publish script, 2 `ci.yml` jobs changed and 1 added, about 20 docs and 8 skills edited, 1 constitution PATCH.

## Constitution Check

*Both constitutions loaded, plus `docs/contributing/review-checklist.md`.*

| Gate | Applies? | Result |
|---|---|---|
| Framework §2.1, §2.2, §2.3, §2.5/§2.23.1, §2.9, §2.20, §2.23.3 (product project, feature and persistence shape) | No: no product project, feature, package or persistence change | pass (n/a) |
| Elsa §E2.2 Runtime ↛ Design; §E2.3 Primitives; §E2.7 Elsa 3 boundary | No | pass (n/a) |
| Elsa §E5: computed versions, bounded ranges | `dependency-map.json` stays committed and checked, so the versioning calculator's input is unchanged | pass |
| Elsa §E6 type naming | Applies to the new generator types (for example `MapSetPublisher`, `PublishedLinkRewriter`): at most 4 components, none of the banned words. R4: the `…Publisher` suffix is not in the R4 table, but it is used for this sense by `Elsa.Versioning.Publisher` | pass, re-checked at review |
| Governance: the constitution changes in the same unit | §E2.1 links `docs/maps/domain-map.md` | PATCH 6.0.2 → 6.0.3 in this unit: the link is re-pointed to the `generated-maps` branch |
| Sanctioned patterns (framework §2.24.2) | No new runtime pattern. The CI publication branch reuses the `publish-state` precedent | pass |

**Post-design re-check**: unchanged, and no violations, so Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/257-generated-artifacts-no-conflict/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/cli-and-ci.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source code (repository root)

```text
.gitignore                                         # ignore the documentation map set
.github/workflows/ci.yml                           # classifier step, docs-only gating, publish-maps job, comments/summaries
tools/ci/change_scope.py                           # new: PR change-scope classifier + self-test
tools/maps/Elsa.Maps.Generator/
├── MapFreshness.cs                                # check → dependency-map only
├── ArchitectureReferenceMapGenerator.cs           # ReadSummary honours the redirect
├── Program.cs                                     # publish, publish-self-test dispatch + usage
└── MapSetPublisher.cs                             # new: generate-to-dir, link rewrite, set README
tools/maps/publish-generated-maps.sh               # new: plumbing push to generated-maps (ordering, no-op, retry)
tools/maps/check-catalog-links.py                  # only if R3's equivalence check finds a gap
tools/repo-hygiene/hygiene.sh                      # exempt generated-maps
tools/versioning/Elsa.Versioning.Calculator/VersionCalculator.cs   # fix hint names -- dependency-map
docs/maps/{12 files}                               # git rm --cached
docs/maps/README.md, AGENTS.md, docs/contributing/{generated-maps,merging,validation-matrix,repository-hygiene}.md,
docs/reference/{spec-lifecycle,elsa-domain-tree,framework-examples,first-user-prompts,nuget-lock-files}.md,
docs/skills/catalog.md, docs/README.md, docs/reports/README.md, Extensions/README.md,
.specify/templates/tasks-template.md, .specify/memory/constitution.md
.claude/skills/{elsa-auto-review-merge,elsa-refresh-generated-maps,elsa-extension-point-catalog,
  elsa-feature-dependency-map,elsa-feature-composition,elsa-verify-codebase,elsa-quality-fix,
  elsa-critical-constitution-review}/SKILL.md
.specify/integrations/claude.manifest.json         # wrapper hashes
```

**Structure Decision**: everything lands in the existing tooling locations. The generator gains one command. CI scripts go next to their peers in `tools/ci/` and `tools/maps/`. There are no new projects.

## Delivery order

1. **Generator.** Fix the redirect bug and narrow `check`. Then add `publish` and `publish-self-test`.
2. **Untracking.** Update `.gitignore` and untrack the 12 files, in the same commit as step 1, so `Generated maps fresh` stays green.
3. **Classifier.** Add `change_scope.py` and its self-test. Gate `ci.yml` on it, and verify the catalog-link equivalence.
4. **Publication.** Add the `publish-maps` job, `publish-generated-maps.sh` and the hygiene exemption.
5. **Docs, skills and constitution.** Update all of them, plus the manifest hashes.
6. **Validation.** Run the quickstart and the throwaway PRs. The owner creates the ruleset. Set `Status: Implemented` in the finishing PR.

## Complexity Tracking

None.
