using Microsoft.AspNetCore.Routing;
using NativeEndpoints;

namespace Elsa.Etl.Runtime.Api;

/// <summary>Maps the finite ETL runtime inspection and cancellation API.</summary>
public static class EtlRuntimeApi
{
    internal const string OwnerId = "Elsa.Etl.Runtime.Api";

    public static void MapEtlRuntimeApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapEndpointGroup(OwnerId, EtlRuntimeApiJsonContext.WireContext, jsonContentType: "application/json")
            .MapEndpointsFrom(typeof(EtlRuntimeApi).Assembly);
    }
}
