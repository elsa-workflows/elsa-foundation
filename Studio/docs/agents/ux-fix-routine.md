# UX fix routine

A scheduled routine runs this contract on weekdays. A user can also ask for one fixer pass. Each run starts in a fresh session on current `main`, does one bounded unit of work, and reports in its final message. The rules below are the routine's whole contract; the scheduled prompt only invokes the `studio-ux-fix` skill.

Its input is the issues that elsa-foundation's [UX QA routine](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/skills/catalog.md#ux-qa-routine) files here. That routine runs the Workbench and Studio in a browser twice a week. It files a reproduced defect whose fix needs no design choice as `ready-for-agent` + `auto-qa`. It files everything else as `needs-triage` for a maintainer. Each issue carries its reproduction as a journey script on `tools/ux-qa/lib.mjs` in elsa-foundation.

## Workflow

1. **Service open UX fix pull requests.** List open PRs whose head branch starts with `claude/ux-fix-`. For each one, fix a red check that the branch caused, and answer each review comment: fix it, or reply with the reason you are declining. A check that is red for a reason outside the branch is reported, not worked around.
2. **Stop at two.** If two `claude/ux-fix-*` PRs are still open after servicing, report them and start nothing new.
3. **Pick one issue.** Candidates are open issues labelled `ready-for-agent` and `auto-qa` with `size:S` or `size:M`. Exclude any issue that:
   - is labelled `size:L` or `status:in-progress`
   - carries a claim comment without a matching release: a later comment that names the same routine run and branch and says the work shipped or was dropped. A release for a different claim does not close this one.
   - is referenced by an open PR

   Prefer `size:S`, then the oldest issue. If nothing qualifies, report that and stop.
4. **Claim it.** Choose the branch name `claude/ux-fix-<issue number>-<short-slug>`. Post one comment on the issue that names the routine run, that branch and the exact scope, then add `status:in-progress`. Every later release comment names the same run and branch. Re-read the issue's comments in the same step. If another claim appeared, release yours and pick again.
5. **Reproduce first.** Branch from current `origin/main`. Before changing product code, turn the issue's reproduction into a test that fails:
   - a component or hook defect becomes a vitest test in the owning package;
   - a defect that needs a page becomes a `tests/browser/*.spec.ts` case on the fixture server.

   If neither can express it because it only shows against a real backend, run the issue's journey script against a source-built pair from an elsa-foundation checkout: `STUDIO_DIR=<this checkout> bash tools/ux-qa/setup.sh`. Confirm it fails. If you cannot reproduce the defect at all, say so on the issue with what you ran, release the claim, and stop without a PR.
6. **Fix the issue as written, and nothing else.** Then run:
   - `pnpm lint`
   - `pnpm typecheck`
   - `pnpm --recursive --workspace-concurrency=1 test`
   - `pnpm test:browser` for the specs you touched or added

   When the reproduction needed the real backend, rerun the journey script and confirm it passes. Never skip, disable or quarantine a test to get green. If the issue proves wrong, or larger than its size label, say so on the issue, release the claim, and stop.
7. **Open a ready (not draft) PR** with `Closes #<n>` in the body. Post the evidence as a PR comment: the commands, their results, the executed test counts, and the new test's red-then-green transition. Link the PR on the issue. A human merges every PR from this routine; do not merge.
8. **Report** the PRs serviced and their state, the issue picked (or why none), and the PR opened.

**Output:** at most one new ready PR per run, the serviced UX fix PRs, and a run report.
