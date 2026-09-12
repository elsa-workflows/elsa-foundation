# Elsa Foundation

`elsa-foundation` is the .NET 10 workflow engine "Elsa 4": a set of libraries under `src/Elsa` that a host
composes into a running server. Term definitions are authoritative in [`docs/glossary/elsa.md`](docs/glossary/elsa.md)
(Elsa terms) and [`docs/glossary/root.md`](docs/glossary/root.md) (framework terms); this file only orients.

## Entry point

The process starts in [`src/Apps/Elsa.Workbench/Program.cs`](src/Apps/Elsa.Workbench/Program.cs). It builds one
or more CShells shells from [`src/Apps/Elsa.Workbench/shells.json`](src/Apps/Elsa.Workbench/shells.json); every
key under `CShells:Shells:default:Features` names an `IShellFeature` class that registers services. A library
whose feature is not listed there is not running. [How a workflow executes](docs/how-a-workflow-executes.md)
traces one request from `Program.cs` to a durable checkpoint.

## The three planes

- **Design** (`src/Elsa/Workflows/Design`, `src/Elsa/Activities/Design`): authoring. Definitions, drafts and
  immutable versions, plus the activity catalog and validations. Nothing here runs a workflow.
- **Publishing** (`src/Elsa/Workflows/Publishing`): compiles a definition version into a content-addressed
  `WorkflowExecutable` and saves it through `IWorkflowExecutableStore`. This is the only bridge from Design to Runtime.
- **Runtime** (`src/Elsa/Workflows/Runtime`, `src/Elsa/Activities/Runtime`): executes executables. Contracts and
  models live in `src/Elsa/Workflows/Runtime/Core`; the engine (dispatcher, mailbox, drainer, work handlers,
  checkpoint committer) lives in `src/Elsa/Workflows/Runtime/Services`; the API in `src/Elsa/Workflows/Runtime/Api`.
  Runtime must not depend on Design.

Activity implementations (`HttpEndpoint`, `Sequence`, `Flowchart`, ...) live under `src/Elsa/Activities/<Name>`.

## Persistence

Every store contract has an in-memory default registered by `AddWorkflowRuntime()` in
`src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`. [ADR 0073](docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md)
establishes EF Core as the only first-party durable persistence destination. The repository is still
in transition: Groundwork-backed runtime, design, and publishing implementations remain while their
owned EF replacements are delivered, and Secrets provides the first opt-in EF implementation. The
Workbench still selects SQLite through `GroundworkProviderSqlite` until its owned composition flip.

Decisions are recorded in [`docs/adr/`](docs/adr/README.md).
