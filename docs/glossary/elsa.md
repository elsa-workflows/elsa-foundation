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
| Extension Builder workspace | A Git-backed authoring workspace: exactly one checked-out repository containing one or more .NET solutions, projects, source files, and related repository metadata. It is the source-control boundary first; build, package, and promotion behavior are downstream concerns. |
| Extension Builder managed repository | The default Extension Builder workspace creation mode where Elsa Server initializes and owns a local Git repository first, then lets the user connect or change the remote origin and push to their own repository later. |
| Extension Builder solution | A .NET solution file inside an Extension Builder workspace. It is the primary coordination boundary for solution-level build/test/package commands in the Studio UX. |
| Extension Builder project | A .NET project file inside an Extension Builder workspace or solution. It may produce packages or runtime contributions, but package identity is output metadata rather than the primary authoring boundary. |
| Extension Builder solution explorer | The authoring navigation surface for a Git-backed Extension Builder workspace. It shows repository, solution, project, folder, and file structure; build, promotion, runtime, and source-control details belong in contextual inspectors rather than as primary tree nodes. |
