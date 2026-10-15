# Repository hygiene

Branches and issues stay legible only if they have a lifecycle that ends. This page is the policy; two
workflows can enforce most of it and a weekly report shows the rest. Scheduled cleanup depends on
the [enforcement setting](#enforcement); in report-only mode, the workflows report proposed actions.

## Branches

A branch lives as long as its pull request.

- **Push a branch only together with a PR.** Open the PR (a draft is fine) in the same session that
  pushes the branch. Exploratory work that is not ready for a PR stays local.
- **Merged branches are deleted automatically.** The repository deletes the head branch on merge. Do not
  restore it: the work is in `main`, and the PR keeps its commits.
- **When enforcement is enabled, the weekly sweep deletes a branch that has no open PR** when its work is already in `main` (directly,
  by a squash merge, or as the merged tip of a PR), or when it has had no commit for 14 days.
- **Committed branch tips are retained.** Before deleting, the sweep tags the branch tip as
  `archive/<branch>`, or `archive/<branch>-<sha12>` when that tag already holds an earlier tip of the same
  name. The tag preserves committed files, not uncommitted, untracked, or ignored files in a local
  checkout. The weekly report lists each swept tip.

  If a maintainer asks to resume a swept branch, find its remote tag, fetch that exact tag, then restore
  the branch and open its PR. Replace `BRANCH-NAME` and `ARCHIVE-TAG` below; `ARCHIVE-TAG` is the full tag
  name returned by the first command, without the `refs/tags/` prefix:

  ```bash
  git ls-remote origin 'refs/tags/archive/BRANCH-NAME*'
  git fetch origin 'refs/tags/ARCHIVE-TAG:refs/tags/ARCHIVE-TAG'
  git push origin 'refs/tags/ARCHIVE-TAG:refs/heads/BRANCH-NAME'
  ```

  Branches deleted on merge are not restored; their work is in `main`.

- **Never swept:** `main`, `publish-state`, `release/*`, `rc/*` and any protected branch. A branch that
  must live longer than its PR (a demo, an integration line) needs an open PR or one of those patterns.
- Agents name branches `agent/<worker>/<issue>-<slug>`, per
  [Issue Bus Lite](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#work). The sweep does not
  enforce names; it reads PR state.
  [Packages](../../.github/workflows/packages.yml) runs on pushes to `main` only; a branch gets CI
  through its PR.

## Issues

An open issue is a commitment someone can act on, not a log entry.

- **Every open issue has a state:** one triage label (`needs-triage`, `needs-info`, `ready-for-agent`,
  `ready-for-human`) or, for program-tracked work, a `status:*` label. Programs and epics are exempt.
  `wontfix` issues are closed. See [triage labels](../agents/triage-labels.md).
- **Owner exception: agents file new issues as `needs-triage`.** Only a maintainer promotes an issue to a
  `ready-*` state. The one exception to that rule is the [UX QA routine](../skills/catalog.md#ux-qa-routine), also
  an owner exception. It may file a defect as
  `ready-for-agent` when a second agent reproduced it, its fix needs no design choice, and the repository is
  under the cap.
- **Progress goes on the existing record.** Checkpoints, receipts and gate evidence belong in PR
  comments or on the issue being worked, not in new issues. Do not file tracking, sweep or checkpoint
  issues. The standing `repo-hygiene`, `ux-qa-ledger` and `product-scout-ledger` issues are the only exceptions.
- **Work-in-progress caps:** at most 2 active `type:program` issues and at most 20 `ready-for-agent`
  issues. A program labelled `status:parked` stays open but does not count toward the cap, and gets no
  new child issues until it is reactivated. At the cap, add scope to the existing parent instead of
  filing more.
  Owner exception: the [11 October owner amendment](../adr/0080-elsa-4-simplification-decisions.md#d0--focus) makes named
  exceptions for existing consolidation #2501 and website/docs #2495. Keep those programs active under
  their existing queues; do not re-park them solely to meet the cap. This does not raise the general cap
  or authorize more programs, cleanup or changes to enforcement settings.
- **Stale intake closes.** An issue labelled `needs-triage` or `needs-info` with no activity for 30 days
  is labelled `stale` and closes as not planned 14 days later. Programs, epics, `security`, `bug` and
  `keep-open` issues are exempt.
- **Stale pull requests close.** A PR with no activity for 21 days is labelled `stale` and closes 7 days
  later, deleting its branch. Label it `keep-open` to exempt it.
- **Idle ready work is re-triaged.** A `ready-for-agent` issue nobody touched for 30 days goes back to
  triage or is closed; the weekly report lists them.
- **Close finished work as completed, not as not planned.** An issue whose work landed but was never
  closed looks idle by every other signal. Before closing one as not planned, check whether a commit on
  `main` references it and finished the work. The check skips references to other repositories
  (`owner/repo#<number>`):
  `git log origin/main -E --grep '(^|[^[:alnum:]_./-])#<number>([^0-9]|$)'`. The weekly report lists the
  open issues a commit on `main` references.

## Ownership and cadence

- **Owner:** the repository maintainer (currently Sipke).
- **Weekly:** the [Repository hygiene](../../.github/workflows/repo-hygiene.yml) workflow runs every
  Monday and comments its report on the open issue labelled `repo-hygiene`. The owner spends 15 minutes
  on what it flags: re-triage or close idle issues, and keep the program and ready caps.
- **Targets:** fewer than 100 open issues; branches roughly equal to open PRs plus exempt branches;
  weekly closures at least equal to new issues.

## Enforcement

Both workflows start in report-only mode: the sweep reports what it would delete and the
[stale workflow](../../.github/workflows/stale.yml) runs in debug-only mode. After reviewing the first
report, set the repository variable `HYGIENE_ENFORCE` to `true` to enforce both. A one-off cleanup can
run the Repository hygiene workflow manually with mode `enforce`.

Run the sweep locally in report mode, which changes nothing:

```bash
GITHUB_REPOSITORY=elsa-workflows/elsa-foundation bash tools/repo-hygiene/hygiene.sh report
```

## Claims

Moved from `AGENTS.md` (#2578); `AGENTS.md` keeps the short form. Claims follow the org-wide
[Issue Bus Lite](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#claim) protocol, which wins over
this section on conflict.

Several agent sessions run against this repository at once, in separate worktrees. Two sessions that pick the same unit will not see each other in git: a branch does not exist anywhere shared until it is pushed, so a clean check at session start proves nothing about what another session began a minute later.

1. Before claiming, read the issue's comments and the open PRs that reference it. If someone already has an open PR for the issue, stop.
2. Comment exactly `claim: <worker> starting`, where the worker is `codex`, `claude`, `cursor` or your app slug, for example `claim: claude starting` when posting as `elsa-workflows-claude[bot]`. Only when you post through the owner's account does your role tag come first, for example `[Claude] claim: claude starting`.
3. Re-read the comments. The earliest claim that hasn't been abandoned wins. If it isn't yours, comment `claim-abandoned` and leave the issue alone.
4. If you won, assign yourself and remove `ready-for-agent`. Assignment plus the missing label is the claim.
5. Branch `agent/<worker>/<issue>-<slug>` off the issue's target branch.

See [Issue tracker](../agents/issue-tracker.md) for the commands.

Re-check for competing work before committing, not only at session start. That check is the backstop that catches a session which started after you did. When a unit is split into slices, prefer giving one session the whole stack, or naming an owner per slice in the issue, over relying on claims alone.

To give up, comment `blocked: <reason>`, add `blocked` (or `needs:decision` when a decision is missing), and unassign yourself. Leave the branch and any draft PR for the next worker.
