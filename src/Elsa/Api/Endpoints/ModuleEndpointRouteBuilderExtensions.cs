using Microsoft.AspNetCore.Routing;
using System.Text.Json.Serialization;

namespace Elsa.Api.Endpoints;

/// <summary>Entry point for mapping a module's routes onto its mediator requests and commands.</summary>
public static class ModuleEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Opens a mapping group for one owning module.
    /// </summary>
    /// <param name="endpoints">The standard route builder the module was handed.</param>
    /// <param name="ownerId">The stable owning module identifier.</param>
    /// <param name="jsonContext">
    /// The owner's source-generated serializer context. It is passed explicitly rather than resolved
    /// from HTTP JSON options so the module's own generated metadata governs both binding and writing,
    /// exactly as the hand-written endpoints did. A module whose effective serializer carries runtime
    /// configuration (converters, naming policies) passes a context constructed over those options.
    /// </param>
    /// <param name="jsonContentType">
    /// The exact Content-Type written on success responses. The charset suffix is part of a module's
    /// published wire contract, so it is configured rather than assumed.
    /// </param>
    /// <param name="tag">
    /// The OpenAPI tag applied to every operation in the group. Defaults to the owner id; modules
    /// whose published documents tag with the host application name resolve it at composition time
    /// and pass it here.
    /// </param>
    public static ModuleEndpointGroup MapModuleEndpoints(
        this IEndpointRouteBuilder endpoints,
        string ownerId,
        JsonSerializerContext jsonContext,
        string jsonContentType = "application/json; charset=utf-8",
        string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(jsonContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonContentType);
        return new(endpoints, ownerId, jsonContext, jsonContentType, tag);
    }
}
