# Worker fixture host proof

Status: evidence for [#1997](https://github.com/elsa-workflows/elsa-foundation/issues/1997), under [#1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961). The candidate in the [profile catalog contract](profile-catalog-contract.md#evaluation-fixtures-not-released-profiles) remains a planning fixture, not a released profile. Source baseline: `e0f44a1d682d641465fcf75fc0696a96ff2be58e`.

## Selection and host closure

The proposed `foundation-core` + `runtime-base` + `worker-http` expression requests exactly 14 feature IDs: `Primitives`, `Serialization`, `Mediator`, `Events`, `Expressions`, `ActivitiesRuntime`, `ActivitiesPrimitives`, `ActivitiesControlFlow`, `ActivitiesSequence`, `WorkflowsRuntimeEntityFrameworkCore`, `WorkflowsRuntimeResumption`, `WorkflowsRuntimeTriggers`, `ApiCapabilities`, and `WorkflowsRuntimeApi`. The [focused Worker host test](../../../tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerHttpFixtureHostEvidenceTests.cs) observed exactly **15 effective IDs**: the 14 selected IDs plus `Tasks`, required by [`WorkflowsRuntimeResumption`](../../../src/essentials/Workflows/Runtime/Resumption/WorkflowsRuntimeResumptionFeature.cs). Unlike the [Embedded fixture](embedded-host-proof.md), Worker selects `WorkflowsRuntimeApi` and `ApiCapabilities` explicitly. [`WorkflowsRuntimeTriggers`](../../../src/essentials/Workflows/Runtime/Api/WorkflowsRuntimeTriggersFeature.cs) still requires the Runtime API, and [`WorkflowsRuntimeEntityFrameworkCore`](../../../src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeEntityFrameworkCoreFeature.cs) requires Resumption. The test adds no test-only feature to the selected set.

The test builds a minimal CShells `WebApplication` with `TestServer`, maps the one Worker shell at the root path, and supplies one named SQLite `primary` resource. The resource resolves as `Provider=Sqlite`, `ConnectionName=Worker`, and no inline connection string in the Runtime EF options. `RuntimeDbContext` uses the isolated SQLite file, has applied migrations, and has zero pending migrations. The connection value remains in the disposable test configuration and is not recorded here. This proves one named-resource binding and migration-ready Runtime store, not other providers or process-restart recovery.

The host owns a process-local distributed-lock provider for `Tasks` initialization. It also registers Foundation identity abstractions and a test authentication scheme outside the 14 selected shell features. The 14-feature fixture has no Identity feature. An exploratory attempt to use CShells' shell-aware authentication delegation failed during startup because the shell had no Foundation permission-policy provider; placing authentication middleware after `MapShells` likewise resolved a missing scheme provider from that shell scope. The passing fixture instead authenticates at the root host before `MapShells`, while the mapped endpoints still enforce their permission metadata. A production Worker needs an explicit host or shell identity choice before the profile can be released; the test-only scheme is not a proposed production policy.

## HTTP and runtime behavior

The mounted host contained **25 distinct `/runtime` route patterns** and `/capabilities`, with no `/design` or `/publishing` route patterns. The test verifies the mapped execute and stimulus endpoints and sends real HTTP requests through them. An anonymous execute request returned 401; an authenticated request with only `workflow-runtime.read` returned 403; a request with `workflow-runtime.execute` returned 200 and an accepted dispatch.

The test seeded a published `Event` executable and source reference through the shell's runtime stores. The authorized `POST /runtime/workflows/executables/{artifactId}/execute` reached the real runtime, whose EF execution and bookmark stores recorded a suspended activity and one bookmark. `POST /runtime/workflows/stimuli` then used that persisted bookmark's stimulus type and hash in `ResumeOnly` mode, resumed the same execution, removed the bookmark, and left the activity and workflow completed. This is a request-to-runtime-to-EF proof in one host generation. It does not exercise a process restart, a remote queue, a production authentication provider, or a second database provider.

The route inventory below is the distinct pattern snapshot printed by the passing test. It folds any method-specific duplicates into one pattern; it is not a claim that every listed operation was exercised.

```text
/runtime/workflows/activation-slots/{definitionId}
/runtime/workflows/activation-slots/{definitionId}/{slotName}
/runtime/workflows/alteration-plans
/runtime/workflows/alteration-plans/{planId}
/runtime/workflows/alteration-plans/{planId}/cancel
/runtime/workflows/alteration-plans/{planId}/jobs/page
/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}
/runtime/workflows/diagnostics/settings
/runtime/workflows/dispatches
/runtime/workflows/dispatches/{dispatchId}
/runtime/workflows/dispatches/{dispatchId}/redrive
/runtime/workflows/executables
/runtime/workflows/executables/{artifactId}
/runtime/workflows/executables/{artifactId}/execute
/runtime/workflows/executables/{artifactId}/provenance
/runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources
/runtime/workflows/instances
/runtime/workflows/instances/page
/runtime/workflows/instances/{workflowExecutionId}
/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}
/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants
/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout
/runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload
/runtime/workflows/instances/{workflowExecutionId}/incidents
/runtime/workflows/stimuli
```

## Profile consequence and repeatable proof

The proposed Worker selection is a useful HTTP runtime fixture when a host supplies authentication and lock services. It is not yet a released Worker profile: the catalog must explain those host prerequisites and distinguish selected shell features from host services and mapped APIs. The future profile decision also needs a production identity boundary, package/host compatibility evidence, and explicit version pinning; this spike does not change those contracts.

Run both the Worker proof and the refactored Embedded regression in the existing Runtime EF test suite with:

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --filter 'FullyQualifiedName~WorkerHttpFixtureHostEvidenceTests|FullyQualifiedName~EmbeddedFixtureHostEvidenceTests'
```
