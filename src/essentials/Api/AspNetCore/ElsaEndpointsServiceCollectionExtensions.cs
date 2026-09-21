using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeEndpoints;

namespace Elsa.Api.AspNetCore;

/// <summary>Registers the endpoint pipeline every Elsa module maps its group onto.</summary>
public static class ElsaEndpointsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the NativeEndpoints pipeline configured the way Elsa's published documents expect.
    /// </summary>
    /// <remarks>
    /// Every host that maps an Elsa endpoint group must call this: mapping a group without it throws.
    /// It is idempotent and safe to call from each module's own feature, which is how a composed host
    /// ends up correct without a central registration every module has to remember to be listed in.
    /// <para>
    /// Two deliberate departures from the package's defaults are installed here: Elsa's operation
    /// convention, which owns endpoint naming and the documented authorization responses (see
    /// <see cref="ElsaEndpointConventions.ElsaModuleOperation"/>), and Elsa's fallback problem
    /// writer, which keeps the failure shape owners without their own writer already publish.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddElsaEndpoints(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Registered before AddNativeEndpoints, which uses TryAdd: first registration wins, so this
        // is what displaces the package's ProblemDetails default rather than racing it.
        services.TryAddSingleton<IEndpointProblemWriter, ElsaFallbackEndpointProblemWriter>();
        services.AddNativeEndpoints(options => options.OperationConvention = ElsaEndpointConventions.ElsaModuleOperation);
        return services;
    }
}
