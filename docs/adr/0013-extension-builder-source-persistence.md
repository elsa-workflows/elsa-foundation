# 0013. Extension Builder source persistence

## Status

Accepted

## Context

The earlier Extension Builder model could store generated project/file state as application data. The redesigned module is Git-repo-first, which means source files need a clear authoritative location.

## Decision

Extension Builder stores repository/workspace metadata, working-copy metadata, build job records, template catalog references, and package promotion history in application persistence. It does not use database file blobs as the source of truth for source code. Source files live in the Git working tree, and Git commits provide durable source history.

## Consequences

- File read/write APIs operate against checked-out repository files.
- Database state can index and describe repositories but cannot become an alternate source tree.
- Backup and migration guidance must include both application data and repository storage.
- Git history, not database revision tables, is the durable source-code audit trail.
