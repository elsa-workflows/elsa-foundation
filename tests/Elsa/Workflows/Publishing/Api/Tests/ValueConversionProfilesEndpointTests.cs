using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Services;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Runtime.Core.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ValueConversionProfilesEndpointTests
{
    [Fact]
    public void Endpoint_has_pinned_route_and_publishing_read_permission()
    {
        var definition = ConfiguredDefinition("Elsa.Workflows.Publishing.Api.Endpoints.ListValueConversionProfiles");

        Assert.Contains("publishing/value-conversion/profiles", definition.Routes);
        Assert.Contains(PermissionNames.WorkflowPublishingRead, definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, definition.AllowedPermissions!);
        Assert.Null(definition.AnonymousVerbs);
    }

    [Fact]
    public void Every_endpoint_request_dto_in_the_assembly_is_bindable_by_fastendpoints()
    {
        // FastEndpoints' RequestBinder<T> static initializer rejects DTOs without publicly accessible
        // properties at first request, not at startup; parameterless endpoints must use EmptyRequest.
        var endpointTypes = typeof(ConversionProfilesCapabilitySource).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(BaseEndpoint).IsAssignableFrom(type));

        foreach (var endpointType in endpointTypes)
        {
            var requestType = RequestTypeOf(endpointType);
            var exception = Record.Exception(() => Activator.CreateInstance(typeof(RequestBinder<>).MakeGenericType(requestType)));
            Assert.True(exception is null, $"FastEndpoints cannot bind request DTO '{requestType}' of endpoint '{endpointType}': {exception?.InnerException ?? exception}");
        }
    }

    private static Type RequestTypeOf(Type endpointType)
    {
        for (var type = endpointType; type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Endpoint<,>))
                return type.GenericTypeArguments[0];
        }
        throw new InvalidOperationException($"Endpoint '{endpointType}' does not derive from FastEndpoints.Endpoint<TRequest, TResponse>.");
    }

    [Fact]
    public async Task Default_host_surfaces_the_built_in_json_and_xml_profiles()
    {
        var handler = new ListValueConversionProfilesRequestHandler(BuiltInValueConversionProfileRegistry.Instance);

        var response = await handler.Handle(new Requests.ListValueConversionProfiles(), CancellationToken.None);

        Assert.Equal(
            [("elsa.json", "1"), ("elsa.xml", "1")],
            response.Items.Select(item => (item.Profile.Id, item.Profile.Version)));
        var json = response.Items.Single(item => item.Profile.Id == "elsa.json");
        Assert.Contains(ValueRepresentation.FormattedContent, json.SupportedSourceRepresentations);
        Assert.Contains(ValueRepresentation.StructuredValue, json.SupportedSourceRepresentations);
        Assert.Equal(["*"], json.SupportedTargetAliases);
    }

    [Fact]
    public async Task Custom_registry_registration_surfaces_host_profiles()
    {
        var hostProfile = new ValueConversionProfileDefinition(
            new ValueConversionProfileReference("partner.customer-json", "3"),
            new HashSet<ValueRepresentation> { ValueRepresentation.FormattedContent },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Acme.Customer" });
        var handler = new ListValueConversionProfilesRequestHandler(new StubProfileRegistry([hostProfile]));

        var response = await handler.Handle(new Requests.ListValueConversionProfiles(), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("partner.customer-json", item.Profile.Id);
        Assert.Equal("3", item.Profile.Version);
        Assert.Equal([ValueRepresentation.FormattedContent], item.SupportedSourceRepresentations);
        Assert.Equal(["Acme.Customer"], item.SupportedTargetAliases);
    }

    [Fact]
    public void Response_serializes_with_camel_case_and_a_nested_profile_identity()
    {
        var view = new ValueConversionProfileView(
            new ValueConversionProfileReferenceView("elsa.json", "1"),
            [ValueRepresentation.FormattedContent],
            ["*"]);

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(new ValueConversionProfilesResponse([view]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var profile = document.RootElement.GetProperty("items")[0].GetProperty("profile");
        Assert.Equal("elsa.json", profile.GetProperty("id").GetString());
        Assert.Equal("1", profile.GetProperty("version").GetString());
        Assert.Equal("FormattedContent", document.RootElement.GetProperty("items")[0]
            .GetProperty("supportedSourceRepresentations")[0].GetString());
        Assert.Equal("*", document.RootElement.GetProperty("items")[0]
            .GetProperty("supportedTargetAliases")[0].GetString());
    }

    [Fact]
    public async Task Source_merges_conversion_profiles_relation_into_the_expressions_capability()
    {
        var services = new ServiceCollection();
        services.AddApiCapabilities();
        // Mirror the wire declaration the expressions API feature ships, without a cross-feature project reference.
        services.AddApiCapability(new ApiCapabilityDeclaration(
            "elsa.api.expressions",
            1,
            [new("expression-descriptors", "expressions/descriptors"), new("variable-types", "expressions/variable-types")],
            "ExpressionsApi"));
        services.AddApiCapabilitySource<ConversionProfilesCapabilitySource>();
        await using var provider = services.BuildServiceProvider();

        var document = await provider.GetRequiredService<IApiCapabilityCatalog>().GetAsync();

        var expressions = Assert.Single(document.Capabilities, capability => capability.Id == "elsa.api.expressions");
        var link = Assert.Single(expressions.Links, candidate => candidate.Rel == "conversion-profiles");
        Assert.Equal("publishing/value-conversion/profiles", link.Href);
        Assert.False(link.Templated);
        Assert.Contains(expressions.Links, candidate => candidate.Rel == "expression-descriptors");
        Assert.Contains(expressions.Links, candidate => candidate.Rel == "variable-types");
    }

    [Fact]
    public async Task Source_does_not_manufacture_an_expressions_capability_when_expressions_is_absent()
    {
        var services = new ServiceCollection();
        services.AddApiCapabilities();
        services.AddApiCapability(new ApiCapabilityDeclaration(
            "elsa.api.publishing", 1, [new("workflow-publish", "publishing/workflows/{versionId}/publish", true)], "WorkflowsPublishingApi"));
        services.AddApiCapabilitySource<ConversionProfilesCapabilitySource>();
        await using var provider = services.BuildServiceProvider();

        var document = await provider.GetRequiredService<IApiCapabilityCatalog>().GetAsync();

        Assert.DoesNotContain(document.Capabilities, capability => capability.Id == "elsa.api.expressions");
    }

    private static EndpointDefinition ConfiguredDefinition(string endpointTypeName)
    {
        var endpointType = typeof(ConversionProfilesCapabilitySource).Assembly.GetType(endpointTypeName, throwOnError: true)!;
        var dependencies = endpointType
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => ResolveDependency(parameter.ParameterType))
            .ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(DefaultHttpContext)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);

        using var serviceProvider = new ServiceCollection().AddServicesForUnitTesting().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var endpoint = (BaseEndpoint)create.Invoke(null, [httpContext, dependencies])!;
        endpoint.Configure();
        return endpoint.Definition;
    }

    private static object ResolveDependency(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var nullLoggerType = typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments[0]);
            return (nullLoggerType.GetProperty(nameof(NullLogger<object>.Instance))?.GetValue(null)
                    ?? nullLoggerType.GetField(nameof(NullLogger<object>.Instance))?.GetValue(null))!;
        }
        if (type.IsInterface)
            return DispatchProxy.Create(type, typeof(NoopProxy));
        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A configuration-only endpoint test must not invoke dependencies.");
    }

    private sealed class StubProfileRegistry(IReadOnlyCollection<ValueConversionProfileDefinition> definitions)
        : IValueConversionProfileRegistry
    {
        public bool TryGet(ValueConversionProfileReference profile, out ValueConversionProfileDefinition definition)
        {
            definition = definitions.FirstOrDefault(candidate =>
                candidate.Profile.Id == profile.Id && candidate.Profile.Version == profile.Version)!;
            return definition is not null;
        }

        public IReadOnlyCollection<ValueConversionProfileDefinition> List() => definitions;
    }
}
