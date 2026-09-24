# Embedded fixture host proof

Status: evidence for [#1994](https://github.com/elsa-workflows/elsa-foundation/issues/1994), under [#1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961). The candidate in the [profile catalog contract](profile-catalog-contract.md#evaluation-fixtures-not-released-profiles) is a planning fixture, not a released profile. Source baseline: `f78a630bf60271752ecec020a7e2f7c113b14a27`.

## Authored selection and source-declared closure

The proposed `foundation-core` + `runtime-base` expression requests exactly 12 IDs:

`Primitives`, `Serialization`, `Mediator`, `Events`, `Expressions`, `ActivitiesRuntime`, `ActivitiesPrimitives`, `ActivitiesControlFlow`, `ActivitiesSequence`, `WorkflowsRuntimeEntityFrameworkCore`, `WorkflowsRuntimeResumption`, `WorkflowsRuntimeTriggers`.

These are authored IDs, not an activation inventory. [`WorkflowsRuntimeTriggers`](../../../src/essentials/Workflows/Runtime/Api/WorkflowsRuntimeTriggersFeature.cs) declares a required dependency on `WorkflowsRuntimeApi`; the [`WorkflowsRuntimeApi` feature](../../../src/essentials/Workflows/Runtime/Api/WorkflowsRuntimeApiFeature.cs) is an `IWebShellFeature` and requires `ApiCapabilities`. [`WorkflowsRuntimeEntityFrameworkCore`](../../../src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeEntityFrameworkCoreFeature.cs) requires `WorkflowsRuntimeResumption`, which requires [`Tasks`](../../../src/essentials/Workflows/Runtime/Resumption/WorkflowsRuntimeResumptionFeature.cs). The [focused generic-host test](../../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/EmbeddedFixtureHostEvidenceTests.cs) observed exactly those three extra IDs: **15 effective features from 12 requested**. A non-ASP.NET host can have no HTTP listener while still activating an API feature; those are different claims.

The Runtime API currently calls `AddWorkflowRuntime()` inside its feature registration. The host-agnostic runtime registration method itself does not require HTTP, but none of the 12 authored Embedded IDs is a dedicated runtime-core shell feature. The existing [test-only runtime root](../../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/RuntimeEntityFrameworkCoreFeatureTests.cs) illustrates the missing seam for a strictly API-free composition. Omitting `WorkflowsRuntimeTriggers` would remove the API dependency only if a host-owned or product-level runtime-root feature also composes the runtime core.

## Host and behavior evidence

The focused host proof uses `ServiceCollection.AddCShells` and `IShellRegistry.GetOrActivateAsync`, without `WebApplication`, ASP.NET integration, a mapped endpoint, or an HTTP listener. It supplied one named SQLite `primary` resource through `Elsa:Persistence:DefaultResource`, `Elsa:Persistence:Resources:primary`, and `ConnectionStrings:Embedded`, without writing the connection value to the report. The activated Runtime EF options resolved to `Provider=Sqlite`, `ConnectionName=Embedded`, and no inline connection string. The Runtime context used the isolated SQLite file, with at least one applied migration and zero pending migrations. This proves one reviewed resource binding and schema installation in this fixture; it does not prove other providers or cross-host durability.

The focused test then registered a manually constructed `Event` executable with a pinned CLR activity contract and a published source reference through the shell's runtime stores. `IWorkflowExecutionStartService` accepted the start; the Event activity suspended with one persisted bookmark. `IBookmarkResumeDispatcher` resumed that bookmark, and the activity and workflow execution both reached `Completed`. The test asserts that the execution and bookmark stores resolved from the shell are the EF implementations. This is a real start/suspend/resume/completion path through the named-resource runtime store in a single host generation. It does not exercise process restart, durable recovery after a crash, or HTTP dispatch.

The existing [shared-persistence host test](../../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/SharedPersistenceCompositionTests.cs) separately proves that the Runtime EF feature can resolve a named SQLite resource and preserve a state-store record across shell recreation. The [runtime end-to-end test](../../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/RuntimeEntityFrameworkCoreEndToEndTests.cs) proves suspend/resume behavior across provider generations using a direct connection string. Neither existing test by itself proves the proposed Embedded selection with a named resource and a real runtime operation.

Repeat the focused proof with:

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --filter FullyQualifiedName~EmbeddedFixtureHostEvidenceTests
```

## Profile consequence

The 12-ID fixture can run useful work in a non-HTTP host, but it must not be published as an API-free Embedded profile: its required closure contains `WorkflowsRuntimeApi`. A strictly API-free starting point needs a reviewed runtime-root seam and a decision about trigger routing without the Runtime API; that change is outside this spike.

Two product paths remain. An embedded process could retain `WorkflowsRuntimeApi` in its feature closure while never mounting HTTP routes, but its profile explanation would need to show why an API feature is present. Alternatively, a new host-agnostic runtime-core feature could own `AddWorkflowRuntime()`, with the API and trigger features depending on that core instead of each other. The latter would change a published feature dependency contract and needs its own design, compatibility, and host tests. Merely hiding the implicit API edge in the builder would make the exact-selection explanation false.
