# Authoring fixture host proof

Status: evidence for [#1991](https://github.com/elsa-workflows/elsa-foundation/issues/1991), under [#1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961). The candidate in the [profile catalog contract](profile-catalog-contract.md#evaluation-fixtures-not-released-profiles) remains a planning fixture, not a released profile. Source baseline: `234d688d0594deb165f767016471fe9fbfbe208a`.

## Selection and host

The proposed `foundation-core` + `authoring-base` expansion requests exactly these 16 IDs:

`Primitives`, `Serialization`, `Mediator`, `Events`, `Expressions`, `ApiCapabilities`, `ActivitiesDesignApi`, `ActivitiesDesignEntityFrameworkCore`, `ActivitiesDesignReconciliation`, `ClrActivityReconciliation`, `WorkflowDesignValidations`, `WorkflowsDesignApi`, `WorkflowsDesignEntityFrameworkCore`, `WorkflowsPublishing`, `WorkflowsPublishingApi`, `WorkflowsPublishingEntityFrameworkCore`.

The [Workbench process test](../../../tests/essentials/Workbench/Tests/AuthoringFixtureHostEvidenceTests.cs) rewrites only the isolated process's copied `shells.json`, supplies a single named SQLite `primary` resource, starts the built Workbench from this checkout, and waits for actual shell readiness. The resource uses `Elsa:Persistence:DefaultResource=primary`, a `Sqlite` provider, `ConnectionName=Authoring`, and a temporary `ConnectionStrings:Authoring` value under that process's disposable content root. No connection value is printed or checked into this report.

## Observed dependency and HTTP boundary

The exact 16-ID exploratory start reached shell readiness and logged **20 activated features**, but an HTTP request failed with 500 because `IAuthenticationSchemeProvider` was absent. The candidate omits `FoundationIdentityAbstractions`, which registers the authentication/authorization services and middleware the Workbench's routed API requests need. Readiness alone therefore does not establish an operable Authoring API.

Source-declared required edges also defeat an authoring-only interpretation: [`WorkflowsPublishing`](../../../src/essentials/Workflows/Publishing/WorkflowsPublishingFeature.cs) depends on `WorkflowsRuntimeTriggers`, which depends on [`WorkflowsRuntimeApi`](../../../src/essentials/Workflows/Runtime/Api/WorkflowsRuntimeTriggersFeature.cs). CShells resolves absent required IDs automatically when their assemblies are available in the host. The fixture thus starts a runtime API even though its 16 explicitly selected IDs do not list one. The feature-management registry's `Enabled`/`Runs` view reports authored selections, so it is not by itself an inventory of every effective shell feature.

In the focused CShells host, `ShellSettings.EnabledFeatures` contained the 16 selected IDs plus exactly `WorkflowsRuntimeApi` and `WorkflowsRuntimeTriggers`: **18 effective features**. The Workbench host logged 20 activated features from the same 16-ID selection; its broader host inventory is not the minimal fixture host. The passing host test observed 35 distinct Activities Design routes, 22 Workflows Design routes, 19 Publishing routes, and 25 Runtime routes from the mounted endpoint sources, as well as the `/capabilities` route. Those numbers are route patterns, not distinct HTTP method/path pairs.

The focused Workbench test passed after explicitly adding `FoundationIdentityAbstractions` and `FoundationIdentityOidc` to the 16 candidate IDs. Anonymous requests returned `GET /design/activities/catalog` → 401, `GET /design/workflows/definitions` → 401, `GET /publishing/workflows/version-1/publish` → 405, and `GET /runtime/workflows/executables` → 401; a known missing path returned 404. The runtime response is a concrete warning that the candidate's Publishing dependency makes execution/inspection routes reachable. The [in-process CShells host test](../../../tests/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/AuthoringCompositionHostEvidenceTests.cs) inspects endpoint sources directly, independently of authentication responses.

The in-process host test bound the three reviewed Design/Publishing EF contexts through the same `Authoring` connection name. Each resolved to the one isolated SQLite file, had one applied migration and zero pending migrations. SQLite ignores schema names, so this proves resource identity and migration readiness for the SQLite fixture, not schema isolation or a production PostgreSQL layout. It did not perform an authenticated authoring or publishing operation. A direct `/capabilities` request against the raw 16-ID host failed at the missing Foundation Identity authorization substrate, so the process test adds the substrate explicitly for its HTTP checks.

## Consequence for the profile catalog

Do not publish the current 16-ID selection as an authoring-only starting profile. A released candidate needs a reviewed explicit dependency closure, host authentication choice, and a product decision on whether authoring includes a runtime API. If authoring-only is required, investigate a narrower Publishing dependency boundary; changing that feature contract is outside this spike. Keep provider and connection settings in the persistence resource/binding layer rather than the profile definition.

## Repeatable checks

From this checkout, run the focused Workbench proof with:

```bash
dotnet test tests/essentials/Workbench/Tests/Elsa.Workbench.Tests.csproj --filter FullyQualifiedName~AuthoringFixtureHostEvidenceTests
```

The in-process host proof can be run with:

```bash
dotnet test tests/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.csproj --filter FullyQualifiedName~AuthoringCompositionHostEvidenceTests
```

The existing [shared-persistence composition test](../../../tests/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/SharedPersistenceCompositionTests.cs) separately checks a restart. The route patterns observed in the passing host test are recorded below.

## Mounted endpoint inventory

Distinct route patterns from the in-process root and shell `EndpointDataSource` instances at the source baseline above. The test prints the same inventory when run with detailed output. Method-specific duplicates are folded into one pattern.

### `/design/activities` (35)

```text
/design/activities/authoring-capabilities
/design/activities/availability/diagnostics
/design/activities/availability/settings
/design/activities/catalog
/design/activities/definitions
/design/activities/definitions/picker
/design/activities/definitions/{definitionId}
/design/activities/definitions/{definitionId}/drafts
/design/activities/definitions/{definitionId}/fork-previews
/design/activities/definitions/{definitionId}/recommendation
/design/activities/definitions/{definitionId}/versions
/design/activities/drafts/{draftId}
/design/activities/drafts/{draftId}/conflict-copies
/design/activities/drafts/{draftId}/contract-proposals
/design/activities/drafts/{draftId}/contract-proposals/apply
/design/activities/drafts/{draftId}/diff
/design/activities/drafts/{draftId}/migrate-provider
/design/activities/drafts/{draftId}/presentation
/design/activities/drafts/{draftId}/publication-preflight
/design/activities/drafts/{draftId}/publish
/design/activities/drafts/{draftId}/validate
/design/activities/fork-candidates/{candidateId}/apply
/design/activities/forks/{idempotencyKey}
/design/activities/publications/{idempotencyKey}
/design/activities/upgrade-plans
/design/activities/upgrade-plans/{planId}
/design/activities/upgrade-plans/{planId}/apply
/design/activities/upgrade-plans/{planId}/receipts/{receiptId}
/design/activities/upgrade-plans/{planId}/refresh
/design/activities/versions/{fromVersionId}/diff/{toVersionId}
/design/activities/versions/{versionId}
/design/activities/versions/{versionId}/dependencies
/design/activities/versions/{versionId}/restore
/design/activities/versions/{versionId}/retire
/design/activities/versions/{versionId}/revoke
```

### `/design/workflows` (22)

```text
/design/workflows/activities/{activityVersionId}/inputs/{inputName}/options
/design/workflows/definitions
/design/workflows/definitions/submit
/design/workflows/definitions/submit/schema
/design/workflows/definitions/{definitionId}
/design/workflows/definitions/{definitionId}/permanent
/design/workflows/definitions/{definitionId}/restore
/design/workflows/definitions/{definitionId}/versions
/design/workflows/drafts/{draftId}
/design/workflows/drafts/{draftId}/promote
/design/workflows/drafts/{draftId}/promotion-preflight
/design/workflows/drafts/{draftId}/validations
/design/workflows/expression-tooling/completions
/design/workflows/expression-tooling/context
/design/workflows/expression-tooling/descriptors
/design/workflows/expression-tooling/hover
/design/workflows/expression-tooling/symbols
/design/workflows/expression-tooling/validate
/design/workflows/scoped-variables/analyze
/design/workflows/structures
/design/workflows/versions/ingest
/design/workflows/versions/{versionId}
```

### `/publishing` (19)

```text
/publishing/activities
/publishing/activities/{activityId}/construct
/publishing/activity-drafts/{draftId}/test-runs
/publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}
/publishing/activity-test-runs/{testRunId}
/publishing/activity-test-runs/{testRunId}/cancel
/publishing/incident-strategies
/publishing/preflight
/publishing/publications/{publicationId}
/publishing/value-conversion/profiles
/publishing/workflows/drafts/test-runs
/publishing/workflows/preflight
/publishing/workflows/{definitionId}/policy
/publishing/workflows/{definitionId}/slots/{slotName}
/publishing/workflows/{definitionId}/slots/{slotName}/restore
/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/executable-export
/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/preflight
/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish
/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/test-runs
```

### `/runtime` (25)

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
