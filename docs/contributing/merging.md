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
- A PR whose every changed path is documentation, specs or agent files (`docs/**`, `specs/**`, `.specify/**`, `.agents/**`, `.claude/**`, root `*.md` other than `EXTENSION_POINTS.md`) gets a fast `Build & test` and `Full tier`: the Python guards and the catalog link check, with no .NET build. Build and test inputs among those paths, such as `specs/*/contracts/**` and MSBuild or analyzer config files, still get the full run, as do pushes to `main`, the nightly schedule and manual runs.
- When a `ProjectReference` changed, `dotnet restore Elsa.Server.slnx --locked-mode` passes.
- A PR that finishes a spec sets its `**Status**:` to `Implemented` (or `Superseded`/`Abandoned`) in the same PR, per the [transition rule](../reference/spec-lifecycle.md#transition-rule). There is no follow-up status PR.
- A diff review is done, and a revert or mutation bite-proof shows red then green wherever a behavioral change is claimed.
- The evidence is posted as a PR comment, because that comment is the notification of record.

## CI tiers

CI runs in two tiers (spec 263). [`tools/ci/test-tiers.json`](../../tools/ci/test-tiers.json) assigns every test project to one of them and groups the projects into shards that run in parallel.

- **PR tier, on every commit.** The required check `Build & test` is the aggregate of `Plan tiers`, the `PR shard (n)` jobs, `Architecture guards` and `Core-only build & test`. Each shard builds the whole solution and runs its projects, or its share of a long project split by test class.
- **Full tier, once the PR is not a draft.** The required check `Full tier` (workflow `CI full tier`) runs the integration-tier projects and every EF container suite with its required-provider mode, plus the Secrets EF composition proof. It runs when a PR is opened as ready, reopened, marked ready, and on every push while it is ready.
- **The tier rule.** A test project that reaches Testcontainers, directly or through a referenced test project, is integration tier. So is one that launches a child host (a Workbench host, a child host process, a worker, a probe or the `dotnet-elsa` CLI) and takes 2 minutes or more. Every other project is PR tier. A new test project needs an entry in the manifest; `Generated maps fresh` fails until it has one. Check locally with `dotnet run --project tools/maps/Elsa.Maps.Generator -- test-tiers-check`.

**The draft rule.** A PR stays a draft until its own review is done.

1. Open it as a draft, and push and fix freely.
2. Run the review loop, [Auto-Review Loop Then Merge](../skills/catalog.md#auto-review-loop-then-merge), on the draft until a round leaves no surviving findings. If it reaches its cap unconverged, go on to step 3 anyway and list the findings still open in the evidence comment: Code Review never reviews a draft.
3. Only then mark it ready and request review:

   ```bash
   gh pr ready <n>
   ```

**Why review the draft.** Code Review is the independent final check of a PR its author is already confident in, not its first reviewer, and a draft push is the cheap place to find and fix problems:

- **A draft push** runs only the PR tier, about 13 minutes and about 45 job-minutes, unless it changes the tier manifest or the tier tooling, which also runs the full tier.
- **A push to a ready PR** also runs the full tier, about 23 minutes and about 115 job-minutes more, on every push, even one that changes only documentation: the classifier looks at the PR's whole diff against `main`. Only a PR whose whole diff is documentation takes the fast path in both tiers.

So fix rounds belong on the draft. A finding from Code Review is still fixed on the ready PR, but the local rounds should leave few.

A draft carries no `Full tier` check, so a PR just marked ready stays unmergeable until the full tier has run on its head.

**The full tier early, on a draft.** Start `CI` by hand on the branch:

```bash
gh workflow run ci.yml --ref <branch> -f tier=full
```

It runs both tiers, the EF container suites included, and reports in the Actions run, not under the required `Full tier` check. There is no label for it. `-f tier=pr` runs the PR tier only.

**Retries and quarantine.**

- **One retry.** A failing test is re-run once, in both tiers. A test that passes on its retry does not fail the run: the run summary lists it, and the [CI flake tracker](https://github.com/elsa-workflows/elsa-foundation/issues/3263) records it.
- **Quarantine.** A known flaky test can be quarantined. Add an entry to `quarantine` in the manifest, naming the test's fully qualified method name and its open tracking issue. A quarantined test still runs; its failure is reported with the issue link and does not fail the run.
- **Tracking issues.** A PR that adds or changes an entry fails when its issue is missing or closed. `main` and the nightly run check every entry, so closing an issue without removing its entry files `main is red: CI`. Fix the test, then remove the entry and close the issue in the same PR.

**Slow tests.** A PR-tier test that takes more than 10 seconds is reported in its shard's summary. That is a report, not a failure, until the owner switches it on (spec 263, FR-020).

## Review and merge

These steps restate [Issue Bus Lite][org-agents-review] for convenience; the org file governs.

- An author never reviews, approves or merges its own PR, or one it committed to.
- Request review by commenting `@elsa-workflows-bot review` on the PR, once it is ready and its own review loop has ended on the draft: converged, or capped with the findings still open listed in the evidence comment ([the draft rule](#ci-tiers)). The one blocking reviewer is Code Review, posting as `elsa-workflows-review-bot[bot]`. Other review tools (CodeRabbit, Copilot, Greptile and Bugbot) and other reviews (spec and standards) are advisory. Code Review always takes them into account, but the decision is its own: only findings it adopts block.
- Review is a loop. Fix actionable findings in new commits, without force-pushing, and reply `Fixed in <sha>` or `Won't fix: <reason>` on each thread; Code Review then reviews again. When a round leaves no actionable findings and every required check (`Build & test`, `Generated maps fresh` and `Full tier`) is green on the exact head commit, Code Review approves and merges by itself. New commits after an approval need a fresh approval.
- After 6 rounds with blocking findings left, comment `blocked: review loop exhausted` and add `needs:decision`; that escalates to Sipke.
- Owner exception: an explicit instruction from Sipke in an agent session counts for that one action only. Record it in the PR, for example `merged on Sipke's instruction`.
- Fallback: while Code Review is unavailable, Sipke may approve and merge manually; agents wait, and no author merges its own PR.
- An ADR PR in an initiative's scope (it changes `docs/adr/` and says `Part of #N` for an `initiative`-labelled issue) is merged only after every approver in its approval plan has approved it ([initiatives](initiatives.md#how-work-flows)). Until then it stays a draft. Code Review does not approve or merge it while an approver's approval is missing, bot approvals never count, and a manual merge follows the same rule.
- When a stop condition in the org file applies, comment `blocked: <reason>`, add `blocked` (or `needs:decision` when a decision is missing) and unassign yourself. Red checks on the base branch count only when they are required checks.

## Post-merge gates

A green PR is not the end of the gate. `CI` runs on every push to `main` as well as on pull requests, because a required check can be bypassed and because a change that passes on a branch can still break main in combination with what landed beside it. Its `Generated maps fresh` job, which checks the committed solution filters and runs the generator self-tests, therefore runs on both too. The same push run publishes the documentation maps to the `generated-maps` branch; see [Generated maps](generated-maps.md). The EF container suites (every Testcontainers test project, each run whole with its required-provider mode armed where it has one, plus the Secrets EF composition proof) run in the [full tier](#ci-tiers) on every ready PR, in `CI`'s nightly schedule and on manual dispatch, but not on pushes to `main`. The former `HTTP workflow performance` gate was retired with all performance measurement by owner decision (#1668, ADR 0073); no performance workflow remains to run or to report as passed. Deterministic EF command-count budgets per reference workflow are correctness gates, not performance measurement, and are allowed (ADR 0080 D2).

When `CI` fails against `main` or on its nightly schedule, it files or updates a GitHub issue titled `main is red: CI`. The separate `Integration (nightly)` workflow and its `main is red: Integration (nightly)` issue are gone; `CI`'s nightly run took over its projects (#2632). Those issues are machine-filed, deduplicated by title, and never auto-closed:

- Treat one as a live report that main is failing that gate right now, not as stale noise.
- Fix forward or revert; nothing can be blocked after the fact, since the commit has landed.
- Close it by hand once the gate is green and the cause is understood. A later green run leaves it open on purpose.

Check for one before starting work that assumes a healthy main; it explains a local failure you would otherwise spend the session chasing.

[org-agents-ibl]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#issue-bus-lite
[org-agents-review]: https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#review-and-merge-rules
