# Merging and post-merge gates

Moved from `AGENTS.md` (#2578). The [Merge Gate](../skills/catalog.md#merge-gate) is the executable checklist.

## Program bookkeeping

When work belongs to a program, the GitHub issue is the public, legible record of progress. The program's project board Status alone is not enough.

- Comment on the issue at every state transition: implementation started (with branch name and scope), PR opened (with link), each review round and its outcome, and merged or closed.
- Keep the program's project board Status and any labels in sync at the same time.
- Throttle concurrent agent sessions on one program to two or three.
- Merge only on a green gate. Run the build, the affected suites, the architecture guard, the generated-maps check, and a diff review; add a revert or mutation bite-proof where a behavioral change is claimed. Post that evidence as a PR comment, because that comment is the notification of record. Do not self-merge a peer session's PR without it.

## Post-merge gates

A green PR is not the end of the gate. `CI` runs on every push to `main` as well as on pull requests, because a required check can be bypassed and because a change that passes on a branch can still break main in combination with what landed beside it. Its `Generated maps fresh` job, which checks the committed maps and solution filters, therefore runs on both too. The EF container suites (every Testcontainers test project, each run whole with its required-provider mode armed where it has one, plus the Secrets EF composition proof) are not a required check and do not run on either: they run in `CI`'s nightly schedule and on manual dispatch only (#2632), so a container-only regression surfaces the next morning, not on the PR. The former `HTTP workflow performance` gate was retired with all performance measurement by owner decision (#1668, ADR 0073); no performance workflow remains to run or to report as passed. Deterministic EF command-count budgets per reference workflow are correctness gates, not performance measurement, and are allowed (ADR 0080 D2).

When `CI` fails against `main` or on its nightly schedule, it files or updates a GitHub issue titled `main is red: CI`. The separate `Integration (nightly)` workflow and its `main is red: Integration (nightly)` issue are gone; `CI`'s nightly run took over its projects (#2632). Those issues are machine-filed, deduplicated by title, and never auto-closed:

- Treat one as a live report that main is failing that gate right now, not as stale noise.
- Fix forward or revert; nothing can be blocked after the fact, since the commit has landed.
- Close it by hand once the gate is green and the cause is understood. A later green run leaves it open on purpose.

Check for one before starting work that assumes a healthy main; it explains a local failure you would otherwise spend the session chasing.
