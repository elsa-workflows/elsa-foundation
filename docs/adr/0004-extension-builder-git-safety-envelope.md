# 0004. Extension Builder Git safety envelope

## Status

Accepted

## Context

Extension Builder is a Git-repo-first online .NET solution editor. Users edit physical files in a server-side Git checkout, so source-control actions can affect real repository state and remote history.

The UI must support useful everyday Git workflows without exposing destructive or conflict-heavy operations before the product has the interaction model and safeguards to handle them well.

## Decision

Extension Builder v1 supports safe source-control operations: view status, view diff, stage, unstage, commit, create branch, push to a connected remote, pull only when the working tree is clean, and discard individual file changes with explicit confirmation.

Extension Builder v1 blocks or defers force push, hard reset, rebase, interactive conflict resolution, remote branch deletion, and pulling with a dirty working tree.

## Consequences

- The source-control UX remains understandable and avoids high-risk Git operations in the first implementation.
- Users can still complete the common edit, build, commit, and push loop.
- Advanced Git workflows must happen outside Extension Builder until explicit support is designed.
- The backend must enforce these limits; hiding controls in the UI is not sufficient.
