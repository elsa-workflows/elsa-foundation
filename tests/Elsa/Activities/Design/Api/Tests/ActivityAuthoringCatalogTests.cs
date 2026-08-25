using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>RED contract for the unified, persisted, availability-aware authoring catalog.</summary>
public sealed class ActivityAuthoringCatalogTests
{
    private const string CatalogRoute = "/design/activities/catalog";

    [Fact]
    public void Activity_design_owns_one_secured_canonical_authoring_catalog()
    {
        var endpoint = FindCatalogEndpoint();
        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var parsed = new PermissionPolicyCodec().Parse(authorization.Policy!);

        Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
        Assert.Equal([PermissionKey.Normalize(ActivityDesignPermissions.Read)], parsed.Descriptor!.Permissions);
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Fact]
    public void Catalog_query_defaults_to_addable_and_supports_privileged_all_mode()
    {
        var request = typeof(ListActivityAuthoringCatalog);
        var availability = request.GetProperty("Availability", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(availability);
        var constructorParameter = Assert.Single(request.GetConstructors().Single().GetParameters(), parameter =>
            StringComparer.OrdinalIgnoreCase.Equals(parameter.Name, "availability"));

        Assert.True(constructorParameter.HasDefaultValue);
        Assert.Equal("Addable", constructorParameter.DefaultValue?.ToString());
        Assert.Equal(new[] { "Addable", "All" }, Enum.GetNames(availability!.PropertyType));
    }

    [Fact]
    public void Catalog_response_is_normalized_for_one_call_editor_bootstrap()
    {
        var response = typeof(ActivityAuthoringCatalogView);
        var activities = response.GetProperty("Activities", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(activities);
        var descriptor = CollectionElementType(activities!.PropertyType);

        AssertProperties(
            descriptor,
            "ActivityVersionId",
            "ActivityTypeKey",
            "Version",
            "DisplayName",
            "Category",
            "Description",
            "ExecutionType",
            "Available",
            "AvailabilityReason",
            "Inputs",
            "Outputs",
            "Ports",
            "ContainerStructure",
            "AuthoringTemplate",
            "Provenance");

        var provenance = descriptor.GetProperty("Provenance")!.PropertyType;
        AssertProperties(provenance, "SourceKind", "SourceId", "FeatureId");

        var inputDescriptor = CollectionElementType(descriptor.GetProperty("Inputs")!.PropertyType);
        AssertProperties(inputDescriptor, "ReferenceKey", "Name", "Type", "CollectionKind", "IsNullable");
        Assert.Equal(typeof(bool), inputDescriptor.GetProperty("IsNullable")!.PropertyType);
        var inputConstructor = inputDescriptor.GetConstructors().Single();
        Assert.Equal(15, inputConstructor.GetParameters().Length);
        var nullability = Assert.Single(inputConstructor.GetParameters(), parameter =>
            StringComparer.OrdinalIgnoreCase.Equals(parameter.Name, "IsNullable"));
        Assert.False(nullability.HasDefaultValue);
        Assert.Contains(inputDescriptor.GetMethods(), method =>
            method.Name == "Deconstruct" && method.GetParameters().Length == 15);
        var portDescriptor = CollectionElementType(descriptor.GetProperty("Ports")!.PropertyType);
        AssertProperties(portDescriptor, "Name", "ReferenceKey", "Type", "IsBrowsable");
    }

    [Fact]
    public void Availability_contract_can_explain_installed_but_unavailable_entries()
    {
        var response = typeof(ActivityAuthoringCatalogView);
        var descriptor = CollectionElementType(response.GetProperty("Activities")!.PropertyType);

        Assert.Equal(typeof(bool), descriptor.GetProperty("Available")!.PropertyType);
        Assert.Equal(typeof(string), Nullable.GetUnderlyingType(descriptor.GetProperty("AvailabilityReason")!.PropertyType) ?? descriptor.GetProperty("AvailabilityReason")!.PropertyType);
    }

    [Fact]
    public void Outcome_ports_expose_explicit_or_name_based_stable_references()
    {
        var handler = typeof(ActivitiesDesignApiFeature).Assembly.GetType(
            "Elsa.Activities.Design.Api.Services.ActivityAuthoringCatalogReader")!;
        var toPorts = handler.GetMethod("ToPorts", BindingFlags.NonPublic | BindingFlags.Static)!;
        var facet = new ActivityDesignFacet("elsa.outcomes", "1", System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            ports = new object[]
            {
                new { referenceKey = "approved", name = "Approved", type = "outcome" },
                new { name = "Rejected", type = "outcome" }
            }
        }));

        var ports = Assert.IsAssignableFrom<IEnumerable<ActivityPortDescriptorView>>(toPorts.Invoke(null, [facet])).ToArray();

        Assert.Equal([("approved", "Approved"), ("Rejected", "Rejected")], ports.Select(x => (x.ReferenceKey, x.Name)));
    }

    [Fact]
    public void Catalog_handler_reads_persisted_versions_and_applies_availability_policy()
    {
        var handler = typeof(ActivitiesDesignApiFeature).Assembly.GetType(
            "Elsa.Activities.Design.Api.Services.ActivityAuthoringCatalogReader");
        Assert.NotNull(handler);
        var dependencies = handler!.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray();

        Assert.Contains("Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionStore", dependencies);
        Assert.Contains("Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionStore", dependencies);
        Assert.Contains("Elsa.Activities.Design.Core.Contracts.IActivityAvailabilityEvaluator", dependencies);
        Assert.Contains("Elsa.Activities.Design.Core.Stores.IActivityAvailabilitySettingsStore", dependencies);
        Assert.Contains("Elsa.Activities.Design.Api.Contracts.IActivityFeatureAttributionResolver", dependencies);
    }

    private static RouteEndpoint FindCatalogEndpoint()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        ActivitiesDesignApi.MapActivitiesDesignApi(routes);
        return Assert.Single(routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(), endpoint =>
            endpoint.RoutePattern.RawText == CatalogRoute &&
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is [var method] && method == HttpMethods.Get);
    }

    private static Type CollectionElementType(Type collectionType) =>
        collectionType.IsArray
            ? collectionType.GetElementType()!
            : collectionType.GetInterfaces().Append(collectionType)
                .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .GenericTypeArguments[0];

    private static void AssertProperties(Type type, params string[] names) =>
        Assert.All(names, name => Assert.NotNull(type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)));

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

}
