using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

using ProfilesEndpoint = Elsa.Workflows.Publishing.Api.Endpoints.ValueConversionProfiles.List.Endpoint;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ValueConversionProfilesEndpointTests
{
    [Fact]
    public void Endpoint_has_pinned_route_and_publishing_read_permission()
    {
        var endpoint = PublishingMinimalApiTestSurface.Named("ListValueConversionProfiles");

        Assert.Equal("publishing/value-conversion/profiles", endpoint.RoutePattern.RawText?.TrimStart('/'));
        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var parsed = new PermissionPolicyCodec().Parse(authorization.Policy!);
        Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
        Assert.Equal(PermissionKey.Normalize(WorkflowPublishingPermissions.Read),
            Assert.Single(Assert.IsType<PermissionPolicyDescriptor>(parsed.Descriptor).Permissions));
    }

    [Fact]
    public void Every_minimal_api_request_dto_is_bindable_by_the_owner_source_generated_context()
    {
        var requestTypes = PublishingMinimalApiTestSurface.Map()
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>())
            .Select(metadata => metadata.RequestType)
            .Where(type => type is not null)
            .Cast<Type>()
            .Distinct()
            .ToArray();

        Assert.NotEmpty(requestTypes);
        var contextType = typeof(WorkflowsPublishingApiFeature).Assembly.GetType(
            "Elsa.Workflows.Publishing.Api.WorkflowsPublishingJsonContext",
            throwOnError: true)!;
        var context = Assert.IsAssignableFrom<JsonSerializerContext>(
            contextType.GetProperty("Default")!.GetValue(null));
        Assert.All(requestTypes, requestType =>
            Assert.NotNull(context.Options.GetTypeInfo(requestType)));
    }

    [Fact]
    public async Task Default_host_surfaces_the_built_in_json_and_xml_profiles()
    {
        var handler = new ProfilesEndpoint(BuiltInValueConversionProfileRegistry.Instance);

        var response = await handler.HandleAsync(new Requests.ListValueConversionProfiles(), CancellationToken.None);

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
        var handler = new ProfilesEndpoint(new StubProfileRegistry([hostProfile]));

        var response = await handler.HandleAsync(new Requests.ListValueConversionProfiles(), CancellationToken.None);

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
