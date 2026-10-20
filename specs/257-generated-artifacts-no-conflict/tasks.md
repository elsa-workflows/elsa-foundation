---

description: "Tasks for spec 257: generated artifacts never block or conflict"
---

# Tasks: Generated artifacts never block or conflict

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/cli-and-ci.md](contracts/cli-and-ci.md), [quickstart.md](quickstart.md)

**Tests**: This unit has no Foundation feature class, so framework §2.23 feature-registration tests do not apply. Logic-bearing tooling gets risk-based self-tests in the repository's existing tooling pattern (`*-self-test` generator commands, `--self-test` Python scripts):

- the link rewrite;
- the change-scope classification;
- the ordering and no-op rules of the branch publisher.

No test-first cadence is declared for repository tooling, so tests sit beside their implementation task, not before it. That gap is the owner's to resolve and is not inferred here.

**Ordering constraint**: T003–T005 land in one commit. Untracking the maps before `check` narrows would turn the required `Generated maps fresh` red.

## Phase 1: Setup

- [ ] T001 Confirm the baseline: `dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- check` and `python3 tools/maps/check-catalog-links.py --all-readmes` pass on the branch tip. Record their runtime in the PR notes as the fast-path budget reference.

---

## Phase 2: Foundational (blocks every story)

- [ ] T002 Fix `ReadSummary` in `tools/maps/Elsa.Maps.Generator/ArchitectureReferenceMapGenerator.cs` (`:118-133,161-168`). It must read `domain-map.md` and `extension-point-map.md` through the active `MarkdownTable.Redirect` location (`tools/maps/Elsa.Maps.Generator/MarkdownTable.cs:22,28-31`), falling back to the repo path only when no redirect is set, so v2 findings describe the run's own outputs.
- [ ] T003 Narrow `MapFreshness.Check` in `tools/maps/Elsa.Maps.Generator/MapFreshness.cs` to `DependencyMap.Generate` only (drop `:35-39`):
  - rewrite the doc comment (`:8-18`) to say it guards the committed dependency-map dataset;
  - rewrite the failure text (`:55-64`) to name `-- dependency-map` as the fix;
  - update the usage comment in `tools/maps/Elsa.Maps.Generator/Program.cs:6-15` to match.
- [ ] T004 Add ignore rules to `.gitignore`: `docs/maps/*.md`, `!docs/maps/README.md`, `docs/maps/manifest.json`, `docs/reports/maps-v1-findings.md`, `docs/reports/maps-v2-findings.md`. Each gets a one-line comment pointing to `docs/contributing/generated-maps.md`.
- [ ] T005 Untrack the 12 generated files with `git rm --cached`:
  - `docs/maps/{architecture-reference-map,domain-map,extension-point-map,feature-dependency-map,feature-map,package-map,project-reference-map,spec-status-map,test-map}.md`
  - `docs/maps/manifest.json`
  - `docs/reports/maps-v1-findings.md`, `docs/reports/maps-v2-findings.md`

  Then run `-- all` and confirm `git status --short docs/` is empty and `-- check` passes. Commit T003–T005 together.

**Checkpoint**: main can merge the PR with both required checks green, and no documentation map is committed.

---

## Phase 3: User Story 1 — Concurrent PRs merge without generated-file conflicts (P1) 🎯 MVP

**Goal**: no PR has to touch a generated documentation file, and no instruction tells anyone to.

**Independent test**: two branches from one base each add a `specs/9xx-probe-*` directory and a test project stub. After the first merges, the second has no conflict and passes `Generated maps fresh` without regenerating anything (quickstart, CI step 4).

- [ ] T006 [US1] Update the `Generated maps fresh` job in `.github/workflows/ci.yml`:
  - rewrite the header comment (`:437-462`) to say the job guards `dependency-map.json` and the solution filters, and that the documentation maps are published, not committed;
  - fix the slnf comment at `:377`;
  - in the "Explain how to refresh the maps" summary (`:524-551`), name only `-- dependency-map` and staging `docs/maps/dependency-map.json`, and drop the `manifest.json` and "open a PR with the refreshed maps" text;
  - keep the job name.
- [ ] T007 [P] [US1] In `tools/versioning/Elsa.Versioning.Calculator/VersionCalculator.cs:143,150`, change the fix hint from `-- all` to `-- dependency-map`.
- [ ] T008 [P] [US1] Remove the "stage every changed map … `docs/maps/manifest.json` included" instructions and replace them with "commit `docs/maps/dependency-map.json` and solution filters only when they change" in:
  - `AGENTS.md:74-76`
  - `docs/skills/catalog.md:118,372-378`
  - `.claude/skills/elsa-auto-review-merge/SKILL.md:25`
  - `Extensions/README.md:132-133`
