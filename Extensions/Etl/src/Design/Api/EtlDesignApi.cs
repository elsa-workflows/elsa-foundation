using Microsoft.AspNetCore.Routing;
using NativeEndpoints;

namespace Elsa.Etl.Design.Api;

/// <summary>Maps the finite ETL authoring API.</summary>
public static class EtlDesignApi
{
    internal const string OwnerId = "Elsa.Etl.Design.Api";

    public static void MapEtlDesignApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapEndpointGroup(OwnerId, EtlDesignJsonContext.WireContext, jsonContentType: "application/json")
            .MapEndpointsFrom(typeof(EtlDesignApi).Assembly);
    }
}
