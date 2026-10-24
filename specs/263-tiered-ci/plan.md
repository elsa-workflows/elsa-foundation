# Implementation Plan: Tiered CI with parallel test shards

**Branch**: `agent/claude/263-ci-tiering-merge-queue` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/263-tiered-ci/spec.md`

## Summary

**The design.** One committed manifest, `tools/ci/test-tiers.json`, assigns every test project to the PR tier or the integration tier and records the shard grouping, the class splits, the slow-test allow-list and the quarantine list. It absorbs `ef-suites.json`.

- **PR tier.** `ci.yml`'s `Build & test` becomes the aggregate of a Python-only plan job and 3 parallel shards, plus the existing `Architecture guards` and `Core-only` jobs. Each shard rebuilds the solution and runs its projects or test-class groups.
- **Full tier.** A new workflow, `ci-full.yml`, reports a new required check, `Full tier`. It runs on PRs opened or reopened as ready, marked ready, or pushed while ready. It runs the integration-tier projects in 4 shards and the EF container suites in 3 combined jobs. On a draft the aggregate reports as `Full tier (not run: draft)`, so a draft head never carries the required context, and a PR just marked ready stays unmergeable until the real run reports (research R8). The jobs live in a reusable `full-tier.yml`, which `ci.yml` also calls for `main`, nightly and dispatch.
- **Retries.** One script retries a failing test once, honours the quarantine list and reports slow tests. Retried passes reach one flake-tracking issue through a `workflow_run` workflow, because fork PRs have read-only tokens.
- **Guards.** The generator gains `test-tiers-check` for FR-016 and FR-017. Studio CI gets path filters.
- **Order.** FR-011 is held by a manifest switch, `integrationLeavesPrTier`, which flips only after an admin makes `Full tier` required.

Measurements and decisions are in [research.md](research.md).

## Technical Context

- **Language/Version**: GitHub Actions YAML; Python 3 (`tools/ci/`, as `change_scope.py`); C# on .NET 10 (`tools/maps/Elsa.Maps.Generator`); Bash.
- **Primary Dependencies**:
  - GitHub-owned actions only (`checkout`, `setup-dotnet`, `cache`, `upload-artifact` and `download-artifact` @v4), which the org allow-list admits (`github_owned_allowed: true`);
  - the `gh` CLI preinstalled on runners;
  - xUnit 2.9.3 with `xunit.runner.visualstudio` 3.1.5 and `Microsoft.NET.Test.Sdk` 18.0.0, for TRX output, `--filter` and `--blame-hang-timeout`.

  No new packages and no third-party actions.
- **Storage**: committed JSON (`tools/ci/test-tiers.json`); run artifacts (`test-outcomes-<shard>`, 7 days); one standing GitHub issue labeled `ci-flake`.
- **Testing**:
  - `test_tiers.py --self-test` and `test_results.py --self-test`;
  - the generator's `test-tiers-self-test` and the retargeted `ef-suites-self-test`;
  - `tools/contributor-checkpoints/test_workflow_guards.py` (unchanged, must stay green);
  - the throwaway-PR scenarios in [quickstart.md](quickstart.md).
- **Target Platform**: GitHub-hosted `ubuntu-latest` (4 cores), under the organization's 20-concurrent-job limit, which is shared with elsa-core.
- **Project Type**: repository tooling and CI.
- **Performance Goals**:
  - SC-001: median push to `Build & test` ≤ 12 min. The estimate after the move is about 12–13 min plus queue wait, so the target is at risk. The bound is the class `CompensationNestingJourneyTests` at 7.1 min, which class sharding cannot split (R3, R9).
  - SC-002: `Full tier` ≤ 25 min after ready. The estimate is about 22 min, bounded by Workbench's native-hosting collection at 17.1 min serial; a retry in that shard can push it past 25 (R9);
  - SC-006: at most 5 PR-tier test jobs and at most 8 full-tier jobs.
- **Constraints**:
  - the context names `Build & test` and `Generated maps fresh` are unchanged;
  - spec 257's documentation fast path holds in both tiers;
  - no `labeled` trigger, no label for the full tier, and no reusable-workflow prefix on a required context (R8);
  - integration projects leave the PR tier only after `Full tier` is required (FR-011);
  - every failing test is retried exactly once;
  - test code is not refactored for speed (spec Out of Scope).
- **Scale/Scope**: 120 test projects (95 container-free, 25 Testcontainers) in 2 tiers; 3 PR shards and 7 full-tier test jobs (4 integration, 3 EF); 4 workflows new or changed plus `studio-ci.yml`; 2 Python scripts; 3 generator commands added and 2 retargeted; 1 doc section; 4 skills; 1 constitution MINOR.

## Constitution Check

*Both constitutions loaded, plus `docs/contributing/review-checklist.md`.*

| Gate | Applies? | Result |
|---|---|---|
| Framework §2.1–§2.3, §2.5, §2.9, §2.20, §2.23.1–§2.23.3 (product projects, features, persistence, visibility) | No product project, feature or package changes; test projects are not modified | pass (n/a) |
| Framework §2.21.1 (refactors keep behaviour) and §2.23.2 (tests do not assert on documentation text) | No test is changed or deleted. `check-docs` is a CI script, not a test asserting on markdown; it checks link presence the way `check-catalog-links.py` does. | pass |
| Framework §2.24.2 (sanctioned patterns) | No runtime pattern. The one-issue alert reuses `main-red-alert.yml`; the generator command follows `ef-suites-*` | pass |
| Framework §2.25.3 (subtraction is verified) | `ef-suites.json` is folded into the manifest, not dropped; `ef-suites-self-test` proves the coverage is unchanged. `Elsa.Architecture.Tests` leaves `Build & test`'s shards but keeps running in `Architecture guards`. | pass |
| Elsa §E2.2, §E2.3, §E2.7, §E5 | No project references change; versions are untouched | pass (n/a) |
| Elsa §E6 type naming | New generator types, for example `TestTierManifest` and `TestTierCheck`: at most 4 components, no banned words | pass, re-checked at review |
| Governance: constitution changes in the same unit; FR-021 | Elsa Gate index gains a "test tier" row (`test-tiers-check`, manifest). MINOR 6.1.0 → 6.2.0 with a Sync Impact Report; needs Joey Barten's or Sipke Schoorstra's approval in the PR. | planned, owner approval required |
| Repository hygiene: new issues are `needs-triage` only | The 5 new flake-tracking issues and the `ci-flake` issue are filed `needs-triage` and linked from the manifest | pass |

**Post-design re-check**: unchanged, with no violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/263-tiered-ci/
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
tools/ci/test-tiers.json                     # new: manifest (absorbs ef-suites.json)
tools/ci/ef-suites.json                      # removed, folded into test-tiers.json
tools/ci/test_tiers.py                       # new: select, check-docs, self-test
tools/ci/test_results.py                     # new: failures, retry-filter, evaluate, check-quarantine, self-test
tools/maps/Elsa.Maps.Generator/
├── Program.cs                               # test-tiers-check, test-tiers-self-test, rebalance dispatch
├── TestTierManifest.cs                      # new: manifest model and load
├── TestTierCheck.cs                         # new: coverage, traits, collections, shard coverage
├── TestTierRebalance.cs                     # new: TRX → medians and bin packing (maintainer tool)
└── EfSuite*.cs                              # retarget to containerSuites
.github/workflows/
├── ci.yml                                   # plan + PR shards + aggregate; dispatch input `tier`; calls full-tier.yml on push/schedule/dispatch
├── ci-full.yml                              # new: required `Full tier` on PR events
├── full-tier.yml                            # new: reusable integration and EF shards
├── ci-flake-record.yml                      # new: workflow_run → flake-tracking issue
└── studio-ci.yml                            # pull_request paths filter
docs/contributing/merging.md                 # new section "CI tiers"
docs/contributing/review-checklist.md        # tier rule row
.claude/skills/{elsa-auto-review-merge,elsa-quality-fix,speckit-implement,elsa-add-unit-tests}/SKILL.md
.specify/integrations/claude.manifest.json   # refreshed skill hashes
.specify/memory/constitution.md              # Gate index row, MINOR 6.2.0
specs/263-tiered-ci/spec.md                  # Assumptions/Evidence corrections (research R5, R6), status at finish
```

