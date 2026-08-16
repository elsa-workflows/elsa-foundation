using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa3.Activities.Design.Import.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class ReusableActivityImportHttpContractTests
{
    [Fact]
    public void Routes_are_exact_and_permission_guarded()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        using var app = builder.Build();

        ReusableActivityImportApi.MapReusableActivityImportApi(app);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        Assert.Equal(5, endpoints.Length);
        AssertEndpoint(endpoints, "POST", "migration/elsa3/reusable-activities/collections", "elsa3-import.manage");
        AssertEndpoint(endpoints, "GET", "migration/elsa3/reusable-activities/collections/{collectionHandle}/analysis", "elsa3-import.read");
        AssertEndpoint(endpoints, "POST", "migration/elsa3/reusable-activities/collections/{collectionHandle}/selection", "elsa3-import.read");
        AssertEndpoint(endpoints, "POST", "migration/elsa3/reusable-activities/collections/{collectionHandle}/apply", "elsa3-import.manage");
        AssertEndpoint(endpoints, "GET", "migration/elsa3/reusable-activities/imports/{idempotencyKey}", "elsa3-import.read");
    }

    private static void AssertEndpoint(
        IReadOnlyCollection<RouteEndpoint> endpoints,
        string verb,
        string route,
        string permission)
    {
        var endpoint = Assert.Single(endpoints, endpoint =>
            endpoint.RoutePattern.RawText == route &&
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(verb, StringComparer.OrdinalIgnoreCase) == true);
        var disposition = endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>();
        Assert.NotNull(disposition);
        Assert.Equal(EndpointSecurityDispositionKind.Permission, disposition!.Kind);
        Assert.Equal(new PermissionPolicyCodec().Format(PermissionPolicyDescriptor.Single(permission)), disposition.Value);
        Assert.Equal(
            new PermissionPolicyCodec().Format(PermissionPolicyDescriptor.Single(permission)),
            endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>()?.Policy);
    }

}