- [ ] T009 [P] [US1] Drop the "regenerate the spec-status map" clause from the Status-line task in `.specify/templates/tasks-template.md:162`. In `docs/contributing/merging.md:19,21` and `docs/reference/spec-lifecycle.md:55`, remove the regenerated spec-status map from the spec-finishing PR and drop the map-conflict rationale.
- [ ] T010 [P] [US1] In `docs/contributing/validation-matrix.md:7`, `docs/contributing/gitbook-source-setup.md:39`, `docs/reference/nuget-lock-files.md:13-15,31`, `docs/reference/developer-solution-filters.md:123` and `docs/reference/rest-api-migration-gates.md:36`, describe `Generated maps fresh` and `-- check` with their narrowed meaning (the dependency-map dataset plus solution filters).

**Checkpoint**: US1 is deliverable alone. Concurrent PRs no longer conflict on maps. The maps are only available by local generation until US2 lands.

---

## Phase 4: User Story 2 — Agents and people still get current maps (P1)

**Goal**: every map is one command or one link away, including for agents that only browse GitHub.

**Independent test**: quickstart "Local" plus "After merge". The branch exists after the merge commit, its tip names that commit, a source link in `test-map.md` opens the pinned project file, and the run carries the `generated-maps-<sha>` artifact.

- [ ] T011 [US2] Add `tools/maps/Elsa.Maps.Generator/MapSetPublisher.cs`. It:
  - generates every layer into an output directory through `MarkdownTable.Redirect`, without touching the working tree;
  - rewrites each relative markdown link whose resolved repo path is outside the published set to `<repo-url>/blob/<sha>/<path>`. In-set links, `#anchors` and absolute URLs are left untouched;
  - writes `<out>/README.md`, naming the source commit, the CI writer convention and the local `-- all` command.
- [ ] T012 [US2] Wire `publish <out-dir> --source-commit <sha> --repo-url <url>` into `tools/maps/Elsa.Maps.Generator/Program.cs`, with exit 2 on bad arguments. Add a `publish-self-test` command with fixture cases for the rewrite:
  - in-set relative link kept;
  - `../../Essentials/x.csproj` pinned;
  - `../reports/maps-v1-findings.md` kept;
  - a link to a non-set report pinned;
  - anchor-only and `https://` links untouched;
  - working tree unchanged after `publish`.
- [ ] T013 [US2] Add `tools/maps/publish-generated-maps.sh <set-dir> <source-sha>`, the plain-git push per research R5:
  - ls-remote exit 2 → bootstrap an orphan commit;
  - read the tip's `Source-Commit:` trailer and run the `merge-base --is-ancestor` ordering check, exiting 0 with "superseded";
  - build the tree through a temporary `GIT_INDEX_FILE`, exiting 0 with "unchanged" when it equals the tip's tree;
  - `commit-tree` as `github-actions[bot]`, with message `Maps for <short-sha>` and the trailer `Source-Commit: <sha>`;
  - push without force, retrying once from the fetch on rejection.
- [ ] T014 [US2] Add `tools/maps/test-publish-generated-maps.sh`, run against a temporary bare remote. It asserts bootstrap, append, superseded (older sha), unchanged (same tree), a hand commit without a trailer treated as superseded-safe, and no force push.
- [ ] T015 [US2] Add the `publish-maps` job ("Publish generated maps") to `.github/workflows/ci.yml`:
  - `if: github.event_name == 'push'`, `permissions: contents: write`, job concurrency `{group: generated-maps, cancel-in-progress: true}`, `fetch-depth: 0`;
  - steps: build the generator locked, run `publish "$RUNNER_TEMP/maps" --source-commit "$GITHUB_SHA" --repo-url "$GITHUB_SERVER_URL/$GITHUB_REPOSITORY"`, upload `actions/upload-artifact@v4` with name `generated-maps-${{ github.sha }}` and `if-no-files-found: error`, then run `bash tools/maps/publish-generated-maps.sh "$RUNNER_TEMP/maps" "$GITHUB_SHA"`;
  - do not add it to the alert job's needs, and comment why.
