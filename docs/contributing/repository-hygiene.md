# Repository hygiene

Branches and issues stay legible only if they have a lifecycle that ends. This page is the policy; two
workflows enforce most of it and a weekly report shows the rest.

## Branches

A branch lives as long as its pull request.

- **Push a branch only together with a PR.** Open the PR (a draft is fine) in the same session that
  pushes the branch. Exploratory work that is not ready for a PR stays local.
- **Merged branches are deleted automatically.** The repository deletes the head branch on merge. Do not
  restore it: the work is in `main`, and the PR keeps its commits.
- **The weekly sweep deletes a branch that has no open PR** when its work is already in `main` (directly,
  by a squash merge, or as the merged tip of a PR), or when it has had no commit for 14 days.
- **Nothing is lost.** Before deleting, the sweep tags the branch tip as `archive/<branch>`, or
  `archive/<branch>-<sha12>` when that tag already holds an earlier tip of the same name. The weekly
  report lists each swept tip. To resume work on a swept branch, find its tag and restore it, then open
  its PR:

  ```bash
  git ls-remote origin 'refs/tags/archive/<branch>*'
  git push origin <archive-tag>:refs/heads/<branch>
  ```

  Branches deleted on merge are not restored; their work is in `main`.

- **Never swept:** `main`, `publish-state`, `release/*`, `rc/*` and any protected branch. A branch that
  must live longer than its PR (a demo, an integration line) needs an open PR or one of those patterns.
- Naming is unchanged and not enforced; the sweep reads PR state, not names. Note that
  [Packages](../../.github/workflows/packages.yml) builds pushes to `feat/*`, `fix/*` and similar
  prefixes.

## Issues

An open issue is a commitment someone can act on, not a log entry.

- **Every open issue has a state:** one triage label (`needs-triage`, `needs-info`, `ready-for-agent`,
  `ready-for-human`) or, for program-tracked work, a `status:*` label. Programs and epics are exempt.
  `wontfix` issues are closed. See [triage labels](../agents/triage-labels.md).
- **Agents file new issues as `needs-triage`.** Only a maintainer promotes an issue to a `ready-*` state.
- **Progress goes on the existing record.** Checkpoints, receipts and gate evidence belong in PR
  comments or on the issue being worked, not in new issues. Do not file tracking, sweep or checkpoint
  issues.
- **Work-in-progress caps:** at most 2 active `type:program` issues and at most 20 `ready-for-agent`
  issues. A program labelled `status:parked` stays open but does not count toward the cap, and gets no
  new child issues until it is reactivated. At the cap, add scope to the existing parent instead of
  filing more.
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
