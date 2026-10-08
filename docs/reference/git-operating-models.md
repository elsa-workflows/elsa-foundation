# Git Operating Models

This reference describes the publication paths defined in [AGENTS.md](../../AGENTS.md#personal-operating-preferences). They apply to engineers and agents using any tool; no personal Git preference file or workflow selection is required before publication.

## Maintainers - Organization Branch

Maintainer work, including Sipke’s sessions, always uses feature/work-unit branches in the organization repository. Never create a fork PR for maintainer work.

1. Check the existing remotes and use the remote pointing to the organization repository. Do not assume `origin` identifies the correct repository or rewrite remotes just to match an example.
2. Work on a branch such as `codex/<work-unit-name>`.
3. Commit coherent work-unit checkpoints locally.
4. Push the branch to the organization repository.
5. Open a draft pull request from that branch to the appropriate organization base branch.
6. Keep follow-up commits on the same branch while the unit is active.

The maintainer path is already selected by repository policy. If organization push access is unavailable, report the access problem and preserve the local work; do not switch to a fork PR.

## Contributors - Fork PR

Contributors without organization write access use a fork. Infer this path from the session context and repository access without asking them to choose an operating model. See [the contributor guide](../../CONTRIBUTING.md#create-a-topic-branch).

1. Check existing remotes and use or configure a remote pointing to the contributor’s fork, preserving the upstream remote.
2. Work on a topic branch based on the appropriate upstream base branch.
3. Commit coherent work-unit checkpoints locally.
4. Push the branch to the fork remote.
5. Open a draft pull request from the fork branch to the upstream base branch.
6. Keep follow-up commits on the same branch while the unit is active.

Contributors with organization write access can use the organization-branch workflow. Do not infer maintainer identity solely from the checkout’s `origin` URL: contributors can clone upstream too.

## Local Checkpoints and Patch Export

When the user requests local-only work, commit locally and leave publication for later. When remote publication is unavailable, preserve local commits and report the concrete blocker. Produce patches, diffs, or a bundle when requested. These are delivery states, not additional operating-model choices required before publication.

## Optional Local Git Details

An ignored `.agent-prefs/git-operating-model.md` may record remote names, branch naming, or other local details. It must respect the maintainer/contributor paths above and is never required for publication. For a maintainer checkout, an example is:

```md
# Git Operating Model Preference

Role: maintainer
Preferred model: organization-branch

organization remote: origin
organization repository: https://github.com/elsa-workflows/elsa-foundation.git
default branch prefix: codex/
use draft PRs: yes
commit style: coherent work-unit checkpoints
```

Only commit `.agent-prefs/.gitkeep`; never commit the personal preference file.
