# 0009. Extension Builder build execution boundary

## Status

Accepted

## Context

Extension Builder edits physical .NET repositories and must support restore, build, test, and pack operations. Those operations run user-authored code paths through SDK targets, analyzers, generators, test frameworks, and package restore hooks. Running them in the Elsa Server process would blur authoring, execution, and hosting responsibilities.

## Decision

Extension Builder treats repository code as untrusted for build execution. Restore, build, test, and pack operations run outside the Elsa Server host process in an isolated worker boundary with configured workspace roots, timeouts, cancellation, log streaming, and explicit result capture. Build success does not automatically install or promote produced packages; runtime install and promotion remain separate explicit actions.

## Consequences

- Elsa Server orchestrates build jobs but does not load user-authored assemblies as part of authoring.
- Build diagnostics, artifacts, and logs are captured as job outputs.
- The implementation needs a build-worker abstraction before deeper UX polish.
- Future deployment modes can choose local process, container, or remote worker implementations behind the same boundary.
