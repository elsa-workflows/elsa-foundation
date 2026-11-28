# Elsa Foundation

`elsa-foundation` is the .NET 10 workflow engine "Elsa 4": a set of libraries under `essentials/src` that a host
composes into a running server. Term definitions are authoritative in [`docs/glossary/elsa.md`](docs/glossary/elsa.md)
(Elsa terms) and [`docs/glossary/root.md`](docs/glossary/root.md) (framework terms); this file only orients.

## Entry point

The process starts in [`server/apps/Elsa.Workbench.Host/Program.cs`](server/apps/Elsa.Workbench.Host/Program.cs). It builds one
or more CShells shells from [`server/apps/Elsa.Workbench.Host/shells.json`](server/apps/Elsa.Workbench.Host/shells.json); every
key under `CShells:Shells:default:Features` names an `IShellFeature` class that registers services. A library
whose feature is not listed there is not running. [How a workflow executes](docs/how-a-workflow-executes.md)
traces one request from `Program.cs` to a durable checkpoint.

## The three planes

- **Design** (`essentials/src/Workflows/Design`, `essentials/src/Activities/Design`): authoring. Definitions, drafts and
  immutable versions, plus the activity catalog and validations. Nothing here runs a workflow.
- **Publishing** (`essentials/src/Workflows/Publishing`): compiles a definition version into a content-addressed
  `WorkflowExecutable` and saves it through `IWorkflowExecutableStore`. This is the only bridge from Design to Runtime.
- **Runtime** executes published workflows. [Actor Foundation](essentials/src/ActorFoundation) owns durable messages,
  jobs, and checkpoint commits. [KernelHosting](essentials/src/Workflows/Runtime/KernelHosting) binds the selected
  provider to the [Native execution model](essentials/src/Workflows/Runtime/Native) and Flowchart. Runtime contracts live
  in [Core](essentials/src/Workflows/Runtime/Core), and [KernelApi](essentials/src/Workflows/Runtime/KernelApi) serves
  execution and inspection. Runtime must not depend on Design.

Activity implementations (`HttpEndpoint`, `Sequence`, `Flowchart`, ...) live under `essentials/src/Activities/<Name>`.

## Persistence

`WorkflowsRuntimeKernelHosting` selects the built-in Actor store for the shell. Stock Workbench uses a dedicated
SQLite file for Actor execution. The Workbench Compose profile uses a separate PostgreSQL database for Actor
execution. Other durable stores use their module's EF Core feature and control-plane connection, which defaults
to `ConnectionStrings:Elsa` in Workbench. [ADR 0073](docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)
describes the first-party persistence family.

Decisions are recorded in [`docs/adr/`](docs/adr/README.md).
