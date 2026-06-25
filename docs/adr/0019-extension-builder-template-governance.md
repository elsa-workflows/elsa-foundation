# 0019. Extension Builder template governance

## Status

Accepted

## Context

Extension Builder templates can generate repositories, solutions, projects, and item files. If templates are arbitrary uploads, they become another code-ingestion and execution surface.

## Decision

Extension Builder v1 treats templates as cataloged extension contributions. Templates can come from trusted built-in packages or installed module packages. Each template contribution includes manifest metadata describing scope, parameters, generated files, compatibility, and presentation details.

Arbitrary uploaded template archives are out of scope for v1.

## Consequences

- Template discovery can use the same contribution/catalog patterns as other extension surfaces.
- Template compatibility can be shown before users apply a template.
- Installed modules can extend Extension Builder without bypassing package governance.
- User-uploaded template support remains a separate future design.
