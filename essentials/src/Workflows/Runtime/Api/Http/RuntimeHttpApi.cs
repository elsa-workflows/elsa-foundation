using MinimalEndpoints;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Workflows.Runtime.Api;

/// <summary>The common Runtime HTTP routes for the classic and retained Actor deployments.</summary>
internal static class RuntimeHttpApi
{
    internal const string OwnerId = "Elsa.Workflows.Runtime.Api";

    internal static void MapRetainedEndpoints(IEndpointRouteBuilder endpoints, bool includeExecute, bool includeDelete, bool includeInputSources)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var api = endpoints.MapEndpointGroup(OwnerId, RuntimeHttpJsonContext.Default,
            jsonContentType: "application/json; charset=utf-8");
        MapEndpoints(api, includeExecute, includeDelete, includeInputSources);
    }

    internal static void MapEndpoints(EndpointGroup api, bool includeExecute, bool includeDelete, bool includeInputSources)
    {
        api.MapEndpoint<Endpoints.Executables.List.Endpoint>();
        api.MapEndpoint<Endpoints.Executables.Get.Endpoint>();
        api.MapEndpoint<Endpoints.Executables.Provenance.Endpoint>();
        if (includeInputSources) api.MapEndpoint<Endpoints.Executables.InputSources.Endpoint>();
        if (includeExecute) api.MapEndpoint<Endpoints.Executables.Execute.Endpoint>();
        api.MapEndpoint<Endpoints.ActivationSlots.List.Endpoint>();
        api.MapEndpoint<Endpoints.ActivationSlots.Get.Endpoint>();
        api.MapEndpoint<Endpoints.Instances.List.Endpoint>();
        api.MapEndpoint<Endpoints.Instances.ListPage.Endpoint>();
        api.MapEndpoint<Endpoints.Instances.Get.Endpoint>();
        if (includeDelete) api.MapEndpoint<Endpoints.Instances.Delete.Endpoint>();
        api.MapEndpoint<Endpoints.ActivityExecutions.List.Endpoint>();
        api.MapEndpoint<Endpoints.ActivityExecutions.Get.Endpoint>();
        api.MapEndpoint<Endpoints.History.Page.Endpoint>();
        api.MapEndpoint<Endpoints.History.Occurrences.Endpoint>();
    }
}
