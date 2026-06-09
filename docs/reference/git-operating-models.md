# Git Operating Models

This catalog describes shared Git workflow shapes that agents and engineers can choose from. It is committed reference material, not a personal preference file.

Personal selections belong in `.agent-prefs/git-operating-model.md`, which is intentionally ignored by Git. If that file does not exist, ask the user which operating model they prefer before pushing, opening pull requests, or changing remotes.

## Model A - Fork PR

Use when the user does not have push rights to the upstream repository, or prefers fork isolation.

Default remote shape:

- `upstream` points to the source repository.
- `origin` points to the user's fork.

Default workflow:

1. Work on a branch such as `codex/<work-unit-name>`.
2. Commit coherent work-unit checkpoints locally.
3. Push the branch to `origin`.
4. Open a draft pull request from the fork branch to the upstream base branch.
5. Keep pushing follow-up commits to the same branch while the unit is active.

Do not push directly to `upstream` under this model.

## Model B - Organization Branch

Use when the user has push rights to the organization repository and wants branches to live directly there.

Default remote shape:

- `origin` may point directly to the organization repository.
- `upstream` may be absent or may also point to the organization repository.

Default workflow:

1. Work on a branch such as `codex/<work-unit-name>`.
2. Commit coherent work-unit checkpoints locally.
3. Push the branch to the organization repository.
4. Open a draft pull request from the organization branch to the base branch.

Before using this model, confirm the user wants direct organization branches.

## Model C - Local Checkpoint

Use when the user wants clean local history but no remote updates yet.

Default workflow:

1. Work on a branch such as `codex/<work-unit-name>`.
2. Commit coherent work-unit checkpoints locally.
3. Do not push unless the user explicitly asks.

This is useful for early exploration, private work, or when remote permissions are not configured.

## Model D - Patch Export

Use when the environment cannot push and the user needs to transfer work manually.

Default workflow:

1. Work on a branch such as `codex/<work-unit-name>`.
2. Commit coherent work-unit checkpoints locally.
3. Produce patches, diffs, or a bundle only when the user asks.

This model should stay a fallback. Prefer a real remote branch when possible.

## Personal Preference Template

Create `.agent-prefs/git-operating-model.md` locally with content like:

```md
# Git Operating Model Preference

Preferred model: fork-pr

upstream: https://github.com/elsa-workflows/elsa-foundation.git
origin: https://github.com/<user>/elsa-foundation.git
default branch prefix: codex/
use draft PRs: yes
commit style: coherent work-unit checkpoints
```

Only commit `.agent-prefs/.gitkeep`; never commit the personal preference file.
