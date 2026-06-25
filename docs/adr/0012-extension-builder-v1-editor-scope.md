# 0012. Extension Builder v1 editor scope

## Status

Accepted

## Context

The Git-repo-first redesign could expand into a full browser IDE. That would risk mixing the first useful slice with harder capabilities such as language-server hosting, debugging, terminal isolation, and NuGet package-management UX.

## Decision

Extension Builder v1 provides:

- Monaco-based source editing.
- Repository, solution, project, folder, and file navigation.
- File create, rename, delete, and save operations.
- Build and test diagnostics mapped back to files.
- Git diff, stage, unstage, commit, push, branch create, and guarded pull operations.
- Template-driven repository, solution, project, and item creation.

Extension Builder v1 does not promise full C# IntelliSense, semantic refactoring, debugging, integrated terminal access, or a full NuGet package manager. Those capabilities require separate Roslyn/LSP, sandbox, and package-management designs.

## Consequences

- The first implementation can deliver a useful online .NET editor without pretending to be a complete IDE.
- Build-backed diagnostics become the first intelligence source.
- The UI can reserve space for future language intelligence while accurately reflecting current capability.
- Roadmap items for Roslyn/LSP, terminal, debugger, and NuGet management remain separate work units.
