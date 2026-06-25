# 0005. Extension Builder pack and promote scope

## Status

Accepted

## Context

Extension Builder is shifting from a package-project generator to a Git-repo-first online .NET solution editor. In the new model, projects are .NET project files and package identity is output metadata. A workspace can contain multiple solutions and projects, and a solution may contain multiple packable projects.

The product still needs package and promotion workflows for Elsa extension delivery, but those operations must not collapse the authoring model back into “one project equals one package workspace.”

## Decision

Extension Builder supports project-level and solution-level pack/promotion operations. V1 should expose explicit commands such as `Pack project`, `Pack solution`, `Promote package`, and `Promote solution packages`.

Workspace-level promotion remains deferred until an explicit release-group or manifest model exists.

## Consequences

- Users can package individual projects or coordinated solution outputs.
- The UI avoids ambiguous workspace-level release semantics.
- Future release manifests can add richer grouping without invalidating project and solution commands.
- Promotion still requires committed Git provenance as defined by ADR 0003.
