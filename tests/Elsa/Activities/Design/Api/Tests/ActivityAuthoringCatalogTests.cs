using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Activities.Design.Api;
using Elsa.Api.FastEndpoints.Constants;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

/// <summary>RED contract for the unified, persisted, availability-aware authoring catalog.</summary>
public sealed class ActivityAuthoringCatalogTests
{
    private const string CatalogRoute = "design/activities/catalog";

    [Fact]
    public void Activity_design_owns_one_secured_canonical_authoring_catalog()
    {
        var endpoint = FindCatalogEndpoint();

        Assert.Contains(PermissionNames.ActivityDesignRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Catalog_query_defaults_to_addable_and_supports_privileged_all_mode()
    {
        var (request, _) = EndpointContract(FindCatalogEndpoint());
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
        var (_, response) = EndpointContract(FindCatalogEndpoint());
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
            "AuthoringTemplate");

        var inputDescriptor = CollectionElementType(descriptor.GetProperty("Inputs")!.PropertyType);
        AssertProperties(inputDescriptor, "ReferenceKey", "Name", "Type", "IsNullable");
        Assert.Equal(typeof(bool?), inputDescriptor.GetProperty("IsNullable")!.PropertyType);
        var inputConstructor = inputDescriptor.GetConstructors().Single();
        Assert.Equal(13, inputConstructor.GetParameters().Length);
        Assert.DoesNotContain(inputConstructor.GetParameters(), parameter =>
            StringComparer.OrdinalIgnoreCase.Equals(parameter.Name, "IsNullable"));
        Assert.Contains(inputDescriptor.GetMethods(), method =>
            method.Name == "Deconstruct" && method.GetParameters().Length == 13);
    }

    [Fact]
    public void Availability_contract_can_explain_installed_but_unavailable_entries()
    {
        var (_, response) = EndpointContract(FindCatalogEndpoint());
        var descriptor = CollectionElementType(response.GetProperty("Activities")!.PropertyType);

        Assert.Equal(typeof(bool), descriptor.GetProperty("Available")!.PropertyType);
        Assert.Equal(typeof(string), Nullable.GetUnderlyingType(descriptor.GetProperty("AvailabilityReason")!.PropertyType) ?? descriptor.GetProperty("AvailabilityReason")!.PropertyType);
    }

    [Fact]
    public void Catalog_handler_reads_persisted_versions_and_applies_availability_policy()
    {
        var handler = typeof(ActivitiesDesignApiFeature).Assembly.GetType(
            "Elsa.Activities.Design.Api.Handlers.ListActivityAuthoringCatalogRequestHandler");
        Assert.NotNull(handler);
        var dependencies = handler!.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray();

        Assert.Contains("Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionStore", dependencies);
        Assert.Contains("Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionStore", dependencies);
        Assert.Contains("Elsa.Activities.Design.Core.Contracts.IActivityAvailabilityEvaluator", dependencies);
        Assert.Contains("Elsa.Activities.Design.Core.Stores.IActivityAvailabilitySettingsStore", dependencies);
    }

    private static BaseEndpoint FindCatalogEndpoint()
    {
        var endpoint = typeof(ActivitiesDesignApiFeature).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(BaseEndpoint).IsAssignableFrom(type))
            .Select(CreateEndpoint)
            .SingleOrDefault(candidate => candidate.Definition.Routes.Contains(CatalogRoute, StringComparer.Ordinal));
        Assert.NotNull(endpoint);
        return endpoint;
    }

    private static (Type Request, Type Response) EndpointContract(BaseEndpoint endpoint)
    {
        for (var current = endpoint.GetType().BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GenericTypeArguments.Length >= 2 &&
                current.GetGenericTypeDefinition().Namespace == "Elsa.Api.FastEndpoints.Abstractions")
                return (current.GenericTypeArguments[0], current.GenericTypeArguments[1]);
        }
        throw new InvalidOperationException($"Endpoint '{endpoint.GetType().FullName}' does not declare a request/response contract.");
    }

    private static BaseEndpoint CreateEndpoint(Type endpointType)
    {
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single().GetParameters().Select(parameter => ResolveDependency(parameter.ParameterType)).ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var rest] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) &&
                              rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var endpoint = (BaseEndpoint)create.Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), dependencies])!;
        endpoint.Configure();
        return endpoint;
    }

    private static object ResolveDependency(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var nullLogger = typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments[0]);
            return (nullLogger.GetProperty("Instance")?.GetValue(null) ?? nullLogger.GetField("Instance")?.GetValue(null))!;
        }
        if (type.IsInterface)
            return DispatchProxy.Create(type, typeof(NoopProxy));
        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static Type CollectionElementType(Type collectionType) =>
        collectionType.IsArray
            ? collectionType.GetElementType()!
            : collectionType.GetInterfaces().Append(collectionType)
                .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .GenericTypeArguments[0];

    private static void AssertProperties(Type type, params string[] names) =>
        Assert.All(names, name => Assert.NotNull(type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)));

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A contract-only endpoint test must not invoke dependencies.");
    }
}
