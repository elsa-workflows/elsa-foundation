# 0018. Extension Builder managed repository initial commit

## Status

Accepted

## Context

Managed repositories are created by Elsa Server before the user connects a remote. Without an initial commit, generated starter files and user edits are mixed together in the first source-control view.

## Decision

Extension Builder creates an automatic initial commit for managed repositories after scaffolding selected starter content such as `.gitignore`, `README.md`, solution files, project files, and template-provided source files. The commit is machine-authored with clear metadata so user edits begin from a clean Git baseline.

## Consequences

- The source-control inspector can show only user changes after creation.
- Managed repositories have valid commit history before a remote is connected.
- Template generation failures must abort before the initial commit is created.
- The machine author identity for initial commits must be configurable or clearly documented.