**Structure Decision**: everything lands in the existing tooling locations: workflows in `.github/workflows/`, CI scripts beside `change_scope.py`, and project-graph checks in the generator. There are no new projects.

## Delivery order

FR-011 fixes the order of steps 3 and 6. The other steps can overlap.

1. **Manifest and guards.**
   - Add `test-tiers.json` with every project, tier and trait from research R5, the splits and shards from R3, and the quarantine entries from R2.
   - Add `test-tiers-check` and `test-tiers-self-test`, and retarget `ef-suites-*`.
   - Add `test_tiers.py` and `test_results.py` with their self-tests.
   - File the 5 missing flake issues and the `ci-flake` issue, all `needs-triage`.
2. **Sharded PR tier with retry and quarantine** (Stories 1 and 3).
   - `ci.yml` gets `Plan tiers`, `PR shard (n)` and the `Build & test` aggregate. `integrationLeavesPrTier` is `false`, so the PR tier still runs every non-container project, in up to 8 test jobs.
   - Add `ci-flake-record.yml`.
3. **Full tier** (Story 2). Add `full-tier.yml` and `ci-full.yml`, and have `ci.yml` call `full-tier.yml` on push, schedule and dispatch. Then the **admin step**: add `Full tier` to ruleset `main-2`, and in the same step update every merge path that names the required checks (`merging.md`, the review checklist, `elsa-auto-review-merge`, `elsa-quality-fix`), so it waits for `Full tier` instead of trying a merge the ruleset refuses.
4. **Studio CI path filter** (Story 6). Independent; can go first.
5. **Documentation, skills and constitution** (Stories 4 and 5): the `merging.md#ci-tiers` section, the skill links, the draft rule in `elsa-quality-fix`, the review-checklist row, the constitution MINOR, the manifest hashes and `check-docs` in `Plan tiers`.
6. **The switch** (FR-011). After step 3's admin step, a PR sets `integrationLeavesPrTier: true`, and `select` verifies the ruleset. Then measure SC-001 over the next 20 code PRs, and SC-002 on each ready PR. Set `Status: Implemented` in the PR that finishes the measured outcomes.

