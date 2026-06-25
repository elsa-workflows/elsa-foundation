# 0007. Extension Builder collaboration through working copies

## Status

Accepted

## Context

Extension Builder workspaces are Git-backed authoring spaces. Multiple users may need to work in the same logical repository through Studio. A single shared checkout would require locks or conflict-heavy save behavior, while separate workspaces per user would fragment the repository concept in the UI.

## Decision

Extension Builder models a workspace as the logical Git repository authoring space and uses separate server-side working copies for user/session/branch editing. Users collaborate through Git branches, commits, pushes, pulls, and provider workflows rather than by concurrently editing the same physical checkout.

## Consequences

- The UI can present one shared workspace while keeping file edits isolated per user or branch.
- Backend storage must distinguish workspace metadata from working-copy checkouts.
- Git status, branch, dirty state, build output, and editor tabs are scoped to the active working copy.
- Cross-user collaboration relies on Git operations rather than file locks in v1.
