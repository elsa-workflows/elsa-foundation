# Contracts — Feature registration surface (post-split)

The observable "contract" of this refactor is the DI registration surface of the two features and the relocated mediator message. These are the assertions the tests pin.

## Engine feature — `WorkflowsPublishing`

Composing **only** `WorkflowsPublishing` (no Api) MUST make all of the following resolvable, and MUST mount **zero** HTTP endpoints and register **no** authorization service:

- `IWorkflowExecutableCompiler`, `IWorkflowExecutableStore`, `IWorkflowExecutableSourceReferenceStore`, `IWorkflowExecutableSourceReferenceReader`, `IExecutableActivityTemplateReader`
- `IPublicationSlotStore`, `IPublicationRecordStore`, `IPublicationPolicyStore`, `IPublicationProjectionIntentStore`, `IPublicationPolicyResolver`, `IPublicationPreflightService`, `IPublicationProjectionPreparer`, `IPublicationActivator`
- `IWorkflowDefinitionVersionLayoutStore` (fallback), `IActivityStructureService`, `IWorkflowDefinitionPermanentDeletionGuard`
- `IActivityTemplateProviderCompilerRegistry`, `IActivityTemplateDependencyDiscovererRegistry`, `IActivityTemplateCompiler`
- `IRequestHandler<PublishWorkflow, PublishedWorkflowView>` (the relocated handler) + the workflow test-run / slot-lifecycle / preflight handlers
- Event handlers for `OnExecutableCompilationCollecting`, `OnExecutableNodeMetadataCollecting`
- `TimeProvider`

MUST NOT resolve (engine is authorization-free): `IActivityPublishingAuthorizationContext` — absent in an engine-only shell.

Behavioural contract: sending `PublishWorkflow(versionId)` in-process compiles the executable, writes a single **live Published** `WorkflowExecutableSourceReference`, and indexes triggers — identical to the pre-split publish result.

## Transport feature — `WorkflowsPublishingApi`

Composing `WorkflowsPublishingApi` (which `DependsOn WorkflowsPublishing`) MUST, at the **shell** level:

- Resolve everything the engine resolves (the shell activates the engine via `DependsOn`).
- Additionally register: `IHttpContextAccessor`; `IActivityPublishingAuthorizationContext → HttpContextActivityPublishingAuthorizationContext`; the activity-draft services `ActivityDefinitionPublisher` / `ActivityDraftTestRunService` (+ `IActivityDraftTestRunStore`, cancellation policy); the publishing FastEndpoints endpoints; `AddApiCapability(PublishingApiCapabilities.StaticDeclaration)`; `AddApiCapabilitySource<ConversionProfilesCapabilitySource>()`.
- Mount the **same** publish/test-run/slot/preflight/inspection endpoints (routes + behaviour) as before the split.
- Register **zero workflow-publish engine services** in its own `ConfigureServices` (SC-003) — those come from the engine via `DependsOn`.

> **Direct-`ConfigureServices` note (tests):** calling `new WorkflowsPublishingApiFeature().ConfigureServices(services)` in isolation does **not** run the engine's registration (that is a shell `DependsOn` concern). Tests asserting engine-service resolution must compose the engine feature too — a §2.21.1 wiring change. The Api-only assertions (auth context, activity-draft services, endpoints, capabilities) resolve from the Api feature alone.

## Relocated mediator contract

```
namespace Elsa.Workflows.Publishing.Core.Requests;
public sealed record PublishWorkflow(
    string VersionId,
    PublicationAction? Action = null,
    string? SlotName = null,
    string? ExpectedPublicationId = null,
    string? PreflightToken = null,
    string? TenantId = null) : IRequest<PublishedWorkflowView>;
```
`PublishedWorkflowView` moves to `Elsa.Workflows.Publishing.Core.Models`. All senders/handlers reference the `Core` location; no reference resolves against the old `Api` namespace (SC-005).

## Authorization contract (unchanged, Api-owned)

`IActivityPublishingAuthorizationContext` keeps its single implementation `HttpContextActivityPublishingAuthorizationContext`, registered **only** by the Api feature. It is a transport concern; the engine does not participate. No neutral/alternate implementation is introduced by this refactor.
