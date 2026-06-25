# 0008. Extension Builder working-copy branch model

## Status

Accepted

## Context

Extension Builder workspaces are logical Git repository authoring spaces, while editing happens in per-user or per-session working copies. Without an explicit branch model, users can accidentally edit the default branch directly, and backend state becomes ambiguous when multiple users work in the same repository.

## Decision

Extension Builder defaults editing work to an explicit working branch. Managed repositories are initialized with a default branch such as `main`, but the first edit session creates or selects a working branch with an editable default name such as `work/{user}/{short-topic}`. For cloned or attached repositories, Studio shows the active branch and requires an explicit choice before editing a protected branch or common default branch such as `main` or `master`.

## Consequences

- Working-copy state is branch-scoped and can be shown clearly in the source-control inspector.
- Users can still intentionally edit default branches, but accidental default-branch edits are guarded.
- Backend APIs must represent the active branch per working copy.
- Branch creation and branch switching become first-class Extension Builder operations.