- [ ] T016 [US2] Add the `publish-self-test` command and `bash tools/maps/test-publish-generated-maps.sh` as steps of the `Generated maps fresh` job in `.github/workflows/ci.yml`, gated like the other self-tests.
- [ ] T017 [P] [US2] Add `generated-maps` to `exempt_re` in `tools/repo-hygiene/hygiene.sh:28`, and to the "Never swept" list in `docs/contributing/repository-hygiene.md:34`.
- [ ] T018 [US2] Rewrite `docs/maps/README.md` as the entry point:
  - what each map holds;
  - where to read it: browse URL, raw-URL pattern, `git fetch origin generated-maps`, the CI artifact, or local `-- all`;
  - that the maps are never committed to `main`;
  - that `dependency-map.json` is the one committed dataset.

  Keep the spec-status metadata and dataset sections.
- [ ] T019 [US2] Rewrite `docs/contributing/generated-maps.md`:
  - retire "committed snapshots, never refreshed automatically";
  - document local generation, the `generated-maps` branch and its ruleset, the artifact, and the narrowed `check`;
  - record the findings-report review as reading the branch's findings after a merge.
- [ ] T020 [P] [US2] In `AGENTS.md:29,32,99`, point map navigation at `docs/maps/README.md` and add the browse URL and raw-URL route, so an agent reading only GitHub finds the maps in one link.
- [ ] T021 [P] [US2] Re-point the map links in these docs to `docs/maps/README.md` or the `generated-maps` browse URL, and replace "refresh the map" with "generate locally or read `generated-maps`":
  - `docs/reference/spec-lifecycle.md:12,81`
  - `docs/reference/elsa-domain-tree.md:6,9,41`
  - `docs/reference/framework-examples.md:32-35,170`
  - `docs/reference/first-user-prompts.md:3,16,67`
  - `docs/skills/catalog.md:8,133,179,340,366-370`
  - `docs/README.md:42`
  - `docs/reports/README.md:30-31`
- [ ] T022 [P] [US2] Replace any "inspect `docs/maps/...`" or "requires docs/maps" wording in these skills with "generate locally (`-- all`) or read the `generated-maps` branch", and restate `elsa-refresh-generated-maps` as generating locally, with no staging step:
  - `.claude/skills/elsa-refresh-generated-maps/SKILL.md`
  - `.claude/skills/elsa-extension-point-catalog/SKILL.md:5,22,25`
  - `.claude/skills/elsa-feature-dependency-map/SKILL.md:5,22,23,28`
  - `.claude/skills/elsa-feature-composition/SKILL.md:5,22,24`
  - `.claude/skills/elsa-verify-codebase/SKILL.md:3,5,23`
  - `.claude/skills/elsa-quality-fix/SKILL.md:26`
  - `.claude/skills/elsa-critical-constitution-review/SKILL.md:24`
- [ ] T023 [US2] Update the SHA256 of every wrapper edited in T008 and T022 in `.specify/integrations/claude.manifest.json`, then run `pwsh tools/skills/validate-claude-wrapper-drift.ps1` and make it pass.
- [ ] T024 [US2] Constitution PATCH 6.0.2 → 6.0.3 in `.specify/memory/constitution.md`:
  - re-point the §E2.1 domain-map link (`:47`) to the `generated-maps` browse URL of `docs/maps/domain-map.md`;
  - add a SYNC IMPACT REPORT entry ("PATCH: link only, no rule changed; the domain map is published, not committed (spec 257)");
  - bump the version and Last Amended to the delivery date;
  - leave the approval line for the owner to fill.

**Checkpoint**: US1 + US2 deliver the full owner ask except fast CI.

---

## Phase 5: User Story 3 — Documentation-only PRs finish fast (P2)

**Goal**: a PR touching only docs, specs or agent files gets both required checks green in minutes, with the doc-reading checks still enforced.

**Independent test**: quickstart CI steps 2 and 3. A throwaway docs-only PR is fast, its summary says "documentation-only", and `Architecture guards` and `Core-only` are skipped. A throwaway mixed PR gets the full run.

- [ ] T025 [US3] Add `tools/ci/change_scope.py` per contracts/cli-and-ci.md:
  - `--base`, `--head`, `--github-output`, `--summary`;
  - the include and exclude sets from research R2;
  - `docs_only=false` on any git error or empty diff;
  - always exit 0 in classify mode.

  `--self-test` must cover at least these cases:
  - `docs/x.md` → true
  - root `README.md` → true
  - root `EXTENSION_POINTS.md` → false
  - `docs/maps/dependency-map.json` → false
  - `specs/220-domain-owned-apis/contracts/management-api.openapi.yaml` → false
  - `.claude/x/.editorconfig` → false
  - `docs/Directory.Build.props` → false
  - `Essentials/README.md` → false
  - `.github/workflows/ci.yml` → false
  - a mixed set → false
  - an empty diff → false
