using Microsoft.AspNetCore.Routing;
using System.Text.Json.Serialization;

namespace Elsa.Api.Mediator;

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
    /// exactly as the hand-written endpoints did.
    /// </param>
    public static ModuleEndpointGroup MapModuleEndpoints(
        this IEndpointRouteBuilder endpoints,
        string ownerId,
        JsonSerializerContext jsonContext)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(jsonContext);
        return new(endpoints, ownerId, jsonContext);
    }
}
