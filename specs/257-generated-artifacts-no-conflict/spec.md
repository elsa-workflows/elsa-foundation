# Feature Specification: Generated artifacts never block or conflict

**Feature Branch**: `agent/claude/257-generated-artifacts-no-conflict`
**Created**: 2026-10-20
**Status**: Draft
**Input**: Owner request (2026-10-20): "We can never merge more than one PR to main before the maps must be refreshed, this triggers again a very long CI. [...] CI should not be blocked when there is not actual code implementation changed." Owner decisions recorded below under Decisions.

## Evidence and Context

- In the 30 days to 2026-10-20, 270 of 518 commits on main touched `docs/maps/`. The most-changed files are `test-map.md` (189), `manifest.json` (123, whose `counts` block changes whenever a project or spec is added), `spec-status-map.md` (113, one row per spec), `docs/reports/maps-v1-findings.md` (83) and the domain, project-reference and package maps (72–89 each).
- Two PRs that each add a spec or a project edit the same `manifest.json` counts line and adjacent `spec-status-map.md` rows, so the second one conflicts in git.
- `Generated maps fresh` is a required status check in ruleset `main-2`, next to `Build & test`. A PR that merged without regenerating, or with a conflict resolved badly, leaves main stale. The next unrelated PR then goes red. Its refresh PR runs the full `ci.yml` (container-free suites, native-hosting matrix, core-only build) even though no code changed.
- What the generated files are used for:
  - **Read by code:** `docs/maps/dependency-map.json` is read from each commit's git tree by `tools/versioning/Elsa.Versioning.Calculator` (`CommitSnapshot`) to resolve package ownership and versions. The `Elsa.Server.*.slnf` solution filters are used by CI and contributors.
  - **Read by people and agents:** the markdown maps, `manifest.json`, `maps-v1-findings.md` and `maps-v2-findings.md` are navigation surfaces only. No build, test, publish or release step reads them.