- [ ] T026 [US3] Verify that `tools/maps/check-catalog-links.py --all-readmes` checks the same files and link rules as `Essentials/tests/Architecture/Tests/CatalogLinkGuardTests.cs:21,41-57,82,131`: the root `EXTENSION_POINTS.md` plus every tracked `EXTENSION_POINTS.md` and `README.md` under `Essentials/` and `Extensions/`, resolved against `git ls-files`. Close any gap in the script, and record the equivalence in a header comment of the script.
- [ ] T027 [US3] Gate `build-and-test` in `.github/workflows/ci.yml`:
  - add a first step after checkout, `id: scope`, that runs `python3 tools/ci/change_scope.py --self-test`, then classifies with `--base ${{ github.event.pull_request.base.sha }} --head ${{ github.sha }}` on `pull_request` only, and emits `docs_only=false` on every other event;
  - expose `docs_only` as a job output;
  - keep steps 2–4 (the Python guards, including the pinned `test_workflow_guards.py` line) unconditional;
  - add `if: steps.scope.outputs.docs_only != 'true'` to setup-dotnet, cache, filter generation, restore, tool restore, build and test;
  - add an unconditional `python3 tools/maps/check-catalog-links.py --all-readmes` step;
  - update the header comment (`:1-53`) to describe the fast path and why push, schedule and dispatch stay full.
- [ ] T028 [US3] In `.github/workflows/ci.yml`, add `if: needs.build-and-test.outputs.docs_only != 'true'` to `architecture-guards` (`:342`) and `core-only` (`:384`). Confirm the alert job (`:418-435`) stays quiet when they are skipped, since skipped is not failure.
- [ ] T029 [US3] Run `python3 tools/contributor-checkpoints/test_workflow_guards.py` locally and keep it passing. Adjust its expectations only where they pin text this unit intentionally changed.

**Checkpoint**: all three stories deliver.

---

## Phase 6: Polish & validation

- [ ] T030 Run [quickstart.md](quickstart.md) "Local" end to end on Windows (PowerShell shims) and in Bash, and confirm byte-identical output (FR-010).
- [ ] T031 Open the implementation PR. Confirm a full run with both required checks green. Then open and close the throwaway docs-only, mixed and two-concurrent-spec PRs from quickstart "CI", and record their run links and durations in the PR body (SC-003).
- [ ] T032 After merge, verify quickstart "After merge": the `generated-maps` branch, the tip trailer naming the merge commit, pinned links, the artifact, and the existing ruleset `generated-maps` (id 25472440) applying to the branch.
- [ ] T033 Set `**Status**: Implemented` in `specs/257-generated-artifacts-no-conflict/spec.md` in the implementation PR before it merges. No spec-status map regeneration is needed any more.

---

## Dependencies & execution order

- **Phase 1 → Phase 2**: T003–T005 are one commit.
- **US1 (T006–T010)** depends on Phase 2.
- **US2 (T011–T024)** depends on Phase 2:
  - T011 → T012 → T016;
  - T013 → T014 → T016;
  - T012 and T013 → T015;
  - the docs tasks T018–T022 can run in parallel;
  - T023 runs after T008 and T022.
- **US3 (T025–T029)** depends only on Phase 2 and is independent of US1 and US2:
  - T025 → T027 → T028 → T029;
  - T026 runs in parallel with T025.
- **Polish** comes after all stories.
- **`ci.yml`**: T006, T015, T016, T027 and T028 all edit it. Run them sequentially, in ID order.

## Parallel examples

- **US1:** T007, T008, T009 and T010 touch disjoint files.
- **US2:** T017, T020, T021 and T022 run in parallel once T018 and T019 settle the wording. T011/T012 (C#) and T013/T014 (Bash) are two independent tracks.
- **US3:** T025 and T026 run in parallel.

## Implementation strategy

1. **MVP:** Phase 2 + US1 removes the conflicts on its own.
2. **Then US2**, so maps stay findable. Ship it in the same PR as the MVP, so there is no window without a published map set.
3. **Then US3**, the fast path. It can be split into a follow-up PR if review of the `ci.yml` gating wants its own diff.
