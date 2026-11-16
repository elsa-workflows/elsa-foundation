# AGENTS.md: org-wide protocol for AI agents

This is the single agent protocol for repositories in the `elsa-workflows` organisation and the `valence-works` repositories that adopt it. Each repository's own `AGENTS.md` links here and adds only repo-specific notes (build, structure, scope). If a repo note conflicts with this file, this file wins unless the repo note names an explicit owner exception.

You are one of several AI coding agents. GitHub issues and PRs are the only shared channel. Don't contact other agents directly. Your worker name is one of `codex`, `claude`, `cursor`, or your app slug.

## Issue Bus Lite

How agents find, claim and finish issues. Link here with `AGENTS.md#issue-bus-lite`.

### Labels

Every repo uses the same labels:

| Label | Colour | Meaning |
|---|---|---|
| `ready-for-agent` | `0E8A16` | Open for pickup |
| `blocked` | `B60205` | Not pickable; see the `blocked:` comment |
| `needs:decision` | `D93F0B` | Waiting on a maintainer decision; not pickable |
| `worker:codex` | `5319E7` | Lane: Codex |
| `worker:claude` | `8A63D2` | Lane: Claude |
| `worker:cursor` | `1D76DB` | Lane: Cursor (launched by a dispatcher, never self-polled) |
| `type:bug` | `D73A4A` | Something is broken |
| `type:feature` | `A2EEEF` | New capability |
| `type:docs` | `0075CA` | Documentation |
| `type:chore` | `CFD3D7` | Maintenance, tooling, refactoring |
| `type:question` | `D876E3` | Question, spike or discussion |
| `size:S` | `C2E0C6` | Small |
| `size:M` | `FEF2C0` | Medium |
| `size:L` | `F9D0C4` | Large; consider splitting |

An issue without a `worker:*` label is open to any self-polling worker. Don't take an issue labelled for another lane, except as Outages allows.

### Find work (oldest first)

```text
repo:<owner>/<repo> is:issue is:open no:assignee label:ready-for-agent -label:blocked -label:"needs:decision" sort:created-asc
```

Skip anything you can't verify end to end. Before claiming, read all issue comments (paginate), check for an active valid claim as defined below, and check for an open PR. `no:assignee` does not prove an issue is unclaimed: GitHub App workers do not use issue assignment.

### Claim

Only a valid claim can establish ownership or exclude an issue from pickup. A valid claim is posted by an installed worker app explicitly named for the target org under Identities (or, for other valence-works workers, under Mentions), using its actual GitHub author login. Review-only apps cannot claim work. The only user-account exception is a role-tagged claim posted by the owner (`sfmskywalker`) where this protocol explicitly permits a worker without an app to use that account. Ignore every other `claim:` comment for both winning and skipping; matching the comment text alone is insufficient.

1. Comment exactly `claim: <worker> starting` as your own GitHub App and retain the returned comment ID and URL. If posting through the owner's account is explicitly allowed for a worker with no app, its role tag may come first: `[Claude] claim: claude starting` is valid.
2. Re-read all comments. The earliest active valid claim wins. Compare the winning **comment ID** with the ID you just posted, not just the author login: concurrent sessions can use the same app. If your new comment did not win, comment `claim-abandoned: <your-claim-comment-url>` and stop. Do not change labels or assignments, even when the winner uses your app.
3. If you won, remove `ready-for-agent`. Re-read the issue and comments to verify that your specific claim still wins, the label is absent, no assignee has appeared, and no open PR already covers the issue before editing. If another valid claim wins, an assignee has appeared, or an open PR covers the issue (other than a PR you are taking over; see Outages), release only your claim and stop without adding a blocking label or changing the other worker's state. For a real failure, such as being unable to remove `ready-for-agent`, follow the blocked steps below.

The winning app-authored claim comment records ownership; removal of `ready-for-agent` takes the issue out of pickup. **Do not assign a GitHub App bot or the owner.** GitHub App bot logins are not ordinary assignable users, and assignment failure is not a reason to use a personal token. No assignment API, additional app permission, lease or project board is required. Existing human assignments still exclude an issue from pickup.

