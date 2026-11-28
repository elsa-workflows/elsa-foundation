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
- **Runtime** (`essentials/src/Workflows/Runtime`, `essentials/src/Activities/Runtime`): executes executables. Contracts and
  models live in `essentials/src/Workflows/Runtime/Core`; the engine (dispatcher, mailbox, drainer, work handlers,
  checkpoint committer) lives in `essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services`, one folder per concept; the API in
  `essentials/src/Workflows/Runtime/Api`. Runtime must not depend on Design.

Activity implementations (`HttpEndpoint`, `Sequence`, `Flowchart`, ...) live under `essentials/src/Activities/<Name>`.

## Persistence

Every store contract has an in-memory default registered by `AddWorkflowRuntime()` in
`essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`. [ADR 0073](docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)
establishes EF Core as the only first-party durable persistence destination, and it is the only one
that ships: every runtime, design, publishing, identity, secrets, diagnostics and dashboard store has
an EF Core implementation, selected per module by its own shell feature. The Workbench defaults to
SQLite through `ConnectionStrings:Elsa`; each module can override its provider and connection.

Decisions are recorded in [`docs/adr/`](docs/adr/README.md).