- The companion change [#3114](https://github.com/elsa-workflows/elsa-foundation/pull/3114) stops tracking `.specify/feature.json`, which caused the same kind of conflict.

## Decisions

1. The documentation maps, `manifest.json` and the two generated findings reports leave `main`. CI regenerates them after every push to main and publishes them two ways: as a build artifact of that run, and as a commit on a CI-maintained `generated-maps` branch that no PR writes to. `publish-state` is the precedent for a branch the workflow token writes. Attaching them to a release is done by the release process when it needs them.
6. The maps must stay findable for external AI agents and people who only browse the repository on GitHub, without CI access or the .NET SDK. A build artifact alone does not do that: it needs authentication and expires. The `generated-maps` branch gives stable browse and raw URLs, and `main` links to it.
2. `docs/maps/dependency-map.json` stays committed in this unit, and so does its freshness check. Removing it requires the versioning calculator to derive the dataset from each commit's tree; that is a separate spec.
3. The `Elsa.Server.*.slnf` solution filters stay committed and checked.
4. The required check names `Generated maps fresh` and `Build & test` stay unchanged.
5. A PR that changes only documentation, specs or agent-instruction files does not run the full build and test matrix. Pushes to main, the nightly schedule and manual runs stay full.

## User Scenarios & Testing

### User Story 1 - Concurrent spec and code PRs merge without generated-file conflicts (Priority: P1)

A contributor or agent opens a PR that adds a spec or a project while other PRs are in flight. After another PR merges, theirs still merges cleanly, and no generated documentation file has to be refreshed or resolved first.

**Why this priority**: This is the reported pain. Today almost every pair of concurrent PRs conflicts on generated files and needs a refresh round-trip.

**Independent Test**: Create two branches from the same main commit. Each adds a new spec directory and one new test project. Merge the first and confirm the second merges cleanly and its required checks pass without regenerating anything.

**Acceptance Scenarios**:

1. **Given** two PRs from the same base that each add a spec, **when** the first merges, **then** the second shows no conflict caused by a generated documentation file.
2. **Given** a PR that adds a test project, **when** it is opened, **then** it does not need to change any generated documentation file to pass its required checks.
3. **Given** main after any merge, **when** the scheduled or push run evaluates freshness, **then** no documentation map can be reported stale, because none is committed.

---

### User Story 2 - Agents and people still get current maps on demand (Priority: P1)

An agent or architect who needs the project, test, spec-status, domain, extension-point or findings maps can get a current set. They either generate it locally with one documented command or download the set CI published for a main commit.

**Why this priority**: The maps are the navigation surface that skills and `AGENTS.md` point at. Removing them without a replacement would degrade agent work.

**Independent Test**: In a clean clone, run the documented command and confirm every removed map and report is produced at its usual path and is ignored by git. Separately, after a merge to main, confirm the `generated-maps` branch holds the same set for that commit, reachable through browse and raw URLs that `docs/maps/README.md` on main links to, and that the run's build artifact holds it too.

**Acceptance Scenarios**:

1. **Given** a clean clone, **when** the contributor runs the documented generation command, **then** every map and findings report is written at its documented path and `git status` shows no changes.
2. **Given** a completed CI run on main, **when** someone opens its artifacts, **then** a maps artifact for that commit is available and names the commit it describes.
3. **Given** an external AI agent that reads the repository only through GitHub's web or raw URLs, with no CI access and no SDK, **when** it starts from `AGENTS.md` or `docs/maps/README.md` on `main`, **then** it reaches the current map set by following one link.
4. **Given** a merge to main, **when** its CI run completes, **then** the `generated-maps` branch has a new commit that describes exactly that main commit and names it.
5. **Given** any skill, `AGENTS.md` section or contributing doc that tells the reader to consult a map, **when** it is read, **then** it says how to generate or download the map instead of implying the map is committed.

---

### User Story 3 - Documentation-only PRs finish fast (Priority: P2)

A contributor opens a PR that only changes documentation, specs or agent-instruction files, such as a spec status edit or a skill update. Its required checks complete in minutes instead of running the full build and test matrix. The checks that read those documents still run.

**Why this priority**: This is the owner's second point ("CI should not be blocked when there is not actual code implementation changed"). Once Story 1 removes most map-refresh PRs, the remaining docs and spec PRs are the main source of avoidable long CI.

**Independent Test**: Open a PR that edits only a spec `Status:` line and a skill file. Confirm both required checks report success without the full matrix running, and that the documentation-reading checks ran. Open a PR that also edits one source file and confirm the full matrix runs.

**Acceptance Scenarios**:

1. **Given** a PR whose every changed path is documentation, specs or agent-instruction files, **when** CI runs, **then** `Build & test` and `Generated maps fresh` both report success, and the heavy suites are skipped.
2. **Given** that same PR, **when** CI runs, **then** every test or check that reads documentation, specs or agent files still runs and can fail the PR.
3. **Given** a PR that changes at least one path outside that set, **when** CI runs, **then** the full matrix runs exactly as before.
4. **Given** a push to main, a scheduled run or a manual run, **when** CI runs, **then** it is a full run whatever the changed paths are.

### Edge Cases

- A PR changes a `.md` file that is a build or test input, such as `AnalyzerReleases.Unshipped.md` or a packed `README.md`. It is not documentation-only and gets the full run.
- A PR changes only the map generator or its tests. It is not documentation-only, and the generator self-tests run.
- A PR changes the project graph. `dependency-map.json` must still be regenerated and committed in that PR, and its freshness check still blocks; that is unchanged by this unit.
- A contributor's local clone still has the old committed maps after pulling. Pulling deletes the tracked copies. Regenerating writes them back, now ignored.
- A doc or skill links to a removed map by relative path. The link is changed to point at the generation instructions, or labelled as generated locally, so the docs link checks don't fail.
- The CI artifact expires under the retention policy. The `generated-maps` branch keeps a commit per main commit, and anyone can also reproduce the set for any commit by checking it out and generating.
- Two pushes to main land close together. Branch updates happen in main-commit order, an older run never overwrites a newer set, and a cancelled run leaves the branch at the last good set.
- The branch update fails, for example on a permission or network error. Main stays green; the failure is reported like other post-merge publication failures, and the next main push catches the branch up.
- Main sees no merges for longer than the hygiene sweep's idle threshold (14 days), so `generated-maps` looks like an idle branch with no open PR. The sweep skips it because it is explicitly exempt. The stale-PR workflow and delete-branch-on-merge only remove PR head branches, which `generated-maps` never is.
- Someone opens a PR against `generated-maps`, or pushes to it by hand. The branch is protected so that only the workflow writes to it.
- `tools/consolidation/foundation-path-map.json` lists the removed map paths. Whatever reads it must not fail because those files are now absent.

## Requirements

### Functional Requirements

- **FR-001**: The repository MUST NOT track the generated markdown maps under `docs/maps/` (every generated `*.md` except `README.md`), `docs/maps/manifest.json`, `docs/reports/maps-v1-findings.md` or `docs/reports/maps-v2-findings.md`. Git MUST ignore them.
- **FR-002**: The repository MUST keep tracking `docs/maps/dependency-map.json` and the `Elsa.Server.*.slnf` solution filters, and their existing freshness checks MUST keep blocking.
- **FR-003**: One documented command MUST generate the full removed set locally at its usual paths, without changing any tracked file in a fresh checkout.
- **FR-004**: Every CI run on main MUST generate the full map set and publish it as a build artifact identified by the commit it describes.
- **FR-004a**: Every successful CI run on main MUST also commit that set to the `generated-maps` branch, in main-commit order, with the commit message naming the main commit it describes. The branch's file layout MUST match the paths the maps had on main, so existing relative links inside the maps still resolve.
- **FR-004b**: Only the CI workflow MUST be able to write `generated-maps`. A ruleset MUST prevent deleting it and non-fast-forward updates to it, as for `publish-state`.
- **FR-004d**: The weekly repository-hygiene branch sweep MUST never delete `generated-maps`. The branch MUST be named in the sweep's explicit exempt list, beside `main` and `publish-state`, and not exempted only through the ruleset. `docs/contributing/repository-hygiene.md` MUST list it under "Never swept".
- **FR-004c**: `docs/maps/README.md` and `AGENTS.md` on `main` MUST link to the `generated-maps` branch's browse URL and describe the raw-URL and `git fetch origin generated-maps` routes, so an agent without CI access or the SDK finds the maps from main in one step.
- **FR-005**: The required `Generated maps fresh` check MUST keep its name and keep its generator self-tests (project facts, feature facts, solution filters, EF suite manifest). It MUST stop comparing the removed files.
- **FR-006**: A PR whose changed paths all fall in the documentation-only set MUST get success from both required checks without running the full build and test matrix. The set is documentation, specs, agent instructions and skills, minus any path that is a build or test input.
- **FR-007**: The documentation-only path MUST still run every check and test that reads documentation, specs or agent-instruction files.
- **FR-008**: Any PR that changes a path outside the documentation-only set, and every push-to-main, scheduled and manual run, MUST run the full matrix unchanged.
- **FR-009**: `AGENTS.md`, `docs/maps/README.md`, `docs/contributing/generated-maps.md`, `docs/reference/spec-lifecycle.md`, `docs/reports/README.md`, `docs/skills/catalog.md` and every skill that points at a removed map MUST describe the generate-locally or download-from-CI route. The rule that maps are committed snapshots MUST be retired.
- **FR-010**: The generator MUST keep producing identical output for the same tree, so a locally generated set and the CI artifact for the same commit match byte for byte.

### Key Entities

- **Documentation map set**: the generated maps, manifest and findings reports for one commit. They are for people and agents only and never committed to `main`. Each main commit's set is published as a CI artifact and as a commit on `generated-maps`.
- **`generated-maps` branch**: a publication branch only CI writes. It is the stable, public, always-current home of the documentation map set.
- **Committed generated inputs**: `dependency-map.json` and the solution filters. They are generated but read by tooling, so they stay committed and checked.
- **Documentation-only change set**: the path classes whose changes alone justify the fast CI path. Build and test inputs are excluded even when they are markdown.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Over the first 30 days after delivery, no merge conflict on main is caused by a generated documentation file. The baseline is the current near-every-pair rate.
- **SC-002**: Over the same window, PRs whose only purpose is refreshing documentation maps drop to zero.
- **SC-003**: A documentation-only PR's required checks complete within 10 minutes, against the full matrix today.
- **SC-004**: A contributor or agent can get a current map set within one minute: one command locally, one link from `main`, or one download from the latest main run.
- **SC-004a**: Within 30 minutes of any merge to main, the `generated-maps` branch describes that merge commit.
- **SC-005**: No regression escapes to main through the fast path. Every test that reads documentation still runs on documentation-only PRs.

## Assumptions

- Nothing in build, test, packaging, publishing or release reads the removed files. Verified on 2026-10-20 except for `tools/consolidation/foundation-path-map.json`, which planning must confirm.
- Attaching the map set to a GitHub Release is left to the release process. GitHub Releases publish nothing until the 4.0 release cut (#2085), so this unit adds no release step.
- The workflow token may push to a non-`main` branch, as `packages.yml` already does for `publish-state`. The org Actions allow-list is not affected, because no third-party action is needed.
- Ruleset `main-2` keeps `strict_required_status_checks_policy: false`. No ruleset change is needed because both required check names stay.
- Build artifacts use the repository's default retention. Older sets can be reproduced from any commit.

## Out of Scope

- Deriving `dependency-map.json` inside the versioning calculator and removing it from the repository. That needs its own spec.
- Changing what the maps contain or how the generator computes them.
- Changing the `.specify/feature.json` handling, which is delivered by #3114.
