using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Http.Models;

/// <summary>
/// Represents the context for authorizing an HTTP endpoint.
/// </summary>
public record AuthorizeHttpEndpointContext(HttpContext HttpContext, object Workflow, string? Policy = default);