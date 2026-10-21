# Feature Specification: Tiered CI with a merge queue

**Feature Branch**: `agent/claude/263-ci-tiering-merge-queue`
**Created**: 2026-10-21
**Status**: Draft
**Input**: Joey Barten: "Whenever a merge arrived before my PR is merged, GitHub tells me to update this branch. [...] We are waiting a long time on the CI build and test runs. [...] Tests should much more run in parallel. [...] Tests should only run when code has changed, not documentation. [...] The regression/integration tests should likely run right before the merge completes, and could block the merge. But perhaps these more expensive regression tests should not run with every commit made during the PR." The design below was settled in a grilling interview with Joey on 2026-10-21; its decisions are recorded under [Decisions](#decisions).

## Evidence and Context

All findings were verified against `main` and the GitHub API on 2026-10-21.

- **"Update branch" is not required.** The `main-2` ruleset requires the `Build & test` and `Generated maps fresh` checks with `strict_required_status_checks_policy: false`. A PR does not need to be up to date with `main` to merge; GitHub creates the merge commit against the current `main` when merging. No merge queue is configured. The repository allows merge commits, squash and rebase; `main` holds a mix of merge and squash commits.
- **What actually blocked PR #3173.** Head `50a39198e` was pushed at 11:19Z. `Build & test` ran 49 minutes and failed at about minute 45 on one flaky test (`WorkbenchCompensationSampleTests.The_sample_workflows_end_as_the_readme_says`, timed out after 2 m 31 s). A re-run started at 12:18Z. At 12:29Z the author pressed "Update branch", which cancelled the re-run and started a full run from zero. The two commits it brought in (#3123, #3174) merged cleanly with the PR (`git merge-tree` reports no conflict). The update cost a full cycle and bought nothing; because the org rules require approval and green checks on the exact head commit, it also reset the review.
- **Where the time goes.** Recent successful PR runs of `CI` take 37-57 minutes. In `Build & test`, restore takes about 40 s, the full build about 3 minutes, and the container-free test step 30-40 minutes. The test step runs every container-free test project on one 4-core runner. The projects' durations add up to 99 minutes. The long pole is `Elsa.Workbench.Tests`: 28-31 minutes for 61 tests, most of which start a Workbench process and share one xUnit collection, so they run one after another. Next are `Elsa.Cluster.EntityFrameworkCore.Tests` (16 min), `Elsa.Activities.Compensation.Tests` (9), `Elsa.Activities.Http.IntegrationTests` (8), `Elsa.Cli.Tests` (6), `Elsa.Architecture.Tests` (4.5), `Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests` (4), `Elsa.Secrets.Workflows.Tests` (4) and `Elsa3.Activities.Design.Import.Integration.Tests` (3). The job's budget was raised from 30 to 45 minutes (#3056) and on 2026-10-21 to 60 minutes.
- **Documentation fast path (spec 257).** `tools/ci/change_scope.py` classifies the whole PR diff against its base. A PR whose every changed path is documentation skips the .NET build and tests. A code PR that receives one more documentation-only commit still runs everything again.
- **Container suites.** The EF container suites (`tools/ci/ef-suites.json`, 23 legs plus the Secrets EF composition proof) run nightly and on manual dispatch only, by owner decision (#2632). In the last green nightly each leg took 1-5 minutes and they run in parallel.
- **Studio CI.** `.github/workflows/studio-ci.yml` runs on every pull request and push to `main` with no path filter; its Chromium browser tests take about 15 minutes. None of its checks is required. Studio's API clients are hand-written, Studio projects reference no backend project, and its browser tests answer every backend call with mocks, so no CI job today fails when a backend endpoint changes shape. That gap is tracked separately in #3177.
- **Runner capacity.** The repository is public, so standard GitHub-hosted runners cost nothing. The organization is on the Free plan: at most 20 concurrent jobs across the organization.
- **Merge authority.** Merges follow the org [AGENTS.md](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#review-and-merge-rules): the Code Review app (`elsa-workflows-review-bot[bot]`) merges after a non-author approval on the exact head commit and green CI, and Claude and Codex may self-merge under the standing authorization of 18 Oct 2026. Both merge directly today.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fast feedback on every PR commit (Priority: P1)

An author (human or agent) pushes a commit to a pull request and learns within minutes whether it builds and whether the unit-level tests pass, instead of waiting most of an hour.

**Why this priority**: It is where the time goes today and it does not depend on any governance change. Every PR and every agent benefits immediately.

**Independent Test**: Push a code change to a PR and measure the time from push until the required `Build & test` check reports.

**Acceptance Scenarios**:

1. **Given** a PR that changes code, **When** a commit is pushed, **Then** the build runs once and the unit-tier test projects run in parallel on several runners, and `Build & test` reports one aggregate result under its existing name.
2. **Given** a unit-tier test fails once and passes on a single automatic retry, **When** the PR-tier run finishes, **Then** it reports success, and the run summary names the retried test so the flakiness stays visible.
3. **Given** a unit-tier test fails twice, **When** the run finishes, **Then** `Build & test` fails and names the test.

---

### User Story 2 - Merging without updating the branch (Priority: P1)

An author whose PR is approved and green merges it even though other PRs landed on `main` in the meantime, without pressing "Update branch", re-running CI on the branch or getting re-approved. The combination of their PR with the latest `main` is still tested before it lands.

**Why this priority**: It removes the manual update loop and puts the expensive tests where they protect `main`: on the exact commit that will land.

**Independent Test**: Queue two approved PRs that each pass on their own; observe that both merge without any branch update and that each combination was tested before landing.

**Acceptance Scenarios**:

1. **Given** an approved PR with a green PR tier, **When** it is put in the merge queue, **Then** the queue tests the PR combined with the latest `main` (and any PRs ahead of it) against the merge-queue tier and merges it only if that passes.
2. **Given** a queued PR whose combination with `main` fails the merge-queue tier, **When** the queue run finishes, **Then** the PR leaves the queue unmerged, `main` is unchanged, and the author sees which check failed.
3. **Given** a queued PR that conflicts with `main`, **When** the queue builds its combination, **Then** the PR is removed from the queue with a conflict notice, as it would be today.
4. **Given** the Code Review app approves a PR, or a Claude or Codex worker completes a standing self-merge review loop, **When** it merges, **Then** it puts the PR in the merge queue instead of merging directly.

---

### User Story 3 - Expensive tests gate the merge, not every commit (Priority: P2)

The integration-tier projects (process-hosting, HTTP integration, Elsa 3 import integration) and the EF container suites run in the merge queue and block a merge that breaks them, but do not run on every PR commit. An author who suspects a regression can run that tier on their PR early; one who does not loses nothing, because the queue runs it anyway.

**Why this priority**: It moves the slowest and most expensive tests off the PR feedback loop while making them a merge gate for the first time (the EF container suites today run only nightly).

**Independent Test**: Push a commit to a PR and confirm no integration-tier project runs; queue the PR and confirm they all run and gate the merge; add the early-run label and confirm they run on the PR head.

**Acceptance Scenarios**:

1. **Given** a PR commit, **When** the PR tier runs, **Then** no integration-tier project and no EF container suite runs.
2. **Given** a PR in the merge queue, **When** the queue tier runs, **Then** every test project of both tiers, the architecture guards, the core-only build and test, and every EF container suite run without retries, and any failure blocks the merge.
3. **Given** an author adds the `ci:full` label to a PR, **When** CI runs on the PR head, **Then** the merge-queue tier runs there too; removing the label stops it running on later commits.
4. **Given** an author starts the workflow manually on their branch with the queue tier selected, **When** it runs, **Then** it runs the merge-queue tier on that branch without a label.

---

### User Story 4 - Documentation follow-ups do not re-run the tests (Priority: P2)

An author whose code PR is already green pushes a commit that changes only documentation (for example, a forgotten doc line). The required check reports success without rebuilding and retesting.

**Why this priority**: This is the case the author called out. It only becomes safe once the merge queue re-checks the real combination before landing.

**Independent Test**: On a PR whose head has a green `Build & test`, push a documentation-only commit and confirm `Build & test` reports success within a few minutes, naming the commit whose result it reused.

**Acceptance Scenarios**:

1. **Given** a PR head with a green `Build & test`, **When** the next commits change only documentation paths, **Then** `Build & test` reuses that green result, reports success, and names the reused commit in its summary.
2. **Given** a documentation-only follow-up that also merges `main` into the branch, **When** CI runs, **Then** the reuse does not apply and the full PR tier runs.
3. **Given** no green `Build & test` exists for any earlier commit of the PR, **When** documentation-only commits are pushed, **Then** spec 257's rule applies unchanged: the fast path only if the whole PR diff is documentation, otherwise a full run.

---

### User Story 5 - Every test project has a known tier and stays in it (Priority: P2)

A coding agent adding or changing tests knows which tier each test project belongs to, assigns any new project to a tier, and cannot silently put slow or process-hosting tests in the fast tier.

**Why this priority**: The tiering is only as good as its assignment. Without guards, drift would quietly push integration-style tests into the PR tier (slowing it again) or leave new projects running nowhere.

**Independent Test**: Add a new test project without assigning it; add a Testcontainers reference to a unit-tier project; add a 20-second test to a unit-tier project. Each must fail CI with a message that says what to do.

**Acceptance Scenarios**:

1. **Given** a test project that the tier manifest does not name, **When** CI runs, **Then** it fails and names the project, the manifest and the two tiers.
2. **Given** a unit-tier project whose references show integration traits (Testcontainers, a Workbench process host, an in-memory ASP.NET Core test server), **When** CI runs, **Then** it fails and says the project belongs in the integration tier.
3. **Given** a unit-tier test that takes longer than the per-test budget and is not on the allow-list, **When** its shard finishes, **Then** the shard fails and the message says to move the test to an integration-tier project or ask for an allow-list entry.
4. **Given** an agent working on tests, **When** it follows the test skill or the contributor documentation, **Then** both state the tier rule and point at the manifest.

---

### User Story 6 - Agents and humans know how to run the queue tier early and how to merge (Priority: P3)

Any author, including a Claude or Codex session, can find one documented way to run the merge-queue tier on a PR early and one documented way to merge through the queue, and every skill that drives a PR toward merge points there.

**Why this priority**: The mechanisms of Stories 2 and 3 only help if the people and agents who merge use them correctly.

**Independent Test**: Follow each PR-driving skill from "PR is green" to "merged" and confirm each reaches the documented section and enqueues rather than merging directly.

**Acceptance Scenarios**:

1. **Given** the contributor documentation, **When** an author looks for how to run the queue tier early, **Then** one section gives the exact commands for the label and for the manual run, and says when running early is worthwhile.
2. **Given** the skills that drive a PR toward merge (`elsa-auto-review-merge`, `elsa-quality-fix`, `speckit-implement`'s finishing step) and the test skill (`elsa-add-unit-tests`), **When** the guard that checks them runs, **Then** each links to that section; removing a link fails CI.
3. **Given** the label name or the manual-run input changes, **When** the workflow's self-test runs, **Then** it fails until the documentation names the same values.

---

### User Story 7 - Studio CI runs only when Studio changes (Priority: P3)

A backend-only PR does not start Studio's lint, unit, browser and .NET jobs; a PR that touches Studio does.

**Why this priority**: Those jobs give no protection on backend-only PRs (see Evidence) and take concurrent job slots from the 20-job organization limit.

**Independent Test**: Open a backend-only PR and confirm Studio CI does not run; open a Studio PR and confirm it does.

**Acceptance Scenarios**:

1. **Given** a PR that changes no Studio path, **When** it is opened or updated, **Then** Studio CI does not run.
2. **Given** a PR that changes a Studio path, shared build configuration, or Studio CI tooling, **When** it is opened or updated, **Then** Studio CI runs as it does today.
3. **Given** a push to `main`, **When** CI is triggered, **Then** Studio CI runs as it does today.

---

### Edge Cases

- **Flaky test in the merge queue.** The queue tier does not retry. A flaky failure removes the PR from the queue; the author re-queues it, and the flake is visible as a failure rather than hidden by a retry.
- **Queue group fails.** When a group of several PRs fails, the queue splits the group to find the PR that broke it, and the others continue.
- **Admin bypass.** Admins can still bypass the queue. The full CI run on every push to `main` and the red-main alert stay in place to catch that.
- **Cancelled runs.** A queue run cancelled because the queue changed is not a failure and must not file a "main is red" issue.
- **Job slot exhaustion.** With the 20-job organization limit, a queue run with many shards and EF legs may wait for slots behind other PRs' runs. Shard counts must leave room for concurrent PR runs.
- **Reuse across a `main` merge.** A documentation-only reuse must never apply when the commits since the green result include a merge of `main` or any non-documentation path.
- **Manifest change.** A PR that changes the tier manifest or the shard grouping runs the full PR tier.
- **Allow-listed slow test.** An allow-list entry names the test and the reason; the allow-list itself is part of the guarded manifest.
- **Docs-only PR in the queue.** A PR whose whole diff is documentation still passes through the queue, using spec 257's fast path for its tests.

## Requirements *(mandatory)*

### Functional Requirements

**PR tier (Story 1)**

- **FR-001**: On a pull request commit, CI MUST build the solution once and run the unit-tier test projects in parallel across several runners, reusing that one build.
- **FR-002**: The shard grouping MUST put no slow project alongside others, so that no shard's expected duration exceeds the PR-tier target (SC-001).
- **FR-003**: The required `Build & test` check MUST keep its name and report one aggregate result that is success only when the build and every PR-tier shard succeeded.
- **FR-004**: The PR tier MUST re-run a failing test once and only once; a test that passes on retry MUST be listed in the run summary as retried.

**Merge queue (Story 2)**

- **FR-005**: `main` MUST be protected by a merge queue that tests each PR combined with the latest `main` and the PRs ahead of it, and merges only when the merge-queue tier passes.
- **FR-006**: The queue MUST merge with squash, take up to 3 PRs per group, start a group with a single PR, and not wait for a group to fill.
- **FR-007**: Approval MUST remain on the PR head. Joining the queue MUST NOT require the branch to be up to date with `main`, and the up-to-date requirement MUST stay off.
- **FR-008**: The Code Review app and the standing self-merge flow MUST merge by putting the PR in the merge queue. The org AGENTS.md MUST be amended to say so, with the owner's sign-off, before the queue is enforced.

**Merge-queue tier (Story 3)**

- **FR-009**: The merge-queue tier MUST run every test project of both tiers, the architecture guards, the core-only build and test, and every EF container suite with the Secrets EF composition proof, all without retries.
- **FR-010**: The PR tier MUST NOT run integration-tier projects or EF container suites unless the author asks for it (FR-011).
- **FR-011**: An author MUST be able to run the merge-queue tier on a PR's head by adding the `ci:full` label, and on any branch through a manual workflow run that selects the queue tier. Neither MUST be required for merging.
- **FR-012**: The nightly scheduled run MUST keep running the full set, as a safety net.
- **FR-013**: The full CI run on every push to `main`, the maps publication and the red-main alert MUST stay as they are.

**Documentation-only reuse (Story 4)**

- **FR-014**: When every commit after the PR's most recent commit with a green `Build & test` touches only documentation paths (as spec 257 defines them) and none of them merges `main`, `Build & test` MUST report success by reusing that result and MUST name the reused commit in its summary.
- **FR-015**: When the condition of FR-014 does not hold, spec 257's whole-diff rule MUST apply unchanged.

**Test tiers (Story 5)**

- **FR-016**: A committed tier manifest MUST assign every test project in the solution to exactly one tier, unit or integration, and record the PR-tier shard grouping. It MUST be the single list both tiers and the shard jobs read.
- **FR-017**: CI MUST fail when a test project is missing from the manifest, or when the manifest names a project that does not exist.
- **FR-018**: CI MUST fail when a unit-tier project references Testcontainers, hosts a Workbench process, or uses an in-memory ASP.NET Core test server, directly or through a referenced test project.
- **FR-019**: A unit-tier shard MUST fail when any single test takes longer than 10 seconds, unless the test is on the manifest's allow-list with a reason. The allow-list MUST start out containing exactly the unit-tier tests that exceed the budget on `main` when the gate is introduced.
- **FR-020**: A test project MUST be wholly one tier. Mixing tiers inside one project (for example through per-test traits) MUST NOT be a supported way to assign a tier.
- **FR-021**: The `elsa-add-unit-tests` skill and the constitution's testing section MUST state the tier rule and point at the manifest.

**Documentation and skills (Story 6)**

- **FR-022**: The contributor merging documentation MUST have one section that explains the two tiers and gives the exact commands to run the queue tier early (label and manual run) and to merge through the queue, and says when running early is worthwhile.
- **FR-023**: `elsa-auto-review-merge`, `elsa-quality-fix`, `speckit-implement`'s finishing step and `elsa-add-unit-tests` MUST link to that section, and a guard MUST fail CI when one of those links is missing.
- **FR-024**: A self-test MUST fail when the label name or manual-run input the workflow uses differs from the one the documentation names.

**Studio CI (Story 7)**

- **FR-025**: Studio CI MUST run on pull requests only when a Studio path, a Studio extension path, shared build configuration, or Studio CI tooling changes, and MUST keep running on every push to `main`.

**Delivery order**

- **FR-026**: Stories 1, 5, 7 and the PR-tier parts of Story 6 MUST be deliverable and useful without the merge queue. Story 4's reuse MUST NOT be enabled before the merge queue is enforced, and Story 3's removal of integration-tier projects from the PR tier MUST NOT happen before the merge queue is enforced, so that no test stops gating merges in between.

### Key Entities

- **Tier**: A named set of checks. *PR tier*: runs on every PR commit and is fast, with one retry for failed tests. *Merge-queue tier*: runs on the combination about to land and is complete, with no retries.
- **Tier manifest**: The committed list of every test project with its tier, the PR-tier shard grouping, and the slow-test allow-list (test name, reason).
- **Shard**: A group of unit-tier test projects that one runner tests from the shared build output.
- **Queue group**: Up to 3 queued PRs combined with `main` and tested together.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a code PR, the median time from push to `Build & test` reporting is at most 12 minutes, down from 37-57 minutes, measured over the first 20 PR runs after Story 1 ships.
- **SC-002**: A documentation-only commit on top of a green code PR gets its `Build & test` result in at most 3 minutes.
- **SC-003**: After the merge queue is enforced, no author or agent needs "Update branch" to merge a conflict-free PR. Measured over 30 days, PR branches gain no "Merge main into" commits whose only purpose is to update.
- **SC-004**: A PR in the queue lands, or is rejected, within 30 minutes of entering an empty queue.
- **SC-005**: Every test project in the solution is in exactly one tier, and each of the three drift checks (missing assignment, integration traits in the unit tier, slow unit test) has a test that proves it fails.
- **SC-006**: No PR merges with a failing EF container suite or integration-tier project, which is a gate that does not exist today.
- **SC-007**: Backend-only PRs start no Studio CI jobs.

## Decisions

Settled with Joey Barten on 2026-10-21. The PR that implements the merge queue needs the owner's (Sipke's) sign-off, because it changes the org-wide merge rules.

1. **Merge queue, not strict up-to-date.** The queue is GitHub's way to test the real combination before landing without anyone updating branches by hand. Strict up-to-date would make the current pain mandatory.
2. **Build once, test in shards.** Rather than each shard building its own subset (repeating ~3 minutes of build) or raising parallelism on one 4-core runner (which cannot shorten the Workbench long pole).
3. **Reuse a green result across documentation-only commits**, only once the merge queue re-checks the real combination before landing.
4. **Retry once in the PR tier, never in the queue.** The retry protects authors' time. The strict queue keeps flakiness visible.
5. **One spec, with the merge queue gated on the owner.** Stories that do not depend on the queue ship first.
6. **Two tiers.** The PR tier runs unit-tier projects. The queue tier runs everything, including integration-tier projects and the EF container suites. Authors may run the queue tier early with the `ci:full` label or a manual run. It is recommended when they suspect a regression and never required.
7. **Manifest plus guards, not naming conventions.** One tier per project, enforced by three guards (missing assignment, integration traits, slow unit tests) and explained in the test skill and the constitution. Words alone drift; the guards make the rule unavoidable.
8. **Squash in the queue.** PR commits are the review trail and stay on the PR (`refs/pull/N/head` survives branch deletion). `main` gets one commit per PR, which keeps `log`, `blame`, `bisect` and `revert` simple, and keeps agents' WIP and "merge main into the branch" commits off `main`. The cost is that stacked PRs need a rebase after the lower PR lands.
9. **Studio CI is path-filtered.** The missing contract check between Studio and the backend is a separate concern (#3177).
10. **The push-to-`main` run stays full.** It is free and blocks nothing, and it covers admin bypasses of the queue.

## Assumptions

- GitHub merge queues are available for this public organization repository on the Free plan, and required checks can report on merge-queue events under the same names.
- The Code Review app can merge through the queue (by enqueueing or by enabling auto-merge). The app is maintained outside this repository; if it cannot, FR-008 blocks enforcement of the queue until it can.
- The 10-second budget fits the current unit-tier distribution once the long-running projects listed under Evidence move to the integration tier. The initial allow-list absorbs the remaining exceptions.
- `Elsa.Workbench.Tests`, `Elsa.Activities.Http.IntegrationTests` and `Elsa3.Activities.Design.Import.Integration.Tests` are integration tier. The tier of each other project, including `Elsa.Cluster.EntityFrameworkCore.Tests` and `Elsa.Activities.Compensation.Tests`, is decided during planning from its references and timings by the rule of FR-018 and FR-019.
- The 20-concurrent-job organization limit is shared with other repositories. The shard count is chosen in planning so that a PR run and a queue run can proceed at the same time.

## Out of Scope

- A contract or live test between Studio and the backend (#3177).
- Making `Elsa.Workbench.Tests` itself faster, for example by sharing one host across tests. Sharding keeps it off the PR critical path; speeding it up is separate work.
- Fixing the flaky `WorkbenchCompensationSampleTests` test seen on #3173. The retry and the summary make it visible; the fix is its own issue.
- Larger or self-hosted runners.
