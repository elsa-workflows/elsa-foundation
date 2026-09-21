# Extension points — API endpoint layer

The endpoint-class bases, the module-local mapper, the request binder, and the module-owned failure
pipeline ship in the [NativeEndpoints](https://www.nuget.org/packages/NativeEndpoints) package.
`Elsa.Api.AspNetCore` carries what stays Elsa's: endpoint ownership, security disposition, authoring
model, and host-credential enforcement, plus the operation convention that attaches them (see
`ElsaEndpointConventions`). Everything below is attached by the owning module through standard DI or
class-level attributes; the contracts named here are the package's, and every owner reaches them by
calling `services.AddElsaEndpoints()` in its feature.

## Failure pipeline contracts

The operation pipeline handles a dispatch failure in three stages: module fault renderers first,
then exception translation into the owner's problem shape, then a sanitized generic 500. Each
service resolves **keyed by the owner id first**, so a host composing several modules keeps each
module's own wire shapes; the unkeyed registration remains the single-module fallback.

| Contract | Kind and registration | Selection semantics | Known implementations |
|---|---|---|---|
| `IEndpointFaultRenderer` | Enumerable; register keyed by owner id (unkeyed = fallback). | Consulted before translation; the first renderer returning `true` owns the response end to end. Scope shapes with endpoint metadata read via `HttpContext.GetEndpoint()`. | `WorkflowPublishingFaultRenderer` (Publishing), `ActivitiesDesignFaultRenderer` (Activities Design) |
| `IEndpointExceptionTranslator` | Enumerable; register keyed by owner id (unkeyed = fallback). | Consulted in registration order after renderers decline; the first non-null `EndpointProblem` wins. Exception-to-status mapping is domain knowledge and stays with the module that defines the exceptions. | `WorkflowDesignExceptionTranslator` (Workflows Design), `WorkflowPublishingExceptionTranslator` (Publishing) |
| `IEndpointProblemWriter` | Single per owner; register keyed by owner id (unkeyed = fallback). | Writes every `EndpointProblem` — binder failures included — in the owner's established error shape, which is part of its published contract. | `WorkflowDesignProblemWriter`, `WorkflowPublishingProblemWriter`, `ActivitiesDesignProblemWriter` |

## Endpoint composition contracts

| Contract | Kind and registration | Purpose | Known implementations |
|---|---|---|---|
| `IEndpointConventionAttribute` | Class-level attribute on an endpoint class; applied by the mapper after mapping. | Lets host layers contribute endpoint conventions — authorization above all — without this layer referencing them. | `RequirePermissionAttribute` (Foundation Identity) applies the full permission requirement so the attribute and the imperative `RequirePermission()` cannot drift. |
| `ApiEndpointOptions.Convention(...)` | Per-endpoint callback in `Configure`. | Registers any ordinary ASP.NET Core convention (`IEndpointConventionBuilder`) on the mapped endpoint — metadata markers, filters, CORS, rate limits. | Publishing and Activities Design attach their failure-shape marker metadata this way or via marker attributes built on `IEndpointConventionAttribute`. |
| `ModuleEndpointGroup.MapOperation<TMessage>` | Public pipeline seam. | Lets an external dispatch style (a mediator bridge, inline delegates) compose on the same bind → dispatch → translate → metadata pipeline without a parallel stack. | The retired `Elsa.Api.Mediator` bridge was built on it; the endpoint-class mapper uses the internal typed forms. |

## Unload-safety enforcement

`RequireStableOpenApi()` runs metadata lifetime validation as the final convention, fail-closed: an
unconfigured host keeps the guard. Hosts that deliberately opt out call
`SuppressOpenApiLifetimeEnforcement()`; anything else enforces the boundary. The request binder's
constructor cache is weak-keyed (`ConditionalWeakTable`) so contract types in collectible module
assemblies are never rooted by this layer.

These are deliberate, narrow seams. The binder covers exactly the shapes first-party endpoints use
and throws on anything else; widening it — like widening any contract above — is a deliberate act
that returns through the framework's decision records, not an implicit extension.
