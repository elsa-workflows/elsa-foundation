# Classic Runtime test archive

Run the classic Runtime resumption pump tests and EF command-cost measurements
from the exact source revision that owns their engine, fixtures and dependency locks.
The measurements describe the classic checkpoint and coalescing path.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" af155fa58651f147160c840924685ed9610a299c
cd "$archive/source"
dotnet test essentials/tests/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
ELSA_COMMAND_COST_OUT="$(mktemp -d)" \
  dotnet test extensions/Http/tests/Activities/CommandCost/Tests/Elsa.Activities.Http.CommandCost.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

The command-cost PostgreSQL run needs Docker. See its
[archived measurement instructions](https://github.com/elsa-workflows/elsa-foundation/blob/af155fa58651f147160c840924685ed9610a299c/extensions/Http/tests/Activities/CommandCost/Tests/README.md)
for report fields and the dedicated test database setup.

## Classic Runtime execution API and scheduler

Use source revision `af562301713e49d879c7c0bf362efdbfaeb8a94e` to replay the
classic execution, dispatch, inspection and timer contracts. From that checkout,
run these projects with the original dependencies and test inputs.

```sh
dotnet test essentials/tests/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
dotnet test essentials/tests/Workflows/Runtime/Scheduling/Tests/Elsa.Workflows.Runtime.Scheduling.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

The current projects exercise standalone trigger management, prepared recurring
schedules and shared cursor behavior. Current Native and Flowchart execution,
history and restart tests use the Actor Foundation and kernel-host projects.

## Classic activity execution

The activity fixture at `b5e76081ff405049f335d6b222de18ed96ae8966` owns the
classic checkpoint, invocation, resumption, activity worker and scope tests.
From that exact checkout, run:

```sh
dotnet test essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

Current source keeps the typed activity and state contracts, schedule metadata,
Event wire and source-registration checks, and scope-name behavior. Actor
Foundation qualifies the supported activity execution paths against its own
engine, stores and host.

## Classic runtime internals and collectible material

Use source revision `b5e76081ff405049f335d6b222de18ed96ae8966` for the
classic run loop, coalesced checkpoint, scheduler, historical inspection,
runtime alteration, stimulus router and opt-in executable collector tests.
Run their original project from that source:

```sh
dotnet test essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

The retained suite verifies contracts and publication services through their
current owners. Physical provider and Actor cases continue to exercise
publication pins, History, prepared starts and protected material across restart.

## Classic Runtime tracing producer

Replay the classic engine tracer and its OpenTelemetry bridge on the pinned
source. The current bridge tests exercise the stable activity source and tags
directly, including per-drain batching, aged flush and nested traces.

```sh
(
  git -c core.fsmonitor=false worktree add --detach "$compatibility_root/classic-tracing" ef70e5acd7fa8b07cfad4a6c0ca9a229a039f20a
  cd "$compatibility_root/classic-tracing"
  dotnet test extensions/Diagnostics.OpenTelemetry/tests/Tests/Elsa.Diagnostics.OpenTelemetry.Tests.csproj \
    -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~EngineTracingBridgeTests
)
```

## Classic Publishing and Runtime integration

Use `45a594857c700de2fe76de9cbcb62d06bbf6876b` for the classic
Publishing/Runtime API join, activity input hydration, checkpoint execution,
and optional executable collection witnesses. The projects at that source build
their original runtime fixtures and use their matching package locks. The
ordinary Publishing fixture exercises the classic intrinsic registration.

```sh
dotnet test essentials/tests/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
dotnet test essentials/tests/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
dotnet test essentials/tests/Workflows/Publishing/Tests/Elsa.Workflows.Publishing.Tests.csproj \
  -c Release -p:RestoreLockedMode=true \
  --filter FullyQualifiedName~Publication_review_sees_the_registered_intrinsic_consumer_without_an_activation_strategy
```

Current Publishing tests retain protected source references, executable hash
compatibility, publication and journal behavior, and real migration and test-run
transport checks through their current storage and execution providers.

## Classic API composition and alteration contracts

Use `415905150f6060a2b257649fe48c645873a428cc` for the classic runtime
HTTP manifest, alteration route and failure contracts, and the concrete
checkpoint engine shape. The current Architecture project checks the Actor API
and keeps the retained alteration wire models and permission checks.

```sh
bash tools/architecture/restore-ci-project-graph.sh
dotnet test essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj \
  -c Release -p:RestoreLockedMode=true \
  --filter 'FullyQualifiedName~RuntimeAlteration|FullyQualifiedName~RuntimeCoreEngineShapeGuardTests|FullyQualifiedName~DomainManagementApiCompositionTests|FullyQualifiedName~DomainApiCapabilityRegistrationTests'
```

## Classic Runtime provider witnesses

Use `415905150f6060a2b257649fe48c645873a428cc` for the classic provider
checkpoint, claim, resumption, dispatch, timer, historical projection and
executable collection cases. The source pins the original provider runners,
fixtures, historical snapshots and restore locks. Run the provider suite in an
owned checkout with disposable databases.

```sh
ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS=1 \
  dotnet test essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj \
  -c Release -p:RestoreLockedMode=true
```

Current provider tests keep the shared schema and historical-row preservation,
artifact and activation transitions, occurrence claims, Actor publication pins
and Events, and standalone and TestRun stores.

The same source also pins the classic SQLite checkpoints, resumptions, queue
drain and collector tests, including their linked fixtures. In that checkout,
run:

```sh
dotnet test essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

The current SQLite suite exercises physical publication, standalone delivery,
TestRun cleanup, shared Runtime tables, and Actor executable retention roots.

## Classic Agent execution and Dispatch checkpoints

Use the same pinned checkout for the Agent workflow runner, its draft TestRun
and continuity fixtures, and Dispatch's checkpoint, resume and readiness
fixtures. Current tests retain Agent publication, journal, provider and evidence
authorization and Dispatch authoring, source pins, and stored control models.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" 91b9aceb2cff489a180196fa1a2e9ee77465018b
cd "$archive/source"
dotnet test extensions/Agent/tests/Tests/Elsa.Agent.Tests.csproj -c Release -p:RestoreLockedMode=true
dotnet test essentials/tests/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

## Classic Dashboard EF selector

The `WorkflowsDashboardEntityFrameworkCore` shell selector composed the classic
checkpoint-backed run-health projection and the workflow portfolio in one
feature. It called `AddWorkflowRunHealthEntityFrameworkCore` in the Dashboard
EF package's `DependencyInjection/WorkflowRunHealthEntityFrameworkCoreRegistration.cs`.
Use `385008d6459580ecf0c4566f0e8412bf9b10cf8c` for both the selector and the
registration's complete-provider and refusal tests.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" 385008d6459580ecf0c4566f0e8412bf9b10cf8c
(
  cd "$archive/source"
  dotnet test essentials/tests/Workflows/Dashboard/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Tests.csproj \
    -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~WorkflowsDashboardEntityFrameworkCoreFeatureTests|FullyQualifiedName~Registration_requires_runtime_ef_context_and_replaces_source'
)
```

Current shells select `WorkflowsDashboardPortfolioEntityFrameworkCore` for
portfolio queries. The retained Dashboard reports Actor run-health statistics
as unavailable. The EF package keeps the historical run-health reader, its
original projection, and physical provider tests.

The classic Workbench multi-selector case needs the older
[`workbench-classic-default` checkout](workbench-compatibility-tests.md)
at `18a16dee878106ac525aa9a861d851a200e43c55`. Its stock shell supplies the
comprehensive Runtime feature that the test replaces. It also owns the two
shared-resource workflow journeys that verify classic physical execution rows.
Run the archived cases from that checkout.

```sh
ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL=1 \
  dotnet test essentials/tests/Persistence/EntityFrameworkCore/SharedResources/Tests/Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests.csproj \
  -c Release -p:RestoreLockedMode=true \
  --filter 'FullyQualifiedName~Partial_opt_in_runtime_stores_activate_without_the_comprehensive_execution_profile|FullyQualifiedName~Shared_resource_preserves_reusable_activity_published_workflow_and_execution_after_restart|FullyQualifiedName~Separate_diagnostics_resource_keeps_representative_workflow_on_primary_after_restart'
```
