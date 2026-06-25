# 0015. Extension Builder API capability split

## Status

Accepted

## Context

The Git-repo-first Extension Builder model covers repository registration, working-copy lifecycle, file editing, Git operations, build execution, template application, and package promotion. A single project-centric endpoint family would recreate the earlier blurred model where workspaces, projects, packages, and runtime state were difficult to distinguish.

## Decision

Extension Builder backend contracts are split by capability:

- Repositories: logical workspace registration, metadata, remotes, and repository health.
- Working copies: per-user/session/branch checkout lifecycle and active branch state.
- Files: repository-rooted file tree, file reads, saves, creates, renames, deletes, and file diagnostics.
- Git: status, diffs, staging, commits, branch creation, guarded pulls, pushes, and remote connection.
- Builds: restore, build, test, pack jobs, logs, diagnostics, cancellation, and artifacts.
- Templates: repository, solution, project, and item template discovery and application.
- Artifacts and promotions: package outputs, promotion records, feed publishing, and downstream module/catalog links.

## Consequences

- API names reflect domain capabilities rather than a generic `project` surface.
- Each capability can be tested and evolved independently.
- The frontend can compose repository workbench views without requiring a monolithic DTO.
- Cross-capability workflows, such as pack then promote, must be modeled explicitly.
