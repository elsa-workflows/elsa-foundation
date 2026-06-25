# 0003. Extension Builder build and promotion source

## Status

Accepted

## Context

Extension Builder is a Git-repo-first online .NET solution editor. Users need fast build feedback while editing source files, but package promotion and runtime activation need traceable source provenance.

The main alternatives are building only committed revisions, building the current working tree, or introducing an internal source snapshot model for every build.

## Decision

Extension Builder builds may run from the current saved working tree, even when the Git repository has uncommitted changes. Promotion and package publish operations require a committed Git revision.

## Consequences

- Users can build quickly while editing without committing every experiment.
- Build records must indicate whether they were produced from a dirty working tree.
- Promoted packages remain traceable to a Git commit.
- The UI must distinguish local build feedback from releasable package artifacts.
