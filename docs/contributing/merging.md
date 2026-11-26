# Merging and post-merge gates

Moved from `AGENTS.md` (#2578). Claiming, PRs, review and merge follow the org-wide [Issue Bus Lite][org-agents-ibl] protocol, which wins over this page on conflict. This page adds the evidence a PR in this repository must carry. The [Merge Gate](../skills/catalog.md#merge-gate) is the executable checklist for that evidence.

## Program bookkeeping

When work belongs to a program, the GitHub issue is the public, legible record of progress.

- Comment on the issue at every state transition: the claim (`claim: <worker> starting`), the PR (`pr: <url>`), each review round and its outcome, and merged or closed. Post as your agent's app (Claude sessions as `elsa-workflows-claude[bot]`, Codex as `elsa-workflows-codex[bot]`) with no role prefix. Only when you post through the owner's account does your role tag come first, for example `[Claude]`.
- Throttle concurrent agent sessions on one program to two or three.

## PR evidence

Every PR carries this evidence. It is what a reviewer checks, not permission to merge.

- The PR body has `Fixes #<n>` and maps each acceptance criterion of the issue to its evidence.
- The build passes, and the affected suites pass as whole test projects. Compare the executed test count with the `--list-tests` count, because a green summary reports only what ran.
- The architecture guard passes: run `bash tools/architecture/restore-ci-project-graph.sh`, then `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj`.
- The generated-maps check passes: `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`, which compares the committed solution filters. The documentation maps and the dependency map are not committed, so no PR regenerates them. See [Generated maps](generated-maps.md).
- A PR whose every changed path is documentation, specs or agent files (`docs/**`, `specs/**`, `.specify/**`, `.agents/**`, `.claude/**`, `tools/initiatives/**` (standalone initiative gate scripts; the retired-persistence-family guard over `tools/` then runs only on `main`), `plugins/**` (user-facing product skills; the migration skill's check still runs), root `*.md` other than `EXTENSION_POINTS.md`) gets a fast `Build & test` and `Full tier`: the Python guards and the catalog link check, with no .NET build. Build and test inputs among those paths, such as `specs/*/contracts/**` and MSBuild or analyzer config files, still get the full run, as do pushes to `main`, the nightly schedule and manual runs.
- When a `ProjectReference` changed, `dotnet restore Elsa.Server.slnx --locked-mode` passes.
- A PR that finishes a spec sets its `**Status**:` to `Implemented` (or `Superseded`/`Abandoned`) in the same PR, per the [transition rule](../reference/spec-lifecycle.md#transition-rule). There is no follow-up status PR.
- A diff review is done, and a revert or mutation bite-proof shows red then green wherever a behavioral change is claimed.
- The evidence is posted as a PR comment, because that comment is the notification of record.

Visible Studio changes also carry the [UX acceptance receipt](studio-ux-acceptance.md#independent-ux-acceptance-review): exact head, goal/reference, actual states inspected, downloadable composed evidence, independent reviewer, findings/retest and unverified states. Machine proof success is separate from visual approval. Failed or incomplete UX acceptance is not delivery acceptance, including under a standing self-merge exception.

## CI tiers

CI runs in two tiers (spec 263). [`tools/ci/test-tiers.json`](../../tools/ci/test-tiers.json) assigns every test project to one of them and groups the projects into shards that run in parallel.

- **One gate: the PR tier, on every commit.** The required check `Build & test` (workflow `CI`) is the aggregate of `Plan tiers`, the `PR shard (n)` jobs, `Architecture guards`, `Core-only build & test` and, on a pull request, `Generated maps`, plus `Studio frontend lint, typecheck & unit tests` when the change touches Studio. The Studio frontend and generated-maps results are checked before the documentation-only fast path. Each shard builds the whole solution and runs its projects, or its share of a long project split by test class. This is the lean CI (owner-approved 20 Nov 2026): one gate of about 12 minutes on pull requests and pushes to `main`.
- **Full tier, nightly.** The integration-tier projects and every EF container suite with its required-provider mode, plus the Secrets EF composition proof, run in `CI`'s nightly schedule (02:41 UTC) and on a manual run, not on pull requests or pushes. Run the full tier on a branch, a draft's head included, with `gh workflow run ci.yml --ref <branch> -f tier=full`; do that before merging a change to a store or another container-backed path (K1b store work included), and say so in the PR's evidence.
- **The other heavy suites, nightly.** The Studio browser shards, the composed Studio UX acceptance, `Studio .NET build & test`, the Ubuntu/Windows backend source journeys, the Weaver package witness, the pinned published-image pair and the shuffled Workflows unit tests also run only in the nightly schedule and on a manual run that selects them (see below).
- **One issue per red nightly suite.** `Alert on a red main` files or updates `main is red: CI` when the gate fails on a push or the nightly run, and `main is red: CI nightly, <suite>` for each heavy suite that fails in the nightly run. Elsa 4 Lead triages them each morning.
- **Two stub checks until the ruleset changes.** `Full tier` and `Generated maps fresh` are still required by ruleset `main-2`, so `CI` keeps both as pass-through jobs that report success on every pull request. The real generated-maps check runs as `Generated maps` inside `Build & test`. Once an admin drops the two contexts from `main-2`, the stubs are deleted. The `full-tier` label and the `CI_FULL_TIER_EVERY_PUSH` variable no longer change anything.
- **No merge queue.** `main`'s ruleset has no merge queue rule, and `CI` has no `merge_group` trigger (removed with the lean CI, owner-approved 20 Nov 2026). PRs merge directly once the required checks are green on their head; the push run on `main` and the nightly run catch what lands beside them.
- **Kernel prototype gate.** `Kernel prototype gate` (workflow `CI`, not a required check) runs the standalone kernel model's gate on a non-draft PR that changes `prototypes/kernel-model/**`, `tools/ci/kernel_prototype_gate.py` or `.github/workflows/ci.yml`, and reports as skipped on any other PR. The `Kernel prototype nightly` jobs run in `CI`'s nightly schedule. Run either on a branch with `gh workflow run ci.yml --ref <branch> -f suite=kernel-gate` (or `suite=kernel-nightly`).
- **Only the affected shards, on a pull request** (#3787). On a `pull_request` run, the PR tier keeps only the shard units and split groups whose test project the PR's changes can reach, and drops a shard left empty. [`tools/ci/affected_tests.py`](../../tools/ci/affected_tests.py) decides, from the PR's diff against today's `main`. A test project is affected when it, or a project it references through `ProjectReference`, owns a changed path. A path belongs to every project whose directory encloses it, and to every project that reads it from outside: an MSBuild `Import`, a linked `Compile`, a `None` item copied to the output, a path property, or a repository path the test's C# builds with `Path.Combine`, such as evidence under `specs/`, Studio fixtures or a sibling app. `Plan tiers` lists in its run summary the changed projects, the selected test projects, and the reason when everything runs.
  - **Everything runs** when the PR changes a shared build input: `Directory.*.props`/`.targets`, `Directory.Packages.props`, `global.json`, `NuGet.config`, `.editorconfig`, a `.sln`/`.slnx`, a file a `Directory.*` file imports, or a project it adds to every project (the analyzers). It also runs everything for a change under `.github/**`, `tools/ci/**`, `tools/maps/**`, `tools/architecture/**` or `tools/solution-filters/**`, for `tools/bpmn/restore_evidence.py`, for a `packages.lock.json` outside a project, for a deleted project, for any other path outside every project that is not documentation, and whenever the diff or the project graph cannot be read.
  - **No shard of their own.** Markdown outside a project, or not named by its csproj, a spec contract (`specs/*/contracts/**`) and a solution filter (`.slnf`) are read only by Architecture guards, Core-only, `Generated maps` and the Python guards in `Plan tiers`, which run on every PR that is not documentation-only. The `Studio/` tree and the `tools/studio/` probes have their own jobs. A test that reads one of these paths is still selected.
  - **Building the solution.** Every PR shard builds the whole solution, which is what catches a compile break (#543). When a PR keeps no PR shard but changes a solution project that neither Architecture guards nor Core-only builds, one PR shard still runs, cut to its quickest project.
  - **Nothing affected still reports.** `Build & test` passes on an explicit `pr_count` of 0 once Architecture guards, Core-only and Generated maps pass. `Generated maps` always runs on a pull request.
  - **The trade-off.** A dependency the selection cannot see, such as a test that builds a path from variables, is missed on the PR. Pushes to `main`, the nightly run and manual runs run every shard; the push run on `main` is where such a miss shows up.
- **Pull requests to an initiative branch** (`initiative/*`, see [Initiatives](initiatives.md)) run the same CI as a pull request to `main`, with the initiative branch as the base the change is diffed against: the same tier selection and the same documentation-only fast path. A PR stacked on a proposal branch does not target `initiative/*` and runs no CI until it does. `test_tiers.py check-workflows` keeps the pull-request targets at exactly `main` and `initiative/**`.
- **The tier rule.** A test project that reaches Testcontainers, directly or through a referenced test project, is integration tier. So is one that launches a child host (a Workbench host, a child host process, a worker, a probe or the `dotnet-elsa` CLI) and takes 2 minutes or more. Every other project is PR tier. A new test project needs an entry in the manifest; `Generated maps` (and so `Build & test`) fails until it has one. Check locally with `dotnet run --project tools/maps/Elsa.Maps.Generator -- test-tiers-check`.

The heavy source proofs run as jobs in `CI`'s nightly schedule: Studio browser/.NET validation and the composed
Studio UX suite, the Ubuntu/Windows backend source journeys, Weaver package conformance, the pinned published-image
pair and the Workflows unit-order checks. Pull requests and pushes to `main` run the Studio frontend checks when
Studio changed (`tools/ci/workflow-paths.json` keeps that path scope), not the heavy proofs. Task-specific local UX
acceptance remains part of each visible change’s review. Run an individual suite on a branch before merging when a
change needs its proof, without starting the backend tiers:

```bash
gh workflow run ci.yml --ref <branch> -f suite=studio
```

Other suite values are `backend-source`, `weaver`, `published-images`, and `studio-order`.
The default `suite=ci` retains the existing `tier=full|pr` dispatch. Marking a PR ready starts a
complete ready run: the PR tier, generated maps and the two stub checks report together. It supersedes an in-progress
draft run. This avoids
the protected-merge failure where splitting successful required contexts across draft and ready
suites still leaves GitHub expecting `Build & test` and `Generated maps fresh` (#3708). A later
push supersedes that ready run through the same PR concurrency group.

Trusted flake recording, CodeQL scanning, daily stale processing and the weekly branch sweep share the
`Repository maintenance` workflow. Flake artifacts remain data read by default-branch code with
a separate issue-write token; ordinary PR jobs still have read-only tokens.

**The draft rule.** A PR stays a draft until its own review is done.

1. Open it as a draft, and push and fix freely.
2. Run the review loop, [Auto-Review Loop Then Merge](../skills/catalog.md#auto-review-loop-then-merge), on the draft until a round leaves no surviving findings. If it reaches its cap unconverged, go on to step 3 anyway and list the findings still open in the evidence comment: Code Review never reviews a draft.
3. Only then mark it ready and request review:

   ```bash
   gh pr ready <n>
   ```

**Why review the draft.** Code Review is the independent final check of a PR its author is already confident in, not its first reviewer, and a draft push is the cheap place to find and fix problems:

- **Every push**, draft or ready, runs the PR tier, about 13 minutes and about 45 job-minutes when every shard is affected. Only a PR whose whole diff is documentation takes the fast path.
- **The heavy suites** (the full tier, the Studio Chromium shards, Studio .NET, composed UX and the Weaver witness) do not run on a PR. Dispatch the ones a change needs on its branch (see above). Accepted gap: a regression they would catch shows up in the next nightly run, up to a day after merge, and files its own issue.

So fix rounds belong on the draft. A finding from Code Review is still fixed on the ready PR, but the local rounds should leave few.


**Catching up with `main`.** There is no merge queue, and `main`'s required checks are not strict, so merge `main` in before a PR lands.

**The full tier on a branch.** Start `CI` by hand on the branch, a draft's included:

```bash
gh workflow run ci.yml --ref <branch> -f tier=full
```

It runs both tiers, the EF container suites included, and reports in the Actions run, not under the required `Full tier` check (a stub until `main-2` drops it). There is no label for it. `-f tier=pr` runs the PR tier only.

**Retries and quarantine.**

- **One retry.** A failing test is re-run once, in both tiers. A test that passes on its retry does not fail the run: the run summary lists it, and the [CI flake tracker](https://github.com/elsa-workflows/elsa-foundation/issues/3263) records it.
- **Quarantine.** A known flaky test can be quarantined. Add an entry to `quarantine` in the manifest, naming the test's fully qualified method name and its open tracking issue. A quarantined test still runs; its failure is reported with the issue link and does not fail the run.
- **Tracking issues.** A PR that adds or changes an entry fails when its issue is missing or closed. `main` and the nightly run check every entry, so closing an issue without removing its entry files `main is red: CI`. Fix the test, then remove the entry and close the issue in the same PR.

**Slow tests.** A PR-tier test that takes more than 10 seconds is reported in its shard's summary. That is a report, not a failure, until the owner switches it on (spec 263, FR-020).

## Review and merge

These steps restate [Issue Bus Lite][org-agents-review] for convenience; the org file governs.

- An author never reviews, approves or merges its own PR, or one it committed to.
- Request review by commenting `@elsa-workflows-bot review` on the PR, once it is ready and its own review loop has ended on the draft: converged, or capped with the findings still open listed in the evidence comment ([the draft rule](#ci-tiers)). The one blocking reviewer is Code Review, posting as `elsa-workflows-review-bot[bot]`. Other review tools (CodeRabbit, Copilot, Greptile and Bugbot) and other reviews (spec and standards) are advisory. Code Review always takes them into account, but the decision is its own: only findings it adopts block.
- Review is a loop. Fix actionable findings in new commits, without force-pushing, and reply `Fixed in <sha>` or `Won't fix: <reason>` on each thread; Code Review then reviews again. When a round leaves no actionable findings and every required check (`Build & test`, plus the `Generated maps fresh` and `Full tier` stubs while `main-2` still requires them) is green on the exact head commit, Code Review approves and merges by itself. New commits after an approval need a fresh approval.
- After 6 rounds with blocking findings left, comment `blocked: review loop exhausted` and add `needs:decision`; that escalates to Sipke.
- Owner exception: an explicit instruction from Sipke in an agent session counts for that one action only. Record it in the PR, for example `merged on Sipke's instruction`.
- Fallback: while Code Review is unavailable, Sipke may approve and merge manually; agents wait, and no author merges its own PR.
- An ADR or spec approval/amendment PR in an initiative's scope follows the
  [initiative boundary and scope rules](initiatives.md#two-planning-systems-and-one-way-of-working), including PRs
  linked through descendant issues or issues they close; a missing link does not exempt a topic on the board. It
  stays a draft until the [agreement rule](initiatives.md#how-work-flows) is met on its final text and approval plan:
  every designated human non-author approver has approved the current head, or an earlier commit whose approval
  remains valid under that rule's materiality threshold, and any designated human author has recorded agreement
  without self-approving. Bot review never counts as architectural agreement, and a manual merge follows the same
  rule. Material changes and changed approval plans require fresh agreement; non-semantic edits retain it. The
  coordinator records the compared commits and explains exactly what changed and how it affects each reviewer's prior
  answers when requesting renewed agreement. Such a PR normally targets the initiative branch and is merged as the
  next bullet says. Whenever it targets `main` instead (its initiative has landed, it has not been retargeted yet, or
  its author works outside the board), Code Review does not approve or merge it before agreement, and this rule does
  not relax the normal Code Review approval and CI requirements on the exact head. A delivery PR that only advances an already agreed spec's lifecycle status follows
  the normal merge path without reopening its architectural agreement.
- **Initiative branches.** A PR whose base is an `initiative/*` branch, or the branch of a proposal PR stacked on one,
  is an initiative proposal. It carries no required CI checks (a PR to `initiative/*` still runs CI as a PR to
  `main` does, see [CI tiers](#ci-tiers)), Code Review does not merge it, and the initiative's
  coordinator merges it after the agreement rule and the [pre-merge gate](initiatives.md#the-pre-merge-gate) (a
  stacked child is merged into its parent by the parent's author), per the
  [org review and merge rules][org-agents-review]. Its author runs the review loop on the draft as usual, posts the
  evidence and requests the approvers instead of Code Review; it stays a draft until agreement. The landing PR from an
  initiative branch to `main` follows the normal path on this page, and Code Review approves and merges it. Its
  decisions are already agreed, so one approver's attestation comment stands in for the per-PR agreement rule
  ([Landing](initiatives.md#landing)).
- When a stop condition in the org file applies, comment `blocked: <reason>`, add `blocked` (or `needs:decision` when a decision is missing) and unassign yourself. Red checks on the base branch count only when they are required checks.

## Post-merge gates

Main CI finishes the active push run and retains the newest pending push in its branch concurrency group. Intermediate pending commits are replaced. This gives continuous merges a completed validation result while bounding the queue to one active and one pending run. Pull-request updates continue to cancel their superseded head. Focused proofs diff the surviving push from the head of the last push run that ran (#4713), so a replaced pending Studio push still selects its Studio frontend checks in the next run; the browser proofs run nightly. A manual `gh workflow run ci.yml --ref <committed-ref> -f suite=studio` supplies the full Studio proof for a specific head when needed. The nightly composed suite and task-specific local acceptance retain their own coverage.

A green PR is not the end of the gate. `CI` runs on every push to `main` as well as on pull requests, because a required check can be bypassed and because a change that passes on a branch can still break main in combination with what landed beside it, and the push run catches that. The push run is the gate only (and the Studio frontend checks when Studio changed since the last push run that ran, so a merge whose own run was replaced while pending, or whose head carried `[skip ci]`, still reaches them, #4713): the integration shards, the EF container suites and the other heavy suites run in the nightly run, not on pushes to `main`. Its `Generated maps` job, which checks the committed solution filters and runs the generator self-tests, runs on pull requests, pushes and the nightly run. The same push run publishes the documentation maps to the `generated-maps` branch; see [Generated maps](generated-maps.md). The EF container suites (every Testcontainers test project, each run whole with its required-provider mode armed where it has one, plus the Secrets EF composition proof) run in the [full tier](#ci-tiers): in `CI`'s nightly schedule and on manual dispatch, not on pull requests or pushes to `main`. The former `HTTP workflow performance` gate was retired with all performance measurement by owner decision (#1668, ADR 0073); no performance workflow remains to run or to report as passed. Deterministic EF command-count budgets per reference workflow are correctness gates, not performance measurement, and are allowed (ADR 0080 D2).

When `CI` fails against `main` or on its nightly schedule, it files or updates a GitHub issue titled `main is red: CI`. The separate `Integration (nightly)` workflow and its `main is red: Integration (nightly)` issue are gone; `CI`'s nightly run took over its projects (#2632). Those issues are machine-filed, deduplicated by title, and never auto-closed:

- Treat one as a live report that main is failing that gate right now, not as stale noise.
- Fix forward or revert; nothing can be blocked after the fact, since the commit has landed.
- Close it by hand once the gate is green and the cause is understood. A later green run leaves it open on purpose.

Check for one before starting work that assumes a healthy main; it explains a local failure you would otherwise spend the session chasing.

[org-agents-ibl]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#issue-bus-lite
[org-agents-review]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#review-and-merge-rules
