# 0014. Extension Builder module and package boundary

## Status

Accepted

## Context

Extension Builder creates and edits .NET source repositories that can produce Elsa extension packages or runtime contributions. The Modules and Package Feeds areas already have their own responsibilities for package discovery, package state, and module availability.

## Decision

Extension Builder's primary domain is repository authoring. Package and module visibility are downstream results of explicit pack, promote, publish, install, or catalog-refresh operations. The repository editor shows produced packages and runtime contribution status as artifacts and diagnostics, but it does not redefine source projects as loaded modules.

## Consequences

- Source projects are not considered modules merely because they are referenced by the server solution.
- The Modules page remains the place to inspect package/module availability and runtime loading state.
- Extension Builder can link to downstream package/feed/module records without owning their lifecycle.
- Promotion flows must bridge from repository artifacts to package-feed/module-catalog behavior explicitly.
