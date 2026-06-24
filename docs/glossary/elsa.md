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
| Activity execution | One concrete runtime invocation of an executable activity node within a workflow execution. Multiple activity executions may reference the same authored activity when loops, retries, composite slots, or repeated scheduling revisit that activity. |
| Activity execution inspection projection | A runtime-owned read model for inspecting one activity execution, separate from continuation state and keyed by activity execution identity. It may include captured evidence such as outcomes, value snapshots, bookmarks, incidents, and scheduling provenance. |
| Activity execution value snapshot | Policy-governed inspection evidence for an activity input or output observed during one activity execution. A value snapshot may contain metadata only or include payload when runtime payload capture policy allows it. |
| Activity scheduling provenance | Runtime-owned correlation data that explains why and from where an activity execution was scheduled, including structural parent, temporal scheduler, optional branch or iteration identity, optional execution path or scope identity, and scheduling cause. |
| Publishing/compile bridge | The domain that reads design-side state and produces runtime artifacts without making Runtime depend on Design. |
| Runtime checkpoint | The workflow runtime commit boundary where continuation state, inspection projections, outbox intents, and related runtime changes become durable together according to runtime checkpoint policy. |
| Scheduler-boundary checkpoint | A runtime checkpoint that must persist before scheduler work can safely continue, such as scheduling an activity execution, starting invocation, suspending on a bookmark, completing, faulting, or canceling. |
| Elsa 3 import | Compatibility strategy for Elsa 3 assets. The boundary is import into Elsa 4-native models, not running Elsa 3 behavior in place. |
| Feature composition | Selecting and activating a set of Elsa features and their dependencies into a shell/API host. |
| Seam | In Elsa docs, a published contract boundary between sub-domains, normally represented by `.Core` contracts. See [seams and bridges](../seams.md). |
| Bridge | Code that connects two contract boundaries from above, depending on both contracts without making either side depend on the other. See [seams and bridges](../seams.md). |
