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
