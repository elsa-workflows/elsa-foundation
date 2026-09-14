# Extension Builder Git Editor Roadmap

## Goal

Turn Extension Builder into a Git-repo-first online .NET solution and project editor. Source files live as physical files in Git working trees. Elsa Server stores metadata and orchestrates editing, Git operations, builds, templates, and promotion flows without treating database blobs as source-code truth.

## Phase 1: Domain, storage, and API foundation

Establish the backend model before rebuilding the UI.

- Replace package-first workspace concepts with repository-first workspace concepts.
- Add working-copy metadata scoped by user/session/branch.
- Store repositories under a configurable Extension Builder workspace root, outside the app source tree by default.
- Keep source files in Git working trees, not database blobs.
- Split APIs by capability: repositories, working copies, files, Git, builds, templates, artifacts, and promotions.
- Enforce repository access rules across every capability API.
- Restrict server-local repository attach to administrators and configured allow-listed roots.

## Phase 2: Repository workbench and physical file editing

Deliver the first steady-state authoring experience.

- Replace always-visible creation forms with a repository rail and `New or Clone` entry flow.
- Implement the four entry paths: managed repo, clone from Git, open server-local repo, and create from template.
- Create a managed repository initial commit after starter content is scaffolded.
- Implement the repository-rooted solution explorer.
- Auto-focus the only solution; show a solution picker when multiple solutions exist.
- Open physical files in Monaco editor tabs.
- Support file create, rename, delete, read, save, dirty indicators, and unsaved-change guards.

## Phase 3: Git-first source control

Make source control visible and trustworthy.

- Add source-control inspector for status, branch, remotes, diffs, staged files, and commit message.
- Default editing to explicit working branches rather than silently editing default branches.
- Support branch create and safe branch selection.
- Support diff, stage, unstage, commit, push, and guarded pull.
- Require explicit user choice before editing protected or common default branches such as `main` or `master`.
- Stop on divergence or conflict risk and explain recovery paths instead of entering unsupported merge states.

## Phase 4: Build worker and diagnostics

Add .NET feedback without loading user code into Elsa Server.

- Introduce a build-worker abstraction for restore, build, test, and pack.
- Run build work outside the Elsa Server host process with timeouts, cancellation, configured roots, and log streaming.
- Capture job records, logs, diagnostics, and artifacts.
- Map build/test diagnostics back to files and editor tabs.
- Show solution-level and project-level build commands in contextual inspectors.

## Phase 5: Pack, promote, and module bridge

Connect repository outputs to package and module flows explicitly.

- Support `Pack project` and `Pack solution`.
- Show package artifacts as downstream outputs of repository authoring.
- Require committed Git revisions for promotion and package publishing.
- Add `Promote package` and `Promote solution packages` flows.
- Link promoted package artifacts to package feeds, module catalog state, and runtime install status.
- Keep module inspection in the Modules area; Extension Builder links to it instead of redefining source projects as modules.

## Phase 6: Advanced IDE capabilities

Design these separately after the Git editor foundation is stable.

- Roslyn/LSP-backed C# intelligence.
- Semantic refactoring.
- Integrated terminal.
- Debugger.
- Full NuGet package manager.
- Interactive merge conflict resolution.
- Workspace-level release grouping.
- User-uploaded template archives.
