using Elsa.Tagging.Core.Contracts;
using Microsoft.AspNetCore.Http;

namespace Elsa.Tagging.Api.Services;

/// <summary>Bridges the host request boundary into catalog audit facts without leaking HTTP into the core domain.</summary>
internal sealed class HttpTagDefinitionAuditContext(IHttpContextAccessor accessor) : ITagDefinitionAuditContext
{
    private HttpContext? Context => accessor.HttpContext;

    public string Actor => Context?.User.Identity?.Name ?? "anonymous";
    public string CorrelationId => Context?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Context?.TraceIdentifier
        ?? "unavailable";
}
