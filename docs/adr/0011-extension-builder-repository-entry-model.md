# 0011. Extension Builder repository entry model

## Status

Accepted

## Context

Extension Builder is being redesigned around Git-backed repositories instead of package-first workspaces. Users need several legitimate entry paths: starting fresh, cloning a remote, attaching an existing server-local repository, or scaffolding from templates.

## Decision

The repository entry flow uses four primary options:

- `Create managed repo`: Elsa Server initializes a local Git repository and automatic starter commit.
- `Clone from Git`: Elsa Server creates a working repository from a user-authorized remote.
- `Open server-local repo`: Elsa Server registers an existing repository already available on the server filesystem.
- `Create from template`: Elsa Server scaffolds a repository, solution, or project from compatible templates.

Remote connection is optional during managed repository creation and can be added or changed later from Source Control.

## Consequences

- The first step of creation/import is repository identity, not project/package identity.
- Managed repositories support the Lovable-style path of starting locally and pushing to a user remote later.
- Template application needs to distinguish repository, solution, project, and item scopes.
- Server-local repository attach requires path allow-listing and clear administrative controls.
