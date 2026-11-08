using System.Security.Cryptography;
using Elsa.Api.AspNetCore;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NativeEndpoints;

namespace Elsa.Etl.Api;

/// <summary>Transport plumbing shared by the independently composed ETL APIs.</summary>
internal static class EtlApiServices
{
    private static readonly string ProcessCursorKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    internal static void Add(IServiceCollection services, string? cursorSigningKey, string ownerId)
    {
        services.AddElsaEndpoints();
        services.AddDynamicEndpointApiExplorerRefresh();
        services.AddHttpContextAccessor();
        services.AddPersistenceCore();
        // Each API owns its key configuration; composing both must not silently select the first key.
        services.TryAddKeyedSingleton(ownerId, new EtlCursorCodec(cursorSigningKey ?? ProcessCursorKey));
        services.TryAddScoped<EtlRequestScope>();
        services.TryAddKeyedSingleton<IEndpointProblemWriter, EtlProblemWriter>(ownerId);
        services.TryAddKeyedSingleton<IEndpointFaultRenderer, EtlFaultRenderer>(ownerId);
    }
}
