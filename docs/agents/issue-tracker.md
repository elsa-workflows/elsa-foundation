# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --comments`, filtering comments by `jq` and also fetching labels.
- **List issues**: `gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

Infer the repo from `git remote -v` - `gh` does this automatically when run inside a clone.

## Claiming a unit before working on it

Claims follow [Issue Bus Lite](https://github.com/elsa-workflows/.github/blob/main/AGENTS.md#claim). See "Concurrent work claims" in [AGENTS.md](../../AGENTS.md#concurrent-work-claims) and [Claims](../contributing/repository-hygiene.md#claims) for the steps.

- **Check for an existing claim**: `gh issue view <number> --comments`, then list the PRs whose branch names the issue:

  ```bash
  gh pr list --state all --limit 20 --json number,title,state,headRefName,baseRefName \
    --jq '.[] | select(.headRefName|test("<number>")) | "#\(.number) [\(.state)] \(.headRefName) -> \(.baseRefName): \(.title)"'
  ```

  An open PR whose branch names the issue is a claim even when no comment exists, and it is usually the fresher signal of the two. A branch name that collides with the one you were about to create is also evidence: stop and inspect before forcing it.

- **Post a claim**: `gh issue comment <number> --body "claim: <worker> starting"`. Posting as your agent's app needs no prefix. Only when you post through the owner's account does your role tag come first: `--body "[Claude] claim: claude starting"`.
- **Re-read and settle it**: `gh issue view <number> --comments`. If the earliest unabandoned claim is not yours, comment `claim-abandoned` and stop. If it is yours: `gh issue edit <number> --add-assignee @me --remove-label ready-for-agent`.
- **Give it up**: comment `blocked: <reason>`, then `gh issue edit <number> --add-label blocked --remove-assignee @me` (use `needs:decision` instead of `blocked` when a decision is missing).

## Keeping an issue current

An issue in a program is the record of progress, so comment at each transition rather than only at the end. See [Program bookkeeping](../contributing/merging.md#program-bookkeeping).

- Implementation started: the claim comment.
- PR opened: `pr: <url>`, then request review on the PR with `@elsa-workflows-bot review`.
- Each review round: what was raised and how it resolved.
- Merged or closed: the outcome, and the gate evidence if it is not already on the PR.

Keep the program's project board Status and any labels in sync in the same pass. Each program has its own board, so resolve it with `gh project list --owner <owner>` rather than assuming a fixed number.

## Pull requests as a triage surface

**PRs as a request surface: no.** This flag tells `/triage` whether to treat an external PR itself as a feature request and apply issue-triage conventions to it; set it to `yes` to do that. It does not mean fixes are unwelcome as PRs: small fixes may come directly by PR, while substantial feature requests should start in Issues for scope alignment. See [CONTRIBUTING.md](../../CONTRIBUTING.md).

When set to `yes`, PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view <number> --comments` and `gh pr diff <number>` for the diff.
- **List external PRs for triage**: `gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments` then keep only `authorAssociation` of `CONTRIBUTOR`, `FIRST_TIME_CONTRIBUTOR`, or `NONE` (drop `OWNER`/`MEMBER`/`COLLABORATOR`).
- **Comment / label / close**: `gh pr comment`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either - resolve with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.