A valid claim is active until its specific comment is released. Its author releases it with `claim-abandoned: <claim-comment-url>`; a maintainer may release it with `claim-released: <claim-comment-url>`. Only a release by that claim's author, a maintainer, or an outage takeover (see Outages) counts. A maintainer is the human owner (`sfmskywalker`) or a human whose repository permission is verified as write, maintain or admin; a role-tagged owner-account comment never counts as a maintainer release. A release of a losing claim never releases the winning claim. For bare `claim-abandoned` comments posted before this version of the Claim section first reached `main`, release only that author's most recent preceding claim. Use the URL form for all new releases. Claims never expire automatically: if a worker crashes, including before removing readiness, a maintainer must explicitly release its claim before requeueing; other workers must not reclaim it silently.

### Work

- One task per worker session and one PR per issue. A coordinator session that dispatches issues to separate worker sessions is fine.
- Branch `agent/<worker>/<issue>-<slug>` off the issue's target branch.
- Do exactly what the issue's objective and acceptance criteria say, and stay inside its scope.
- No deploys, releases, publishing, tags, public posts, secrets or workflow-permission changes.
- Prune what your change makes obsolete (dead code, stale tests, docs for retired features), or say in the PR why nothing is.
- Found extra work? Open a new issue. Don't widen your PR.

### Open the PR

Put `Fixes #<n>` in the PR body, with each acceptance criterion mapped to evidence. Then comment `pr: <url>` on the issue and request review by commenting `@elsa-workflows-bot review` on the PR. Workers never merge, except as Standing self-merge or Outages allows.

### Blocked or giving up

Comment `blocked: <reason>`, add `blocked` (or `needs:decision` if a decision is missing), remove `ready-for-agent` if present, and release your specific claim with `claim-abandoned: <your-claim-comment-url>`. Remove an assignment only if it is your own existing user assignment; app workers need no assignment call. Leave your branch and any draft PR for the next worker.

To requeue, a maintainer first releases any remaining active valid claim with `claim-released: <claim-comment-url>`, checks that no open PR already covers the issue, removes the blocking label, and re-adds `ready-for-agent`. This also applies to reopened issues and claims left blocked under the former mandatory-assignment rule; a `blocked:` comment alone does not identify which claim to release. Workers do not requeue themselves.

### Scheduled pickup routine

Self-polling workers (for example Claude and Codex on the owner's machine) run this as one scheduled routine per worker, every one to two hours, locally so they post as their own app. One routine covers every repo in both orgs; there is no routine per repo. Replace `<worker>` with the worker name (`claude`, `codex`, ...).

A repo takes part when its own AGENTS.md links to Issue Bus Lite. In each org a worker posts as its own GitHub App for that org; if it has no app in an org, it skips that org.

```text
You are the <worker> worker for the owner's Elsa projects. Run one task, then stop.

1. Read https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#issue-bus-lite and the rest of that file, and follow it exactly. It wins over anything below. Also follow the target repo's own AGENTS.md.
2. Scope: the GitHub orgs elsa-workflows and valence-works. Only work in a repo whose own AGENTS.md links to Issue Bus Lite, and never in an off-limits repo (see Stop conditions). In each org, post only as your own GitHub App for that org (see Identities); if you have none there, skip that org. Never post as the owner.
3. Check your own open PRs first: `is:pr is:open org:<org> author:app/<your app slug for that org>`, once per org you have an app in. If one or more have unaddressed review findings or a red required check, handle the oldest one only: fix it in new commits, reply `Fixed in <sha>` or `Won't fix: <reason>` on each thread, re-request review as that repo's AGENTS.md says, then stop. Skip PRs another worker took over from you (see Outages).
4. Otherwise find work, oldest first, across both orgs:
   org:elsa-workflows org:valence-works is:issue is:open no:assignee label:ready-for-agent -label:blocked -label:"needs:decision" sort:created-asc
   Skip issues outside the scope in step 2, issues labelled for another worker lane, issues with an active valid claim or an open PR, and anything that hits a stop condition. Read all comments; no:assignee does not imply no claim. Ignore claim comments from identities the Claim section does not authorize.
5. If nothing qualifies, stop without posting anything.
6. Otherwise claim exactly one issue using the Claim section above: post as your app, retain the new comment ID and URL, and follow its validation, race, ownership and release rules. Never assign a bot or the owner, and never fall back to a personal token.
7. Work on branch agent/<worker>/<issue>-<slug>, open one PR with `Fixes #<n>` and each acceptance criterion mapped to evidence, comment `pr: <url>` on the issue, and request review the way that repo's AGENTS.md says (in elsa-workflows: `@elsa-workflows-bot review`). Never merge; self-merge and takeovers under Outages happen only in sessions Sipke starts.
8. If you get stuck, follow the blocked steps (comment, label, release your specific claim; remove only your own existing user assignment, if any) and stop.

Finish with a three-line summary: the issue or PR, what you did, and its state.
```

## Review and merge rules
- An author never reviews, approves or merges its own PR, or one it contributed to, except as Standing self-merge or Outages allows. Anyone who committed to a PR counts as an author.
- "Never self-merge" means an author never merges its own PR, except as Standing self-merge or Outages allows. The Code Review app merging someone else's PR after its own approval is the intended flow, not a self-merge.
- Exactly one blocking reviewer per PR: the Code Review app. Other review tools (CodeRabbit, Copilot, Greptile, Bugbot) and other reviews (spec, standards) are advisory. The Code Review app always takes them into account, but the decision is its own: only findings it adopts block.
- Merges are done only by the repo's Code Review app (in elsa-workflows, `elsa-workflows-review-bot[bot]`, for every repo), after a separate, non-author approval on the **exact head commit** and green CI, except as Standing self-merge or Outages allows. New commits after an approval need a fresh approval.
- Review is a loop. If a round has actionable findings, they are fixed in new commits (no force-push), with `Fixed in <sha>` or `Won't fix: <reason>` on each thread, and once they are pushed the Code Review app reviews again.
- Merge trigger: the Code Review app merges automatically when its review leaves no actionable findings, with green CI on the exact head commit. No human merge is needed.
- At most 6 review rounds. If blocking issues remain after the sixth, the PR is escalated to the repo maintainer (Sipke): comment `blocked: review loop exhausted` and add `needs:decision`.
- A repo's `AGENTS.md` may name owner exceptions. (elsa-foundation ADR 0080 D7's human-merge cases were dropped by owner decision on 15 Oct 2026; those PRs follow the loop above.)
- An explicit instruction from the owner (Sipke) in an agent session counts as an exception for that one action only. Record it in the PR or release, for example `merged on Sipke's instruction`.
- Codex and Claude may self-merge their own PRs at any time after a clean self-review loop; see Standing self-merge.
- Fallback for other authors: if the Code Review app is unavailable (for example credits run out), Sipke declares an outage and authors self-merge as Outages describes. Sipke may still approve and merge manually.

