# Quickstart — Validating the publishing engine / API split

How to prove the split works and preserves behaviour. All commands from the repo root (`elsa-foundation`).

## 1. Behaviour preservation (the golden-rule gate)

Run the existing publishing test suites unchanged — they must stay green:

```bash
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
dotnet test tests/Elsa/Workflows/Publishing/Persistence/Groundwork/Tests/Elsa.Workflows.Publishing.Persistence.Groundwork.Tests.csproj
```

Key preserved assertions: `WorkflowsPublishingApiFeatureTests` (every engine service still resolves through the inherited Api feature, incl. `IActivityPublishingAuthorizationContext`), `PublishWorkflowRequestHandlerTests`, `PublishWorkflowTriggerIndexingTests`.

## 2. Architecture guards

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

After literal updates: `GroundworkPersistenceLifetimeTests` (file-path literals → engine feature), `RuntimeExecutionSliceDependencyTests` + `BridgeDependencyDirectionTests` (assertions now also cover the engine assembly), `Solution_folders_collapse_leaf_project_segments` (new project in `.slnx`).

## 3. Engine-without-endpoints (the reason for the split — SC-002)

New engine registration test (`§2.23.1`) composes **only** `WorkflowsPublishing`:

- Build the `IServiceProvider` from `new WorkflowsPublishingFeature().ConfigureServices(services)`.
- Assert `IRequestHandler<PublishWorkflow, PublishedWorkflowView>`, `IWorkflowExecutableCompiler`, and the publication stores resolve; assert `IActivityPublishingAuthorizationContext` is **not** registered (engine is authorization-free).
- Assert **no** FastEndpoints publish endpoints are registered (no `Elsa.Api.FastEndpoints` transport pulled in).
- (Integration-style, optional) send `PublishWorkflow(versionId)` for a materialised version and assert a single live Published source reference is produced.

## 4. Full build + server smoke

```bash
dotnet build Elsa.Server.slnx
```

Then the standard server bootstrap (Groundwork apply + run the built `Elsa.Server.dll`) and confirm the publish endpoint still responds identically when `WorkflowsPublishingApi` is enabled — the pre-split baseline behaviour (SC-004).

## Done when

- SC-001 existing publishing tests pass unchanged.
- SC-002 engine-only shell mounts zero publish endpoints yet can publish in-process.
- SC-003 Api `ConfigureServices` registers only base + HTTP override + endpoints + capabilities.
- SC-004 API-enabled endpoint surface identical to baseline.
- SC-005 no reference resolves against the old `Api` command namespace.
