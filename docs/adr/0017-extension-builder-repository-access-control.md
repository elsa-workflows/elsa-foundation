# 0017. Extension Builder repository access control

## Status

Accepted

## Context

Extension Builder can create managed repositories, clone user-authorized remotes, and register existing server-local repositories. Server-local attach is powerful because it can expose files already present on the Elsa Server filesystem.

## Decision

Server-local repository registration is an administrative operation and is limited to configured allow-listed roots. Regular users can create managed repositories, clone repositories through their Git provider identity, and work in repositories they have been granted access to. Extension Builder does not expose a general-purpose server filesystem browser.

## Consequences

- Server-local repository attach can be supported without making Studio a filesystem escape surface.
- Repository access checks must be explicit in repository, working-copy, file, Git, build, and promotion APIs.
- Managed and cloned repositories remain the default non-admin paths.
- Configuration must expose repository storage roots and server-local attach allow-lists separately.
