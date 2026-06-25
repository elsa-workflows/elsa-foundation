# Elsa Glossary

These are Elsa-specific terms and bindings. Framework-level terms live in [root.md](root.md).

| Term | Canonical meaning |
|---|---|
| Elsa foundation workspace | The repository role where foundation libraries, architecture gates, maps, skills, and glossary knowledge make Elsa navigable and verifiable. |
| Elsa host | The `Elsa.Server` ASP.NET Core application instance that composes selected Elsa features. |
| Elsa application | The Elsa application defined by the domain tree in the Elsa constitution. |
| Elsa foundation repo | `elsa-foundation`, this repository: the transitional baseline for Elsa foundation libraries, architecture knowledge, maps, specs, and default implementation work. |
| Elsa.Primitives | The primitives domain replacing the historical `Elsa.Common`; it carries truly cross-cutting primitive abstractions without becoming a dumping ground. |
| Workflow design | The authoring side of workflows: definitions, drafts, versions, design-time validation, design-time persistence, and read models. |
| Workflow runtime | The execution side of workflows: executable artifacts, instances, runtime state, bookmarks, logs, and execution integrations. |
| Activity catalog | The persisted source of truth for activities visible to design-time consumers. Picker visibility comes from catalog rows, not live scanning. |
| Workflow root activity | The single activity a workflow definition/executable runs. It may be a primitive activity or a composite activity such as `Sequence`, `Flowchart`, or `StateMachine`; composition details belong to that activity. |
| Workflow executable | The runtime-owned artifact produced from design-time source and consumed by runtime execution. It carries one compiled root activity plus runtime identity, source reference, resume targets, timestamps, and compatibility metadata. |
| Workflow definition state | The authored workflow document stored with drafts and versions in the design domain. It owns authored content, including one root activity, not runtime state. |
| Facet | A module-owned, kinded, schema-versioned design metadata fragment attached to a broader model. Core stores, hashes, validates generic shape, and round-trips facets without owning their payload semantics. |
| Artifact-only runtime | The rule that runtime execution must be able to load and run from the published artifact without requiring design documents. |
| Reconciliation | A process that updates persisted design/catalog state from authoritative sources such as CLR activities, JSON import, or workflow definitions. |
| Activity construction | The runtime-side process that turns a descriptor type, descriptor payload, and argument bags into a live `IActivity`. |
| Publishing/compile bridge | The domain that reads design-side state and produces runtime artifacts without making Runtime depend on Design. |
| Elsa 3 import | Compatibility strategy for Elsa 3 assets. The boundary is import into Elsa 4-native models, not running Elsa 3 behavior in place. |
| Feature composition | Selecting and activating a set of Elsa features and their dependencies into a shell/API host. |
| Seam | In Elsa docs, a published contract boundary between sub-domains, normally represented by `.Core` contracts. See [seams and bridges](../seams.md). |
| Bridge | Code that connects two contract boundaries from above, depending on both contracts without making either side depend on the other. See [seams and bridges](../seams.md). |
| Extension Builder workspace | A logical Git-backed authoring space for one repository. It contains one or more .NET solutions, projects, source files, and related repository metadata. It is the source-control boundary first; build, package, and promotion behavior are downstream concerns. Multiple users may work in the same workspace through separate working copies. |
| Extension Builder working copy | A server-side Git checkout/clone for a specific user, session, or branch within an Extension Builder workspace. Source editing happens in a working copy, while the workspace remains the logical repository boundary visible in Studio. |
| Extension Builder working branch | The Git branch selected for an Extension Builder working copy. New edit sessions default to an explicit working branch rather than silently editing the repository default branch. |
| Extension Builder managed repository | The default Extension Builder workspace creation mode where Elsa Server initializes and owns a local Git repository first, creates an automatic initial commit for generated starter files, then lets the user connect or change the remote origin and push to their own repository later. |
| Extension Builder server-local repository | An existing Git repository on the Elsa Server filesystem that is registered as an Extension Builder workspace instead of being created or cloned by Extension Builder. |
| Extension Builder solution | A .NET solution file inside an Extension Builder workspace. It is the primary coordination boundary for solution-level build/test/package commands in the Studio UX. |
| Extension Builder project | A .NET project file inside an Extension Builder workspace or solution. It may produce packages or runtime contributions, but package identity is output metadata rather than the primary authoring boundary. |
| Extension Builder package artifact | A package output produced from an Extension Builder project or solution. It can be promoted to package feeds or module catalogs, but it is downstream from repository authoring. |
| Extension Builder solution explorer | The authoring navigation surface for a Git-backed Extension Builder workspace. It is repository-rooted, auto-focuses the only solution when exactly one solution exists, and uses a solution picker when multiple solutions exist. It shows repository, solution, project, folder, and file structure; build, promotion, runtime, and source-control details belong in contextual inspectors rather than as primary tree nodes. |
| Extension Builder repository workbench | The primary Extension Builder screen for selecting a Git-backed workspace, opening its active working copy, navigating solutions and files, editing source, and inspecting source-control/build state. |
| Extension Builder editor intelligence | The code understanding available inside Extension Builder. The first implementation target is build-backed intelligence: syntax-aware editing plus build/test diagnostics mapped to files. The UX may reserve space for future Roslyn/LSP-backed capabilities without implying they exist. |
| Extension Builder v1 editor scope | The first useful online .NET editor slice: source editing, repository navigation, file operations, build/test diagnostics, Git operations, and templates, without full IDE features such as debugger, terminal, semantic refactoring, or full NuGet management. |
| Extension Builder source-control inspector | The contextual source-control surface for a Git-backed Extension Builder workspace. It exposes real Git status, diffs, staging, unstaging, commit, and push flows; staged changes are the commit boundary, with stage-all as a convenience. |
| Extension Builder template | A scaffold applied at a specific authoring level: repository templates create managed workspaces, solution templates add .NET solutions, project templates add .NET projects to a solution or repository, and item templates add files to compatible projects. |
| Extension Builder build worker | The isolated execution boundary that runs restore, build, test, and pack operations for Extension Builder repositories outside the Elsa Server host process. |