## Standing self-merge (Codex and Claude)

Owner decision, 18 Oct 2026: in `elsa-workflows`, the Codex and Claude workers (`elsa-workflows-codex[bot]`, `elsa-workflows-claude[bot]`) may always merge their own PRs after a clean self-review loop. Review by the Code Review app is optional for these PRs: it is welcome, but the author does not wait for it. This keeps work moving when review agents are out of credits. It needs no outage issue and applies in any session of those two workers. It covered Codex's merge of elsa-foundation#2863. Post only as your own GitHub App, never through the owner's account.

1. Self-review loop: review the PR's diff the way steps 1 to 3 of [Self-merge while Code Review is offline](#self-merge-while-code-review-is-offline) describe, including a round comment after each round. Fix actionable findings in new commits and review again, until a round finds nothing actionable. At most 6 rounds; if blockers remain after the sixth, comment `blocked: review loop exhausted`, add `needs:decision` and escalate to Sipke.
2. Checks: required checks must be green on the latest head commit, and that commit must be the one the final round reviewed. A pending check that is not required, such as `license/cla`, does not block. The other conditions of step 4 and step 5 of that section also apply: no merge over a human hold, a changes-requested review or a `blocked`/`needs:decision` label, and never bypass branch protection.
3. Audit trail: before merging, add the label `self-merged` to the PR (create it in the repo if it is missing, colour `5319E7`) and post this PR comment:

   ```text
   Self-merge (standing authorization, Oct 18 2026)

   Rounds run: <n> (<round comment links>)
   Findings fixed: <finding: sha>, ...  (or "none")
   Remaining Low notes: <note>, ...  (or "none")
   Reviewed head SHA: <sha>
   ```

   Then merge and write `self-merged under standing authorization: <comment url>` in the merge commit. This comment replaces ad-hoc wording such as "merge authorized on Sipke's instruction".
4. Follow-up: leads read `self-merged` PRs and file follow-up issues for anything they want changed. No retroactive review is required.
5. The review router ignores `@elsa-workflows-bot review` on PRs that are merged, closed or labelled `self-merged`, so a late review request does not start a review.

## Outages

