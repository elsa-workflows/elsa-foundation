# Feature Specification: Tiered CI with parallel test shards

**Feature Branch**: `agent/claude/263-ci-tiering-merge-queue`
**Created**: 2026-10-21
**Revised**: 2026-10-22, after review by Sipke Schoorstra and CodeRabbit on #3179 (see [Revision history](#revision-history))
**Status**: Draft
**Input**: Joey Barten: "Whenever a merge arrived before my PR is merged, GitHub tells me to update this branch. [...] We are waiting a long time on the CI build and test runs. [...] Tests should much more run in parallel. [...] Tests should only run when code has changed, not documentation. [...] The regression/integration tests should likely run right before the merge completes, and could block the merge. But perhaps these more expensive regression tests should not run with every commit made during the PR." The objective, as confirmed on 2026-10-22, is **shorter waiting time**: from a push to a result, and from "ready" to "merged". The machine time of individual tests is not the objective.

## Evidence and Context

All findings were verified against `main` and the GitHub API on 2026-10-21 and 2026-10-22.

- **"Update branch" is not required.** The `main-2` ruleset requires the `Build & test` and `Generated maps fresh` checks with `strict_required_status_checks_policy: false`. A PR does not need to be up to date with `main` to merge; GitHub creates the merge commit against the current `main` when merging.
- **What actually blocked PR #3173.**
  - Head `50a39198e` was pushed at 11:19Z. `Build & test` ran 49 minutes and failed at about minute 45 on one flaky test (`WorkbenchCompensationSampleTests.The_sample_workflows_end_as_the_readme_says`, timed out after 2 m 31 s).
  - A re-run started at 12:18Z. At 12:29Z the author pressed "Update branch", which cancelled the re-run and started a full run from zero.
  - The two commits it brought in (#3123, #3174) merged cleanly with the PR. The update cost a full cycle and bought nothing.
- **Where the time goes.**
  - Recent successful PR runs of `CI` take 37-57 minutes. In `Build & test`, restore takes about 40 s, the full build about 3 minutes, and the container-free test step 30-40 minutes, on one 4-core runner. The test projects' durations add up to 99 minutes.
  - The long pole is `Elsa.Workbench.Tests`: 28-31 minutes for 62 tests in 17 test classes. Four of those classes share one non-parallel xUnit collection and two share another (corrected during planning; see research R6).
  - Next are `Elsa.Cluster.EntityFrameworkCore.Tests` (16 min; it starts child host processes), `Elsa.Activities.Compensation.Tests` (9), `Elsa.Activities.Http.IntegrationTests` (8), `Elsa.Cli.Tests` (6), `Elsa.Architecture.Tests` (4.5), `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests` (4), `Elsa.Secrets.Workflows.Tests` (4) and `Elsa3.Activities.Design.Import.Integration.Tests` (3).
  - The job's budget was raised from 30 to 45 minutes (#3056) and on 2026-10-21 to 60 minutes.
- **Flaky tests are the largest measured waste.**
  - Failed `Build & test` runs since 2026-10-15 fail intermittently on at least five different Workbench native-hosting tests (`NativeHostingConformanceTests`, `NativeHostingGracefulHandoffTests`, `WorkbenchBackgroundActivityRecoveryTests`), on `Http.IntegrationTests.ResponseReplaySafetyProcessTests`, and on the sample test above.
  - The open `main is red: CI` issue (#2293) records a Workbench failure that passed on the second attempt.
  - Since 2026-10-08, CI runs on pushes to `main`: 50 green, 3 red, 46 cancelled because a newer push superseded them.
- **Merge rate.** `main` took 22, 26 and 24 merges on 2026-10-19, 20 and 21 (35 on 2026-10-18).
- **Documentation fast path (spec 257).** `tools/ci/change_scope.py` classifies the whole PR diff against its base. A PR whose every changed path is documentation skips the .NET build and tests.
- **Container suites.** The EF container suites (`tools/ci/ef-suites.json`, 23 legs plus the Secrets EF composition proof) run nightly and on manual dispatch only, by owner decision (#2632). Each leg takes 1-5 minutes.
- **Test hosts.** 22 test projects use an in-memory ASP.NET Core test server (`Microsoft.AspNetCore.Mvc.Testing` or `TestHost`). That server does not make a project slow by itself.
- **Studio CI.**
  - `.github/workflows/studio-ci.yml` runs on every pull request and push to `main` with no path filter, and its Chromium browser tests take about 15 minutes. None of its checks is required.
  - No CI job today fails when a backend endpoint changes shape. That gap is tracked separately in #3177.
- **Runner capacity.** The repository is public, so standard GitHub-hosted runners cost nothing. The organization is on the Free plan: at most 20 concurrent jobs across the organization.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fast feedback on every PR commit (Priority: P1)

An author (human or agent) pushes a commit to a pull request and learns within minutes whether it builds and whether the PR-tier tests pass, instead of waiting most of an hour.

**Why this priority**: It is where the waiting goes today, and it needs no change to how PRs are merged.

**Independent Test**: Push a code change to a PR and measure the time from push until the required `Build & test` check reports.

**Acceptance Scenarios**:

1. **Given** a PR that changes code, **When** a commit is pushed, **Then** the PR-tier test projects run in parallel on several runners, and `Build & test` reports one aggregate result under its existing name.
2. **Given** a test project whose expected duration exceeds the shard target (today `Elsa.Workbench.Tests` and `Elsa.Cluster.EntityFrameworkCore.Tests`), **When** the PR tier runs it, **Then** its test classes are spread across shards, balanced by measured duration, and the classes that share an xUnit collection stay on one shard.
3. **Given** a test fails once and passes on a single automatic retry, **When** the run finishes, **Then** it reports success and the run summary names the retried test.
4. **Given** a test fails twice, **When** the run finishes, **Then** `Build & test` fails and names the test.

---

### User Story 2 - Expensive tests gate the merge, not every WIP commit (Priority: P1)

While a PR is a draft, only the PR tier runs. Once it is marked ready for review, the full tier also runs on every push, and the PR cannot merge until it is green on the exact head commit. A draft author who wants the full tier early can ask for it.

**Why this priority**: It is the author's original intent: the integration-tier projects and the EF container suites block a broken merge (the EF container suites block nothing today), without running on every work-in-progress commit.

**Independent Test**: Push commits to a draft PR and confirm only the PR tier runs. Mark it ready and confirm the full tier runs and is required. Push again and confirm it runs again on the new head.

**Acceptance Scenarios**:

1. **Given** a draft PR, **When** a commit is pushed, **Then** the PR tier runs, the full tier does not, and no check named `Full tier` is reported on that head.
2. **Given** a PR is opened as ready for review, reopened while not a draft, marked ready for review, or receives a commit while it is not a draft, **When** CI runs, **Then** the full tier runs on that head and reports a required check.
3. **Given** a ready PR whose full tier failed or has not finished, including a PR just marked ready whose full-tier run has not started yet, **When** someone tries to merge it, **Then** GitHub blocks the merge.
4. **Given** a draft PR, **When** its author starts the workflow manually on the branch with the full tier selected, **Then** the full tier runs on that head without the PR leaving draft.
5. **Given** a PR whose whole diff is documentation, **When** it is ready for review, **Then** the full tier follows spec 257's fast path and reports success without building or testing.
6. **Given** a push to `main` or the nightly schedule, **When** CI runs, **Then** the full set runs as it does today, and a red result files or updates the `main is red` issue.

---

### User Story 3 - Flaky tests stop costing merges (Priority: P1)

A known flaky test no longer forces a full re-run or blocks a merge. Every flake is visible and tracked until it is fixed.

**Why this priority**: Flakes are the largest measured waste (see Evidence). Faster shards alone would make them fail faster, not less often.

**Independent Test**: Quarantine a deliberately flaky test and confirm a run where it fails still reports success, names it in the summary, and links its issue. Remove it from quarantine and confirm a double failure fails the run.

**Acceptance Scenarios**:

1. **Given** a test on the quarantine list, **When** it fails, **Then** the run still succeeds, and the summary names the test and links its tracking issue.
2. **Given** a test that is not quarantined and passes only on its retry, **When** the run finishes, **Then** the retry is recorded in one flake-tracking issue, so repeat offenders become visible.
3. **Given** the flaky tests listed under Evidence, **When** this story is delivered, **Then** each is either fixed or quarantined with a tracking issue.
4. **Given** a PR that adds or changes a quarantine entry, **When** the entry's tracking issue is missing or closed, **Then** that PR's `Build & test` fails and names the entry.
5. **Given** a quarantine entry already on `main`, **When** its tracking issue is closed, **Then** the next push run on `main` or the nightly run fails, the `main is red` issue names the entry, and open PRs are not affected.

---

### User Story 4 - Every test project has a known tier and stays in it (Priority: P2)

A coding agent adding or changing tests knows which tier each test project belongs to, assigns any new project to a tier, and cannot silently put process-hosting or container tests in the PR tier.

**Why this priority**: The tiering is only as good as its assignment. Without guards, drift would slow the PR tier again or leave new projects running nowhere.

**Independent Test**: Add a new test project without assigning it, add a Testcontainers reference to a PR-tier project, and add a 20-second test to a PR-tier project. The first two must fail CI with a message that says what to do; the third must be reported.

**Acceptance Scenarios**:

1. **Given** a test project that the tier manifest does not name, **When** CI runs, **Then** it fails and names the project, the manifest and the two tiers.
2. **Given** a PR-tier project that references Testcontainers or starts a child process (a Workbench host, a child host process, the CLI process), directly or through a referenced test project, **When** CI runs, **Then** it fails and says the project belongs in the integration tier.
3. **Given** a PR-tier test that takes longer than 10 seconds and is not on the allow-list, **When** its shard finishes, **Then** the summary reports it. This is a report, not a failure, until the switch described in FR-020.
4. **Given** an agent working on tests, **When** it follows the test skill or the contributor documentation, **Then** both state the tier rule and point at the manifest.

---

### User Story 5 - Agents and humans know the draft rule and how to run the full tier early (Priority: P3)

Any author, including a Claude or Codex session, knows to open a PR as a draft while working and mark it ready when done, and can find one documented way to run the full tier early.

**Why this priority**: Story 2 only saves waiting if PRs are drafts while work is in progress.

**Independent Test**: Follow each PR-driving skill from "open PR" to "request review" and confirm each opens a draft, marks it ready before requesting review, and links the documented section.

**Acceptance Scenarios**:

1. **Given** the contributor documentation, **When** an author looks for how CI tiers work, **Then** one section explains the two tiers, the draft rule, and the exact command for the manual run.
2. **Given** the skills that open or drive a PR (`elsa-auto-review-merge`, `elsa-quality-fix`, `speckit-implement`'s finishing step) and the test skill (`elsa-add-unit-tests`), **When** the guard that checks them runs, **Then** each links to that section; removing a link fails CI.
3. **Given** the manual-run input changes, **When** the workflow's self-test runs, **Then** it fails until the documentation names the same values.

---

### User Story 6 - Studio CI runs only when Studio changes (Priority: P3)

A backend-only PR does not start Studio's lint, unit, browser and .NET jobs; a PR that touches Studio does.

**Why this priority**: Those jobs give no protection on backend-only PRs (see Evidence) and take concurrent job slots from the 20-job organization limit.

**Independent Test**: Open a backend-only PR and confirm Studio CI does not run; open a Studio PR and confirm it does.

**Acceptance Scenarios**:

1. **Given** a PR that changes no Studio path, **When** it is opened or updated, **Then** Studio CI does not run.
2. **Given** a PR that changes a Studio path, shared build configuration or Studio CI tooling, **When** it is opened or updated, **Then** Studio CI runs as it does today.
3. **Given** a push to `main`, **When** CI is triggered, **Then** Studio CI runs as it does today.

---

### Edge Cases

- **A draft is merged.** GitHub does not merge drafts, so the full tier always runs before a merge.
- **Ready, then back to draft.** Converting back to draft stops the full tier from running on later commits. The required check must report again before merge once the PR is ready.
- **Marked ready, then merged at once.** Between marking a PR ready and the start of the full-tier run, which can take a long time when runners queue, the head must not be mergeable. Because a draft head carries no `Full tier` check (FR-006), the required check is missing there, so GitHub blocks the merge until a real run reports.
- **A quarantine issue is closed outside a PR.** Closing a tracking issue, by hand or through a `Fixes #n` merge that leaves the entry in place, must not turn every open PR red. Only `main` and nightly runs check the whole list (FR-013).
- **Combination breaks.** Two PRs that pass on their own can break `main` together (#543). The full CI run on every push to `main` and the red-main alert catch that after the fact. The merge queue that would prevent it is deferred; see [Alternatives considered](#alternatives-considered).
- **Admin bypass.** Admins can still bypass required checks. The push run on `main` covers that.
- **Cancelled runs.** A run cancelled by a newer push is not a failure and must not file a `main is red` issue.
- **Job slot exhaustion.** Under the 20-job organization limit, runs wait for free slots. The budgets in FR-022 keep a PR-tier run and a full-tier run able to proceed at the same time.
- **Manifest change.** A PR that changes the tier manifest, the shard grouping or the quarantine list runs both tiers.
- **Quarantine abuse.** Every quarantine entry needs a tracking issue, and the list itself is in the guarded manifest.

## Requirements *(mandatory)*

### Functional Requirements

**PR tier (Story 1)**

- **FR-001**: On every pull request commit, CI MUST run the PR-tier test projects in parallel across several runners.
- **FR-002**: A test project whose expected duration exceeds the shard target MUST be split across shards by test class, balanced by measured class durations. Classes that share an xUnit collection MUST stay on one shard.
- **FR-003**: The required `Build & test` check MUST keep its name, keep spec 257's documentation fast path, and report one aggregate result that is success only when the build and every PR-tier shard succeeded.
- **FR-004**: A failing test MUST be re-run once and only once, in both tiers. A test that passes on retry MUST be listed in the run summary and recorded in the flake-tracking issue.

**Full tier (Story 2)**

- **FR-005**: The full tier MUST contain every integration-tier test project, every EF container suite, and the Secrets EF composition proof.
- **FR-006**: The full tier MUST run on a pull request whenever it becomes ready: when it is opened as ready for review, reopened while not a draft, or marked ready for review. It MUST also run on every push while the PR is not a draft, so the required check never waits for another push. It MUST NOT run on draft commits unless asked for (FR-008). A head on which the full tier did not run MUST NOT carry a check under the required name, not even a successful placeholder, so that the head is not mergeable until a real run reports. GitHub counts a skipped or placeholder success as passing.
- **FR-007**: The full tier MUST report a required status check, and the `main-2` ruleset MUST require it. Only a run that executed the full tier, or spec 257's documentation fast path on a PR that is not a draft (FR-009), MAY report under that name.
- **FR-008**: An author MUST be able to run the full tier on any branch, a draft's head included, through a manual workflow run that selects the full tier. There is no label for it.
- **FR-009**: A PR whose whole diff is documentation (spec 257) MUST get a full-tier result without building or testing.
- **FR-010**: The full CI run on every push to `main`, the nightly schedule, the maps publication and the red-main alert MUST stay as they are.
- **FR-011**: Integration-tier projects MUST NOT leave the PR tier until the full tier is a required check, so that no test stops gating merges in between.

**Flakes (Story 3)**

- **FR-012**: A committed quarantine list MUST name each quarantined test and its tracking issue. A quarantined test MUST still run, and its failure MUST be reported without failing the run.
- **FR-013**: A guard MUST check that every quarantine entry has an open tracking issue. On a pull request it MUST check only the entries that the PR's own diff adds or changes, so that issue state outside the PR never fails a PR. On pushes to `main` and the nightly run it MUST check the whole list, and a failure there MUST file or update the `main is red` issue.
- **FR-014**: Each flaky test listed under Evidence MUST be fixed or quarantined with a tracking issue before this spec is Implemented.

**Test tiers (Story 4)**

- **FR-015**: A committed tier manifest MUST assign every test project to exactly one tier, PR or integration. It MUST be the single list that both tiers and the shard jobs read.
- **FR-016**: CI MUST fail when a test project is missing from the manifest, or when the manifest names a project that does not exist.
- **FR-017**: CI MUST fail when a PR-tier project, directly or through a referenced test project, references Testcontainers, or starts a child host process (a `dotnet` launch of a host, worker, probe or the CLI) and has a median duration of 2 minutes or more. Launching `git` or `bash` is not a child host process. Any other project MUST be assigned by its measured duration.
- **FR-018**: A test project MUST be wholly one tier. Per-test traits MUST NOT be a supported way to assign a tier.
- **FR-019**: A PR-tier shard MUST report every test that takes longer than 10 seconds and is not on the manifest's allow-list.
- **FR-020**: The report of FR-019 MUST become a failure only after two weeks of reports, once the allow-list matches them, and only with the owner's approval.
- **FR-021**: The `elsa-add-unit-tests` skill and the constitution's testing section MUST state the tier rule and point at the manifest.

**Job slots**

- **FR-022**: Once FR-011's move has happened, a PR-tier run MUST use at most 5 test jobs, and a full-tier run at most 8 jobs, with the EF container suites combined into a few jobs. Before that, the PR tier MAY use up to 8 test jobs.

**Documentation and skills (Story 5)**

- **FR-023**: The contributor merging documentation MUST have one section that explains the two tiers, the draft rule, and the exact command for the manual run.
- **FR-024**: `elsa-auto-review-merge`, `elsa-quality-fix`, `speckit-implement`'s finishing step and `elsa-add-unit-tests` MUST link to that section. The PR-driving skills MUST open PRs as drafts and mark them ready before requesting review. A guard MUST fail CI when one of those links is missing.
- **FR-025**: A self-test MUST fail when the manual-run input the workflow uses differs from the one the documentation names.

**Studio CI (Story 6)**

- **FR-026**: Studio CI MUST run on pull requests only when a Studio path, a Studio extension path, shared build configuration or Studio CI tooling changes, and MUST keep running on every push to `main`.

### Key Entities

- **PR tier**: The tests that run on every PR commit: the build, the PR-tier test projects, the architecture guards and the core-only build and test. It reports the existing `Build & test` check.
- **Full tier**: The integration-tier projects and the EF container suites. It runs on ready PRs, on request, on `main` and nightly, and it reports its own required check.
- **Tier manifest**: The committed list of every test project with its tier, the shard grouping, the slow-test allow-list and the quarantine list.
- **Shard**: A group of test projects or test classes that one runner tests.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a code PR, the median time from push to `Build & test` reporting is at most 12 minutes, down from 37-57 minutes, measured over the first 20 PR runs after Story 1 ships. A median of at most 13 minutes is accepted as met, with a follow-up issue for a method-level split of the slowest class (plan, Risks). A median above 13 minutes is a miss.
- **SC-002**: The full tier reports within 25 minutes of a PR being marked ready.
- **SC-003**: No PR merges with a failing integration-tier project or EF container suite.
- **SC-004**: Over the 30 days after Story 3 ships, no PR needs a manual re-run because of a test on the quarantine list, and `main is red` issues caused by flaky tests fall from the September-October level.
- **SC-005**: Every test project is in exactly one tier, and each drift check (missing assignment, container or process traits in the PR tier, slow PR-tier test) has a test that proves it fires.
- **SC-006**: One PR-tier run and one full-tier run can run at the same time within the 20-job organization limit.
- **SC-007**: Backend-only PRs start no Studio CI jobs.

## Decisions

Settled with Joey Barten on 2026-10-21 and revised on 2026-10-22 after review.

1. **The objective is waiting time.** Parallelism shortens it. Refactoring tests to cut their machine time is out of scope.
2. **Shard by project, and by test class for long projects.** Sharding by project alone cannot get under the 30-minute `Workbench.Tests` floor.
3. **The full tier becomes required once a PR leaves draft.** That meets "expensive tests gate the merge, not every commit" without a merge queue: the draft state already means work in progress, and approval already sits on the exact head.
4. **One reported retry in both tiers, plus a quarantine list.** A retry that is reported keeps flakiness visible. At about 25 merges a day, blocking on known flakes costs more than it protects.
5. **Hard tier traits are Testcontainers, and a child host process in a project of 2 minutes or more.** Those are what make tests slow and flaky here. A fast project that starts a short-lived child host stays in the PR tier, so draft commits keep its feedback. An in-memory test server is not a trait, and is tiered by measured duration.
6. **The 10-second gate starts report-only.** Switching it to fail is a separate owner decision after two weeks of data.
7. **Studio CI is path-filtered.** The missing contract check between Studio and the backend is #3177.
8. **The push-to-`main` run stays full.** It is free, blocks nothing, and catches combination breaks and admin bypasses.

## Alternatives considered

- **GitHub merge queue (deferred).** The first draft made a merge queue the merge gate. Review and a second look at the data showed it does not serve the objective:
  - **It adds waiting.** Every merge waits for a queue run of about 20-25 minutes.
  - **It duplicates runs.** With Decision 3 in place, it would mostly re-run tests the head already passed.
  - **Its unique value is rare here.** What only a queue adds is testing the exact combination with the latest `main`. The push runs on `main` show such breaks are rare: the red-main issues since September are dominated by flakes.
  - **Its cost is high.** GitHub runs CI separately for every queued PR, so the 20-job cap is multiplied by the queue's build concurrency. The Code Review app and the standing self-merge flow would have to enqueue, which needs an org AGENTS.md amendment, and a plain REST merge is likely refused.

  **Revisit when** more than 2 combination breaks reach `main` in a month, as recorded by `main is red` issues whose cause is two PRs that passed separately.
- **Reusing a green result across documentation-only follow-up commits (cut).** With Story 1, a documentation follow-up waits about 12 minutes. Reuse would make a required check go green without running anything, which costs more trust than the time it saves. Documentation-only PRs keep spec 257's fast path.
- **Strict "require branches to be up to date" (rejected).** It would make the "Update branch" loop mandatory.
- **Build once and share the output with shards (left to planning).** Moving the build output between jobs may cost more than the 3-minute rebuild. Planning measures both.
- **Larger or self-hosted runners (rejected for now).** Standard runners are free for this public repository; parallelism is enough to reach the targets.

## Assumptions

- `Elsa.Workbench.Tests`, `Elsa.Cluster.EntityFrameworkCore.Tests` and `Elsa.Activities.Http.IntegrationTests` start child processes and are integration tier. Planning confirmed each other project's tier from its references and measured durations (research R5): `Elsa3.Activities.Design.Import.Integration.Tests` uses an in-memory test host and stays in the PR tier.
- Splitting a project by test class across runners needs no change to the tests. Classes that share state through an xUnit collection stay together.
- The 20-concurrent-job organization limit is shared with other repositories. The budgets in FR-022 are a starting point that planning checks against observed queueing.
- Agents can open PRs as drafts and mark them ready with the tools they already use.

## Out of Scope

- A GitHub merge queue (see Alternatives considered).
- Refactoring tests to make them individually faster, including sharing one Workbench host across tests.
- A contract or live test between Studio and the backend (#3177).
- Larger or self-hosted runners.

## Revision history

- **2026-10-21**: First draft, with a merge queue as the merge gate.
- **2026-10-22**: Revised after review on #3179. Each review point and its resolution:

| Review point | Resolution |
|---|---|
| Sipke 1: SC-001 unreachable while Workbench stays in the PR tier | Long projects are sharded by test class (FR-002). |
| Sipke 2, CodeRabbit (merge groups): the job cap and the queue topology | The merge queue is deferred. Job budgets are set in FR-022 and SC-006. |
| Sipke 3: known flakes would cause daily ejections | The flakes found were broader than the one named. A reported retry, a quarantine list and fixing the listed flakes became Story 3. |
| Sipke 4: integration failures surfacing only in the queue | The full tier runs on ready PRs and is required (Story 2). |
| Sipke 5: FR-018's traits too broad | Hard traits are Testcontainers and starting a child process; the rest is tiered by measured duration (FR-017). |
| Sipke 6: the 10-second gate as a new flake source | It starts report-only (FR-019, FR-020). |
| Sipke 7, CodeRabbit (FR-001/003 vs FR-014): Story 4 | Cut. |
| Sipke 8: "build once" in the spec | Left to planning, which measures it. |
| Sipke 8, CodeRabbit: `merge_group` triggers and enqueueing bots | Moot without a merge queue. |
| Sipke 8: checklist accuracy | Checklist corrected. |
| CodeRabbit: documentation-only entries in the queue | Moot. Documentation-only PRs keep the fast path in both tiers (FR-003, FR-009). |
| CodeRabbit: SC-004 vs the Workbench duration | Replaced by SC-002, which class sharding makes reachable. |
| Sipke, follow-up: the full tier must also run on a PR opened or reopened as ready | FR-006 and Story 2, scenario 2 cover opened, reopened and marked-ready PRs. |
| CodeRabbit: merge authority | Not changed. The org AGENTS.md has allowed Claude and Codex standing self-merge since 18 Oct 2026, and this spec no longer touches merge authority. |

- **2026-10-22 (planning)**: two factual corrections from research R5 and R6, with no change to any requirement. Workbench has 17 classes and two shared collections (four classes and two), not 13 classes and one collection of three; `Elsa3.Activities.Design.Import.Integration.Tests` starts no child process.
- **2026-10-22 (planning, Joey Barten)**: FR-017 and Decision 5 narrowed so the PR tier loses as little as possible. A child host process is a hard trait only in a project with a median of 2 minutes or more, and `git` or `bash` launches never are. `Elsa.Etl.Tests`, `Elsa.Secrets.Persistence.EntityFrameworkCore.Tests` and `Elsa.Workflows.Publishing.Tests` stay in the PR tier.
- **2026-10-22 (planning, Joey Barten)**: the `ci:full` label is dropped from FR-008, FR-023, FR-025 and Story 2 scenario 4, and the manual run is the one way to run the full tier early. This follows Sipke's review point 4 ("Agents won't add `ci:full`"): the label was never the safety net once the full tier became required on ready PRs. Keeping it would have cost a third workflow and a second check name (research R8).
- **2026-10-22 (Code Review round 1)**: each point and its resolution:

| Review point | Resolution |
|---|---|
| Blocker 1: the draft placeholder leaves a green `Full tier` on a head that becomes mergeable as soon as it is marked ready | A draft head no longer carries a `Full tier` check. The aggregate's job name is `Full tier (not run: draft)` when the full tier did not run, so the required context is missing and GitHub blocks the merge until the real run reports (FR-006, FR-007, Story 2 scenarios 1 and 3, edge case "Marked ready, then merged at once", research R8). Quickstart Story 2 step 3 proves it. |
| Blocker 2: `check-quarantine` in the required `Build & test` turns every PR red when an issue closes | PR runs check only the entries their diff adds or changes; `main` and nightly runs check the whole list and report through the `main is red` issue (FR-013, Story 3 scenarios 4 and 5, T028, T029, quickstart Story 3). |
| Low 1: review rounds will be most of the pushes made while ready | Stated in the plan's Risks, with marking ready after a clean review round as the first lever. That lever needs the review app to review drafts, so it is not adopted yet. |
| Low 2: the admin step must also update every merge path that names the required checks | Added to plan step 3 and T036. |
| Low 3: SC-001 is at risk with no defined fallback | SC-001 accepts a median up to 13 minutes, with a follow-up issue for a method-level split. |
