# Extension Builder Git Editor Design

## Current direction

Extension Builder is being redesigned as a Git-repo-first online .NET repository editor. A workspace is the logical repository boundary. Editing happens in a working copy, usually on an explicit working branch. Source files live on disk in Git working trees, while the database stores metadata, job records, template references, and promotion history.

The phased implementation sequence is tracked in [extension-builder-git-editor-roadmap.md](extension-builder-git-editor-roadmap.md).

## Primary screen model

The steady-state Extension Builder screen has four zones:

1. Repository rail
2. Solution explorer
3. Editor tabs
4. Context inspector

The bottom diagnostics drawer remains global and can show logs, build output, source-control diagnostics, and runtime signals.

## Repository rail

The repository rail lists Git-backed workspaces with compact health indicators:

- Active branch
- Dirty state
- Last build state
- Remote connection state
- Attention count

The rail is for selecting the repository authoring context, not for project creation forms.

## Solution explorer

The solution explorer is repository-rooted. If the repository has exactly one solution, that solution is auto-focused. If the repository has multiple solutions, the explorer shows a solution picker. It presents repository, solution, project, folder, and file structure.

Build, promotion, runtime, and source-control details are not primary tree nodes. They appear in contextual inspectors and diagnostics surfaces.

## Editor tabs

Editor tabs open physical files from the active working copy. The first v1 target is Monaco-based editing with save, dirty indicators, syntax awareness, and build/test diagnostics mapped back to files.

Full C# IntelliSense, semantic refactoring, debugging, terminal access, and full NuGet management are outside v1.

## Context inspector

The right inspector changes by current selection:

- Repository or branch: source-control status, remotes, branch operations, pull/push state.
- Solution: build/test/pack commands and solution-level diagnostics.
- Project: project properties, package outputs, project-level build/pack actions.
- File: file properties, diagnostics, diff actions.
- Package artifact: promote/install status and downstream feed/module references.

## Repository entry flow

The `New or Clone` action exposes four entry paths:

- Create managed repo
- Clone from Git
- Open server-local repo
- Create from template

Managed repositories can connect to a remote later through Source Control.
Managed repositories create a machine-authored initial commit after starter content is scaffolded, so user edits begin from a clean baseline.

Server-local repository registration is admin-only and limited to configured allow-listed roots. Studio does not expose a general-purpose server filesystem browser.

Templates are cataloged extension contributions from trusted built-in packages or installed module packages. Arbitrary uploaded template archives are outside v1.

## First implementation slice

The first useful slice should deliver:

- Repository list and selection.
- Managed repository creation.
- Physical-file solution explorer.
- Monaco editing and save.
- Build worker integration for restore/build/test/pack logs.
- Build diagnostics mapped to files.
- Git status, diff, stage, unstage, commit, create branch, guarded pull, and push.
- Template-driven repository, solution, project, and item creation.

## Backend capability split

Backend contracts are split by capability rather than by a single project-centric endpoint family:

- Repositories
- Working copies
- Files
- Git
- Builds
- Templates
- Artifacts and promotions

## Deferred capabilities

These require separate designs:

- Roslyn/LSP-backed C# intelligence.
- Integrated terminal.
- Debugger.
- Full NuGet package manager.
- Interactive conflict resolution.
- Force push, hard reset, rebase, and remote branch deletion.
- Workspace-level release grouping.
