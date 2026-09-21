using Microsoft.AspNetCore.Http;

namespace Elsa.Http.Core.Models;

/// <summary>
/// Represents the context for authorizing an inbound HTTP endpoint request (spec 089 sub-unit C).
/// </summary>
/// <remarks>
/// Carries only what the request middleware can supply for a single-definition claimant: the live
/// <see cref="HttpContext"/> and the endpoint's authorization <see cref="Policy"/>. Pre-release note: the
/// former Runtime-specific <c>Workflow</c> resource member was dropped when the contract moved to
/// <c>Elsa.Http.Core</c> — the middleware authorizes an inbound request before any workflow instance exists,
/// so there is no protected workflow resource to hand a policy. A handler that needs a resource-scoped policy
/// evaluates against the authenticated user alone (or overrides the handler).
/// </remarks>
public record AuthorizeHttpEndpointContext(HttpContext HttpContext, string? Policy = default);