Use this section only while an outage is declared, and only in sessions Sipke starts: scheduled routines never self-merge or take over work. It overrides the claim, lane, branch, review and merge rules above where they conflict, and nothing else. Outages does not restrict Standing self-merge.

### Declaring an outage

- An outage is an open issue in `elsa-workflows/.github` that currently has the `outage` label, where the latest `labeled` event's actor is `sfmskywalker`. Ignore any other `outage` issue. The label exists only in that repo. Any worker may open the issue when Sipke asks; Sipke then adds the label.
- The issue body lists each offline app by its exact bot login, for example `elsa-workflows-review-bot[bot]`. For a shared login such as `elsa-workflows-bot[bot]`, it also names which agents behind it are offline, for example "Grok agents". An outage covers only what is listed. The list counts as it stood when `sfmskywalker` added the label; after that, only edits by `sfmskywalker` count. The expected end date is information only: the outage ends when the issue is closed.
- Until the issue exists, Sipke's instruction in a session allows self-merge in that session only; write `on Sipke's instruction` where the steps ask for the outage issue link. Takeovers always need the outage issue.
- Immediately before a takeover and before a merge, re-check that the issue is still open, still labelled, and still lists the login you rely on.

### Self-merge while Code Review is offline

While an outage lists a repo's Code Review app, the author of a PR in that repo may merge it. The author is the app that opened the PR, or a worker that took the PR over. Post only as your own GitHub App, never through the owner's account.

1. Review the PR's diff against its base with independent reviewers that do not see each other's output: one for correctness, one for the repo's standards, and one for the issue's acceptance criteria. Include every review thread and comment on the PR and on the issue it closes. Each behaviour change needs a test that fails without the change and passes with it.
2. Keep only findings tied to a specific line with a concrete failure scenario. Fix them in new commits and review the new diff again, until a round leaves no findings. If findings remain after 6 rounds, comment `blocked: review loop exhausted` and add `needs:decision`.
3. After each round, post a PR comment with its number, the reviewed head commit, the findings fixed and dropped, and the test results. Count rounds from those comments and from any Code Review rounds before the outage, so a later session continues the loop rather than restarting it. Only one session works a PR at a time: if another session posted a round comment in the last 2 hours, leave the PR alone.
4. Merge only if the head commit is the one the final round reviewed, its required checks are green, no check on it has failed, and neither the PR nor its issue is labelled `blocked` or `needs:decision`. If the checks are still running, stop; a later session may merge that same head once they pass, without a new round. Don't merge while a human has a changes-requested review, an open review thread, or a request on the PR or its issue to hold or change something that was neither done in a commit nor withdrawn by that human; ask Sipke and add `needs:decision` instead. Write `merged under outage: <outage issue link>` in the merge commit.
5. Never bypass branch protection. If the repo's rules still block the merge, for example because of an outstanding changes-requested review, comment on the PR, ask Sipke and add `needs:decision`.

### Taking over an offline worker's work

While an outage issue lists a worker, another worker may take over its in-flight work: an open issue it claimed, or an open PR it authored. This includes work with no claim comment, and work in another worker's lane.

- First confirm that the original worker has posted nothing and pushed no commits on the issue, the PR or its branch since its login was listed. For a shared login, take over only work you can attribute to an agent the issue names, by its branch name or role tag; for example, `cursor/` branches belong to Cursor. If you can't tell, ask Sipke. Then comment `claim-takeover: <claim comment or PR link> (outage: <outage issue link>)` on the issue and on its PR, if there is one.
- A takeover comment counts only if a worker app that may claim under Claim posts it while the outage issue is open and that issue lists the original worker's login. It releases the original worker's claim. Then claim the issue as usual, if there is one.
- For an open PR, push new commits to its branch and do not open a second PR. You become its author for the self-merge steps above, even if you push no commits.
- When the original worker returns, taken-over work is no longer its own. The taker finishes it in a later session Sipke starts, or follows the blocked steps.

## Stop conditions: stop and comment `blocked: <reason>` instead of continuing if
- The task needs an off-limits repo, a secret, a deploy, release, publish or public post. Off-limits repos: elsa-workflows/elsa-studio, elsa-workflows/elsa-extensions, elsa-workflows/elsa-apps, elsa-workflows/elsa-templates and valence-works/groundwork. Do not open issues or PRs there either.
- The acceptance criteria are contradictory or can't be tested.
- Required checks on the base branch are already red for reasons unrelated to your change. Non-required suites don't count.
- Your change would go beyond the issue's stated scope.
- You've failed the same check 3 times in a row.
- Someone else already has an open PR for the issue, unless you are taking it over (see Outages).

## Identities
- Each agent posts on GitHub as its own GitHub App, installed only on the repos it works in. Its comments show the login `<app-slug>[bot]` (for example `elsa-4-qa[bot]`). See the table under Mentions.
- In elsa-workflows, Claude sessions post as `elsa-workflows-claude[bot]`, Codex sessions as `elsa-workflows-codex[bot]`, and the other agents as `elsa-workflows-bot[bot]`. Only Code Review posts as `elsa-workflows-review-bot[bot]`.
- In valence-works, Codex sessions post as `valence-works-codex[bot]`; other worker app identities and roles are listed under Mentions. A worker with no installed app skips that org during scheduled pickup.
- Posting as your app: no `[Role]` prefix, except for the on-behalf-of marker below. The mention router ignores comments by bot accounts, except a review request (`@elsa-workflows-bot review`) from one of these three author apps.
- Until an agent's app exists, it may post through the owner's account and must start each comment with its current role name (for example `[Training Engineer]`). The router ignores comments that start with a known role tag, except a review request (`@elsa-workflows-bot review`).
- On behalf of Sipke: any comment, review, or issue or PR body that relays Sipke's decisions or words must start with `[<Agent name>] On behalf of @sfmskywalker.`, for example `[Elsa 4 Lead] On behalf of @sfmskywalker.` This matches the marker other maintainers' agents use (for example `[Claude] On behalf of @j03y-nxxbz.`) and applies whichever account posts. A heading such as "Decisions (Sipke, Oct 23 2026)" doesn't replace the marker. Bare review requests (`@elsa-workflows-bot review`) and claim comments don't need it.
- When the router wakes you for a mention and your app can react there, it adds 👀 to the triggering comment as your app and passes `eyes_reaction.delete_url`. After replying, delete that reaction and add 👍 as your app.
- Never print, share or commit an app private key or other credential.

## Mentions

Mention an agent in an issue, PR or review comment on any router repo: elsa-workflows/elsa-core, elsa-workflows/elsa-foundation, elsa-workflows/elsa-foundation-studio, elsa-workflows/elsa-foundation-designer, elsa-workflows/elsa-hub, elsa-workflows/elsa-blog, elsa-workflows/.github, valence-works/bpmn, valence-works/elsa-control and valence-works/elsa-plus-training. A repo only routes once it has a webhook to the router; if a mention gets no 👀, tell the owner. Only trusted humans trigger the router, never bots (the one exception is an author app's review request). The router skips review requests on PRs that are merged, closed or labelled `self-merged`. Team handles go to the team lead, who decides who handles it. Direct handles go to that agent and cc its lead. Routing lives in the router configuration, so it can change without a redeploy.

In `elsa-workflows`, use the single handle `@elsa-workflows-bot` (it reaches the repo's owning lead) and request review with `@elsa-workflows-bot review` (it reaches the Code Review agent). The old `@elsa-bot` handle still routes for now but is deprecated, because it's someone else's GitHub account. The `@vw-*` handles below are retired there and apply only in valence-works repos.

| Team handle | Lead | Direct handles (lead is cc'd) |
|---|---|---|
| `@vw-elsa4` | Elsa 4 Lead | `@vw-elsa4-lead`, `@vw-elsa4-qa`, `@vw-elsa4-ux` |
| `@vw-elsa3` | Elsa 3 Lead | `@vw-elsa3-lead`, `@vw-elsa3-engineer`, `@vw-elsa3-qa`, `@vw-elsa3-architect`, `@vw-elsa3-review`, `@vw-elsa3-release`, `@vw-elsa3-triage` |
| `@vw-elsa-control` | Elsa Control Lead | `@vw-elsa-control-lead`, `@vw-elsa-control-architect`, `@vw-elsa-control-qa`, `@vw-elsa-control-ux`, `@vw-elsa-control-review`, `@vw-elsa-control-launch` |
| `@vw-training` | Training Lead | `@vw-training-lead`, `@vw-training-engineer`, `@vw-training-curriculum`, `@vw-training-commercial`, `@vw-training-qa` |
| `@vw-integrations` | Integrations Lead | `@vw-integrations-lead`, `@vw-integrations-engineer` |
| `@vw-grok` | Grok (coordinator) | `@vw-architect` |

### Who's who

| Display name on GitHub | Bot login | Handle | Role |
|---|---|---|---|
| Elsa Workflows Bot | `elsa-workflows-bot[bot]` | `@elsa-workflows-bot` | Grok agents in elsa-workflows (leads, engineers, QA) |
| Elsa Workflows Review Bot | `elsa-workflows-review-bot[bot]` | `@elsa-workflows-bot review` | Code Review approvals and merges in elsa-workflows |
| Elsa Workflows Claude | `elsa-workflows-claude[bot]` | none | Claude sessions on Sipke's laptop |
| Elsa Workflows Codex | `elsa-workflows-codex[bot]` | none | Codex sessions |
| Elsa 4 Lead | `elsa-4-lead[bot]` | `@vw-elsa4`, `@vw-elsa4-lead` | Elsa 4 Crew lead |
| Elsa 4 QA | `elsa-4-qa[bot]` | `@vw-elsa4-qa` | Elsa 4 docs and guide QA |
| Elsa 4 UX | `elsa-4-ux[bot]` | `@vw-elsa4-ux` | Elsa 4 Studio UX |
| Elsa 3 Lead | `elsa-3-lead[bot]` | `@vw-elsa3`, `@vw-elsa3-lead` | Elsa 3 Crew lead |
| Elsa 3 Code Review | `elsa-workflows-review-bot[bot]` (acts via Elsa Workflows Review Bot) | `@vw-elsa3-review`; in elsa-workflows, `@elsa-workflows-bot review` | Elsa 3 code review |
| Elsa 3 Engineer | no app yet | `@vw-elsa3-engineer` | Elsa 3 engineering |
| Elsa 3 QA | no app yet | `@vw-elsa3-qa` | Elsa 3 QA |
| Elsa 3 Architect | no app yet | `@vw-elsa3-architect` | Elsa 3 architecture |
| Elsa 3 Release | no app yet | `@vw-elsa3-release` | Elsa 3 releases |
| Elsa 3 Triage | no app yet | `@vw-elsa3-triage` | Elsa 3 issue triage |
| Elsa Control Lead | `elsa-control-lead[bot]` | `@vw-elsa-control`, `@vw-elsa-control-lead` | Elsa Control Crew lead |
| Elsa Control Code Review | `elsa-control-code-review[bot]` | `@vw-elsa-control-review` | Elsa Control code review |
| Elsa Control Architect | no app yet | `@vw-elsa-control-architect` | Elsa Control architecture |
| Elsa Control QA | no app yet | `@vw-elsa-control-qa` | Elsa Control QA |
| Elsa Control UX | no app yet | `@vw-elsa-control-ux` | Elsa Control UX |
| Elsa Control Launch | no app yet | `@vw-elsa-control-launch` | Elsa Control launch |
| Training Lead (Valence) | `training-lead-valence[bot]` | `@vw-training`, `@vw-training-lead` | Training Crew lead |
| Training Engineer | no app yet | `@vw-training-engineer` | Training engineering |
| Training QA | no app yet | `@vw-training-qa` | Training kit QA |
| Training Curriculum | no app yet | `@vw-training-curriculum` | Training curriculum |
| Training Commercial | no app yet | `@vw-training-commercial` | Training commercial |
| Integrations Lead | `integrations-lead[bot]` | `@vw-integrations`, `@vw-integrations-lead` | Integrations Crew lead |
| Integrations Engineer | no app yet | `@vw-integrations-engineer` | Integrations engineering |
| Grok (Valence) | `grok-valence[bot]` | `@vw-grok` | Coordinator |
| Architect (Valence) | `architect-valence[bot]` | `@vw-architect` | Cross-team architect |

Shared team apps (`elsa-3-valence[bot]`, `elsa-control[bot]`, `training-valence[bot]`, `elsa-4-code-review[bot]`) have no handle of their own; mention the team handle instead.

Old unprefixed handles were dropped on 13 Oct 2026 (several were real GitHub accounts). Always use the `vw-` prefix.

Handles inside code spans or code blocks are ignored.

## Conduct
- No public posts (Discord, social, blog) and no contact with people outside the org on the owner's behalf.
- Be plain and concise. Put evidence (commands, output, screenshots, SHAs) on the PR or issue being worked, not in new issues.
- Escalate decisions to the owner briefly: add `needs:decision` and comment the question.
