# 0001. Extension Builder Git identity

## Status

Accepted

## Context

Extension Builder is being reshaped into a Git-repo-first online .NET solution editor. A workspace is one checked-out Git repository. The default workspace creation mode lets Elsa Server initialize a local managed repository first, then lets the user connect or change the remote origin and push to their own repository later.

Remote Git operations need an identity and credential owner. The main choices are user-owned provider authorization, a server-owned bot identity, per-workspace raw credentials, or no server-side push support.

## Decision

Extension Builder remote Git operations use user-owned Git provider authorization as the primary UX. A server-owned bot identity may be configured as an administrator-managed fallback.

Per-workspace raw tokens or deploy keys are not the preferred UX path and should be avoided unless a later integration scenario explicitly requires them.

## Consequences

- Commits and pushes can be attributed to the user who initiated them.
- Elsa Server must support secure provider authorization, token storage, revocation, and permission checks before remote Git operations.
- Administrators can still enable a bot/service identity for environments where user OAuth is unavailable or undesirable.
- The UI must make the active Git actor visible before push operations.
