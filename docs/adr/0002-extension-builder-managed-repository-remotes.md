# 0002. Extension Builder managed repository remotes

## Status

Accepted

## Context

Extension Builder managed repositories are initialized locally by Elsa Server and start with an automatic initial commit. Users can later connect the managed repository to their own Git remote.

Connecting a local managed repository to an existing remote can mean several different things: creating a new remote repository, attaching an empty remote, or reconciling with an existing non-empty remote history. Reconciling unrelated histories introduces merge, rebase, conflict, branch, and attribution behavior that does not belong in the default managed-repository flow.

## Decision

Managed repositories may connect to a newly created remote repository or an existing empty remote repository. If the target remote already has commits, Extension Builder blocks the connect-origin flow and directs the user to the clone-existing-repository flow instead.

## Consequences

- The default managed-repository flow remains predictable and Lovable-style: create locally, then publish to a new or empty remote.
- Existing repositories keep their history as the source of truth and are adopted through clone, not origin replacement.
- The UI must distinguish “Connect remote” from “Clone repository.”
- Backend remote checks must detect whether the target remote has existing commits before setting origin and pushing.
