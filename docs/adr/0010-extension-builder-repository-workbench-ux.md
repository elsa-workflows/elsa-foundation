# 0010. Extension Builder repository workbench UX

## Status

Accepted

## Context

The earlier Extension Builder UX mixed workspace creation forms, project creation forms, file navigation, build details, and runtime state on one page. The redesigned module is Git-repo-first and should behave like an online .NET repository editor rather than a package-management form.

## Decision

Extension Builder opens as a repository workbench. The primary left column is a compact repository list with branch, dirty state, and build indicators. Selecting a repository opens its active working copy, solution explorer, and editor immediately. Creation and import flows are secondary actions behind a single `New or Clone` command rather than always-visible forms.

## Consequences

- Returning users land directly in authoring context.
- Workspace/project creation forms no longer dominate the steady-state experience.
- The UX needs clear empty states for no repositories, no selected working copy, and no solution files.
- Repository health indicators become part of the navigation surface.
