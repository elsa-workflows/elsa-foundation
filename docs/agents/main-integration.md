# Integrating `main` into a working branch

`main` is the source of truth. A branch is a proposal; `main` is what the project has agreed.
This page is for the session doing the integration — human or AI — because merging `main` can
change what your branch is *allowed* to do, not just what it conflicts with textually.

## Why this page exists

Work increasingly happens in several AI workspaces against the same application. Each session sees
its own branch clearly and the others not at all. The expensive failures are not merge conflicts —
git reports those. They are the silent ones: a gate, validator, or invariant that landed on `main`
while you were working, which your branch now trips in a way that does not look like your change.

The cost is almost never the fix. It is the discovery.

## Check early, not at PR time

Check whether `main` is ahead at the **start** of a work session and again before you claim a suite
is green — not only when you open the PR.

```bash
git fetch origin main && git log --oneline HEAD..origin/main | head -30
```

If it is ahead, read what landed before merging — the commit subjects are usually enough to tell
whether a new gate is among them:

```bash
git log --oneline --no-merges HEAD..origin/main
git diff --stat HEAD...origin/main -- .specify/memory/ docs/adr/
```

## What a merge can mean to you

A merge brings four kinds of change. Only the first is visible to git.

1. **Textual conflicts.** Git tells you. Least dangerous.
2. **Behavioural changes** in code you call. Unit tests usually catch these.
3. **New gates and validators.** Code that was fine yesterday is now rejected. These fail *loudly*
   but often *somewhere unrelated*, and the message rarely names your change.
4. **New invariants that make a whole mode of operation impossible.** These are the ones that cost
   days. They frequently pass every unit test, because unit tests exercise the mode that still works.

After merging `main`, re-run the suites that cover **deployment modes**, not just the unit tests.
A gate can be satisfied trivially in one composition and impossible in another. See
[backend e2e tests](../../e2e-tests/README.md).

## Failure shapes worth recognising

Recorded from real integrations, because each one was diagnosed the slow way at least once.

- **A healthy-looking host that serves nothing.** Shell reports `Active`, `/health/ready` returns
  `200`, and every route 404s, because endpoint registration threw and was swallowed. Do not treat a
  green readiness probe as evidence that the API exists — ask the server for a real route.
- **One root cause reported as hundreds of failures.** A single unresolvable package aborts an entire
  reconciliation cycle and every package is then listed as failed. Look for the *distinct* reason, not
  the list length.
- **A suite that hangs instead of failing.** Usually one of: a container-backed test waiting on an
  image; a readiness poll against a shell that will never activate; or a runner blocked on I/O rather
  than work. Check CPU — a stuck run is idle. Prefer per-suite timeouts over patience.
- **A stale local database after a schema change.** Old documents carry a schema version a newer build
  refuses to read (`GW-SCHEMA-*`). The tell is that the conflicts name document kinds your change never
  touched. Delete the local stores and redeploy the schema; this is local state, not a product defect.
- **Assertions that fail for environmental reasons.** Line endings, absent Docker, a port still held by
  a previous run. Establish *ours vs pre-existing* by comparison against a clean worktree of the merge
  base, never by argument.

## Establishing "is this mine?"

Do not classify a failure by reading it. Measure it:

```bash
git worktree add --detach ../baseline HEAD    # short path: long ones break on Windows
# run the same test projects there, then diff the failing-test-name sets
```

A failure present in both sets is inherited. A failure only in yours is yours. This takes minutes and
replaces an argument with a fact.

## If you disagree with what landed

`main` being the source of truth does not mean it is beyond challenge — it means the challenge is made
in the open and settled before the code follows.

1. Open an issue or FR describing the problem concretely, with the reproduction.
2. If the disagreement is architectural, challenge or amend the ADR. ADRs are proposals until merged;
   they can be superseded in part (see [ADR 0042](../adr/) for the partial-supersession convention).
3. Discuss with the contributors, developers and architects who own the decision.
4. Once the amended ADR is merged to `main`, it is the agreed truth and code follows it.

What is not acceptable is quietly working around an invariant on a branch. If you must bypass one to
make progress, say so explicitly on the PR (below) and link the issue that will resolve it properly.

## Recording out-of-scope work

Integration regularly forces changes outside the spec you are working on. Record them where the next
person will look — the PR is the primary surface, because that is what review reads:

- **A dedicated PR comment**, titled so it is findable, listing each out-of-scope change with: what
  broke, why it had to be fixed here, what was changed, and what remains owned elsewhere (link the
  issue).
- **The spec's `tasks.md`**, as numbered tasks with their evidence, so the work unit's own record is
  complete.
- **An issue** for anything you deliberately did *not* fix, so it is owned rather than forgotten.

State plainly when you disable or weaken something another work unit introduced, and leave its tests
intact wherever possible so the rule survives for whoever re-enables it.
