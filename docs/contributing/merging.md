# Merging and post-merge gates

Moved from `AGENTS.md` (#2578). Claiming, PRs, review and merge follow the org-wide [Issue Bus Lite](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#issue-bus-lite) protocol, which wins over this page on conflict. This page adds the evidence a PR in this repository must carry. The [Merge Gate](../skills/catalog.md#merge-gate) is the executable checklist for that evidence.

## Program bookkeeping

When work belongs to a program, the GitHub issue is the public, legible record of progress. The program's project board Status alone is not enough.

- Comment on the issue at every state transition: the claim (`claim: <worker> starting`), the PR (`pr: <url>`), each review round and its outcome, and merged or closed. When you post through the owner's account, start each comment with your role tag, for example `[Claude]`.
- Keep the program's project board Status and any labels in sync at the same time.
- Throttle concurrent agent sessions on one program to two or three.

## PR evidence

Every PR carries this evidence. It is what a reviewer checks, not permission to merge.

- The PR body has `Fixes #<n>` and maps each acceptance criterion of the issue to its evidence.
- The build passes, and the affected suites pass as whole test projects. Compare the executed test count with the `--list-tests` count, because a green summary reports only what ran.
- The architecture guard passes: run `bash tools/architecture/restore-ci-project-graph.sh`, then `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj`.
- The generated-maps check passes: `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. See [Generated maps](generated-maps.md).
- When a `ProjectReference` changed, `dotnet restore Elsa.Server.slnx --locked-mode` passes.
- A diff review is done, and a revert or mutation bite-proof shows red then green wherever a behavioral change is claimed.
- The evidence is posted as a PR comment, because that comment is the notification of record.

## Review and merge

These steps restate [Issue Bus Lite](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#review-and-merge-rules) for convenience; the org file governs.

- An author never reviews, approves or merges its own PR, or one it committed to.
- Only the Code Review app (`elsa-4-code-review[bot]`) merges, after a separate, non-author approval on the exact head commit and green CI. New commits after an approval need a fresh approval.
- Fix review feedback in new commits, without force-pushing, and reply `Fixed in <sha>` or `Won't fix: <reason>` on each thread. After 3 rounds, a human is brought in.
- When a stop condition in the org file applies, comment `blocked: <reason>`, add `blocked` (or `needs:decision` when a decision is missing) and unassign yourself.
- Mention agents only by their `@vw-*` handles.

## Post-merge gates

A green PR is not the end of the gate. `CI` runs on every push to `main` as well as on pull requests, because a required check can be bypassed and because a change that passes on a branch can still break main in combination with what landed beside it. Its `Generated maps fresh` job, which checks the committed maps and solution filters, therefore runs on both too. The EF container suites (every Testcontainers test project, each run whole with its required-provider mode armed where it has one, plus the Secrets EF composition proof) are not a required check and do not run on either: they run in `CI`'s nightly schedule and on manual dispatch only (#2632), so a container-only regression surfaces the next morning, not on the PR. The former `HTTP workflow performance` gate was retired with all performance measurement by owner decision (#1668, ADR 0073); no performance workflow remains to run or to report as passed. Deterministic EF command-count budgets per reference workflow are correctness gates, not performance measurement, and are allowed (ADR 0080 D2).

When `CI` fails against `main` or on its nightly schedule, it files or updates a GitHub issue titled `main is red: CI`. The separate `Integration (nightly)` workflow and its `main is red: Integration (nightly)` issue are gone; `CI`'s nightly run took over its projects (#2632). Those issues are machine-filed, deduplicated by title, and never auto-closed:

- Treat one as a live report that main is failing that gate right now, not as stale noise.
- Fix forward or revert; nothing can be blocked after the fact, since the commit has landed.
- Close it by hand once the gate is green and the cause is understood. A later green run leaves it open on purpose.

Check for one before starting work that assumes a healthy main; it explains a local failure you would otherwise spend the session chasing.
