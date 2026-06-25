# 0016. Extension Builder conflict handling in v1

## Status

Accepted

## Context

Extension Builder v1 supports guarded Git operations in an online editor. Full interactive merge conflict resolution requires additional UI, file-state modeling, Git index handling, and recovery design.

## Decision

Extension Builder v1 does not provide inline merge conflict resolution. If pull, push, branch switch, or related Git operations detect divergence or conflict risk, Studio stops the operation, explains the repository state, shows affected files when available, and offers safe exits such as committing current work, switching branch when possible, or resolving outside Studio. Interactive merge tooling is deferred to a separate roadmap item.

## Consequences

- V1 avoids exposing partially merged working copies through an editor that cannot recover them well.
- Git operations must preflight dirty state, branch state, and divergence before mutating the working copy.
- Error messages need to be precise enough for users to recover with external Git tools.
- Inline conflict resolution remains a future capability, not an implied part of guarded pull.
