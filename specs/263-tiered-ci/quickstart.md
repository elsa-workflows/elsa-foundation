# Quickstart: validating tiered CI

These are the checks that show each story works. Local checks need Python 3 and the .NET 10 SDK. PR checks use throwaway draft PRs from a branch of this repository; close each when done.

## Local checks

```bash
python3 tools/ci/test_tiers.py --self-test
python3 tools/ci/test_results.py --self-test
python3 tools/ci/test_tiers.py check-docs
python3 tools/ci/test_tiers.py select --tier pr --summary /dev/stdout
dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- test-tiers-self-test
dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- test-tiers-check
dotnet run --project tools/maps/Elsa.Maps.Generator -c Release -- ef-suites-self-test
```

Each command exits 0 on a clean tree.

## Story 1 — fast PR tier

1. Open a draft PR that changes one line in a PR-tier project's source.
2. Confirm that `Plan tiers`, the `PR shard (n)` jobs, `Architecture guards`, `Core-only build & test` and `Build & test` run, and that the full-tier aggregate reports as `Full tier (not run: draft)`.
3. Record the push-to-`Build & test` time. SC-001 is the median of the first 20 code PRs after the switch, taken from the runs API: `created_at` of the run to `completed_at` of `Build & test`.
4. **Retry.** Add a test that fails on its first run and passes on the second; it can use a marker file in `$RUNNER_TEMP`. Confirm the shard succeeds, the summary lists the test under "passed on retry", and after the run `ci-flake-record` comments on the `ci-flake` issue.
5. **Double failure.** Make the test always fail. Confirm `Build & test` fails and names it.

## Story 2 — full tier gates the merge

1. On the draft from Story 1, confirm the head carries `Full tier (not run: draft)` and no `Full tier` check, and that no integration or EF shard ran.
2. Run `gh workflow run ci.yml --ref <branch> -f tier=full`. Confirm the integration and EF shards run on the draft's head, and that the PR stays a draft.
3. Run `gh pr ready <n>`. Straight away, and again while `CI full tier` is queued or running, confirm `gh pr view <n> --json mergeStateStatus` is `BLOCKED` and that `Full tier` shows as expected (with the ruleset requiring it, after T036) or absent. Then confirm `CI full tier` runs on the same head, `Build & test` does not re-run, and `Full tier` reports after the shards.
4. Push another commit. Confirm both workflows run.
5. Close and reopen the PR. Confirm both run again.
6. Open a second PR directly as ready. Confirm both run on open.
7. With `Full tier` failing (a failing test in `Elsa.Workbench.Tests`), confirm GitHub blocks the merge once the ruleset requires `Full tier`.
8. Open a documentation-only ready PR. Confirm `Build & test` and `Full tier` both take the fast path and report success within a few minutes.
9. Run `gh workflow run ci.yml --ref <branch> -f tier=full`. Confirm the PR tier, the integration shards and the EF container shards all run.

## Story 3 — quarantine

1. Confirm every test in `quarantine` has an open issue: `python3 tools/ci/test_results.py check-quarantine --manifest tools/ci/test-tiers.json`.
2. In a throwaway PR, make a quarantined test fail. Confirm the shard succeeds and the summary lists it with its issue link.
3. In a throwaway PR, add an entry that points at a closed issue. Confirm `Plan tiers` fails and names the entry.
4. With an entry already on `main` whose issue is then closed, confirm an open PR that does not touch the entry still passes `Plan tiers`, and that `python3 tools/ci/test_results.py check-quarantine --manifest tools/ci/test-tiers.json` (no `--base`, as `main` and nightly run it) fails and names the entry.

## Story 4 — tier guards

1. Add a new empty xUnit project under `Essentials/tests/` without a manifest entry. Confirm `Generated maps fresh` fails, naming the project, `tools/ci/test-tiers.json` and both tiers.
2. Add `<PackageReference Include="Testcontainers" />` to a PR-tier project. Confirm the same check fails and says the project belongs in the integration tier.
3. Add a `Process.Start("dotnet", …)` launch to a PR-tier project. Confirm it fails the same way.
4. Add a `[Fact]` that sleeps 20 s to a PR-tier project. Confirm the shard summary reports it under "slower than 10 s" and the run still succeeds.
5. Add a class to a split project without naming it in a group. Confirm it runs in the rest group.

## Story 5 — documentation and skills

1. Remove the `#ci-tiers` link from one of the four skills. Confirm `Plan tiers` fails, naming the skill.
2. Rename the `tier` input's `full` option in `ci.yml` only. Confirm `check-docs` fails.

## Story 6 — Studio CI

1. Open a draft PR that changes only `Essentials/src/…`. Confirm Studio CI does not start.
2. Open one that changes `Studio/src/…`. Confirm Studio CI runs all four jobs.
3. After a merge to `main`, confirm Studio CI runs on the push.

## FR-011 ordering

1. Merge with `integrationLeavesPrTier: false`. `Full tier` runs, and the integration projects run in both tiers.
2. An admin adds `Full tier` to ruleset `main-2`'s required contexts:

   ```bash
   gh api repos/elsa-workflows/elsa-foundation/rulesets --jq '.[] | select(.name=="main-2") | .id'
   ```

   The admin then adds the context in the ruleset's settings.
3. A follow-up PR flips `integrationLeavesPrTier` to `true`. Its `Plan tiers` confirms that the ruleset lists `Full tier`. After it merges, the PR tier drops to at most 5 test jobs.