Steps 1–5 can ship as one PR or several. Step 6 is its own PR by construction.

## Risks

- **Queue time under the shared 20-job limit (R1).** Waiting time includes the queue, which reached 2 h 20 min on 2026-10-22, driven mostly by elsa-core. Concurrency limits job count, but under saturation throughput is set by **job-minutes**.
  - **Today**: a backend PR push costs about 81 job-minutes, about 50 of `CI` and 31 of Studio CI.
  - **After this plan**, estimated from R3 and R4:
    - a draft push costs about 45 job-minutes: the PR tier, with no Studio CI;
    - a push while ready costs about 45 + 115 for the full tier. Today the EF container suites run only nightly; on ready PRs they become new load.
  - **Break-even**: total load only falls when fewer than about 30% of PR pushes happen while ready. That makes the draft rule (Story 5) load-bearing, not a convenience.
  - **Review rounds are likely to miss it from the start.** FR-024 has PR-driving skills mark a PR ready before requesting review, so every fix push in a review loop (up to 6 rounds) runs the full tier, about 115 job-minutes each. Most pushes made while ready will be review fixes.
    - The first lever is to mark a PR ready only after a clean review round. The review app skips drafts today, so that lever also needs a change to how review is requested. It is not adopted in this spec; the measurement after step 3 decides.
  - **Mitigations in this plan**:
    - EF legs are combined, so per-leg setup is paid once per job;
    - `Architecture.Tests` stops running twice;
    - backend PRs no longer start Studio CI;
    - shards rebuild rather than download a shared build, which R4 measured as cheaper in job-minutes too.
  - **Measure after step 3**: the share of PR pushes made while ready, and the queue wait. If total load rises, the next lever is fewer full-tier runs per PR, which needs an owner decision against Decision 3.
- **SC-001 sits on a class floor (R3, R9).** `CompensationNestingJourneyTests` takes 7.1 min alone, so the PR tier lands at about 12–13 min. SC-001 therefore accepts a median of up to 13 min, with a follow-up issue for a method-level split of that one class (which needs an FR-002 change). Above 13 min it is a miss, and the method-level split becomes this spec's work.

  Speeding up the test is out of scope.
- **SC-002 sits on a collection floor (R3).** Workbench's native-hosting collection runs 17.1 min serially. It also holds 4 of the 7 quarantine candidates, and their single retry can add up to about 4.5 min. Quarantine keeps the full tier green while they run and are reported, and #3147, #3252 and the new issues track the fixes.

## Complexity Tracking

None.
