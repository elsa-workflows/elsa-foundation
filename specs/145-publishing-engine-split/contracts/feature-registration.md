# Contracts — Feature registration surface (post-split)

The observable "contract" of this refactor is the DI registration surface of the two features and the relocated mediator message. These are the assertions the tests pin.

## Engine feature — `WorkflowsPublishing`

Composing **only** `WorkflowsPublishing` (no Api) MUST make all of the following resolvable, and MUST mount **zero** HTTP endpoints:

- `IWorkflowExecutableCompiler`, `IWorkflowExecutableStore`, `IWorkflowExecutableSourceReferenceStore`, `IWorkflowExecutableSourceReferenceReader`, `IExecutableActivityTemplateReader`
- `IPublicationSlotStore`, `IPublicationRecordStore`, `IPublicationPolicyStore`, `IPublicationProjectionIntentStore`, `IPublicationPolicyResolver`, `IPublicationPreflightService`, `IPublicationProjectionPreparer`, `IPublicationActivator`
- `IWorkflowDefinitionVersionLayoutStore` (fallback), `IActivityStructureService`, `IWorkflowDefinitionPermanentDeletionGuard`
- `IActivityTemplateProviderCompilerRegistry`, `IActivityTemplateDependencyDiscovererRegistry`, `IActivityTemplateCompiler`, `IActivityDefinitionPublisher`, `IActivitySourceVersionPublisher`, `IActivityDraftTestRunService`, `IActivityDraftTestRunStore`
- `IRequestHandler<PublishWorkflow, PublishedWorkflowView>` (the relocated handler) + sibling publish/test-run/slot-lifecycle handlers
- Event handlers for `OnExecutableCompilationCollecting`, `OnExecutableNodeMetadataCollecting`
- `IActivityPublishingAuthorizationContext` → **neutral default** (engine-only shell)
- `TimeProvider`

Behavioural contract: sending `PublishWorkflow(versionId)` in-process compiles the executable, writes a single **live Published** `WorkflowExecutableSourceReference`, and indexes triggers — identical to the pre-split publish result.

## Transport feature — `WorkflowsPublishingApi`

Composing `WorkflowsPublishingApi` MUST:

- Resolve **everything** the engine resolves (inherited via `base.ConfigureServices`) — the existing `WorkflowsPublishingApiFeatureTests` presence assertions pass unchanged.
- Additionally register: `IHttpContextAccessor`; `IActivityPublishingAuthorizationContext` → `HttpContextActivityPublishingAuthorizationContext` (**overrides** the engine's neutral default); the publishing FastEndpoints endpoints; `AddApiCapability(PublishingApiCapabilities.StaticDeclaration)`; `AddApiCapabilitySource<ConversionProfilesCapabilitySource>()`.
- Mount the **same** publish/test-run/slot/preflight/inspection endpoints (routes + behaviour) as before the split.
- Register **zero** engine services in its own `ConfigureServices` body beyond the base call, the HTTP override, endpoints, and capabilities (SC-003).

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

## Replacement-contract rule (framework §2.6.2)

`IActivityPublishingAuthorizationContext` has exactly one active implementation per shell: the engine's neutral default, or the Api's HttpContext impl when Api is composed. The Api override uses `RemoveAll<IActivityPublishingAuthorizationContext>()` + `AddScoped<…, HttpContextActivityPublishingAuthorizationContext>()` — no silent last-write-wins.
