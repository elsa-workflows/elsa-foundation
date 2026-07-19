using System.Reflection;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Tagging.Api.Capabilities;
using Elsa.Tagging.Api;
using Elsa.Tagging.Api.Requests;
using Elsa.Tagging.Core.Contracts;
using Elsa.Tagging.Core.Models;
using Elsa.Tagging.Persistence.Groundwork.DependencyInjection;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Tagging.Tests;

public sealed class TaggingApiCapabilitiesTests
{
    [Fact]
    public void Advertises_the_v1_catalog_link_at_the_frozen_route()
    {
        var capability = TaggingApiCapabilities.StaticDeclaration;

        Assert.Equal("elsa.api.tagging", capability.CapabilityId);
        Assert.Equal(1, capability.ContractMajorVersion);
        Assert.Contains(
            capability.Links,
            link => link.Rel == "tag-definitions"
                    && link.Href == "tagging/definitions"
                    && !link.Templated);
        Assert.Contains(
            capability.Links,
            link => link.Rel == "tag-definition-values"
                    && link.Href == "tagging/definitions/{tagDefinitionId}/values"
                    && link.Templated);
    }

    [Fact]
    public async Task Operational_capability_requires_durable_catalog_persistence()
    {
        Assert.Empty(await new TaggingOperationalCapabilitySource().GetCapabilitiesAsync());

        var markerOnly = await new TaggingOperationalCapabilitySource(new DurableCatalogPersistence()).GetCapabilitiesAsync();
        var markerCapability = Assert.Single(markerOnly);
        Assert.DoesNotContain(markerCapability.Links, link => link.Rel == "tag-definition-values");

        var advertised = await new TaggingOperationalCapabilitySource(
            new DurableCatalogPersistence(),
            new DurableControlledValuePersistence()).GetCapabilitiesAsync();

        Assert.Equal(TaggingApiCapabilities.StaticDeclaration, Assert.Single(advertised));
    }

    [Fact]
    public async Task Feature_does_not_advertise_the_in_memory_catalog_but_does_advertise_groundwork_persistence()
    {
        var ephemeral = new ServiceCollection();
        ephemeral.AddApiCapabilities();
        new TaggingApiFeature().ConfigureServices(ephemeral);
        using var ephemeralProvider = ephemeral.BuildServiceProvider();
        using var ephemeralScope = ephemeralProvider.CreateScope();

        var unavailable = await ephemeralScope.ServiceProvider
            .GetRequiredService<IApiCapabilityCatalog>()
            .GetAsync();

        Assert.DoesNotContain(unavailable.Capabilities, capability => capability.Id == TaggingApiCapabilities.CapabilityId);

        var durable = new ServiceCollection();
        durable.AddApiCapabilities();
        new TaggingApiFeature().ConfigureServices(durable);
        durable.AddGroundworkTaggingStore();
        using var durableProvider = durable.BuildServiceProvider();
        using var durableScope = durableProvider.CreateScope();

        var available = await durableScope.ServiceProvider
            .GetRequiredService<IApiCapabilityCatalog>()
            .GetAsync();

        Assert.Equal(TaggingApiCapabilities.CapabilityId, Assert.Single(available.Capabilities).Id);
    }

    [Fact]
    public async Task Create_maps_validation_failures_to_bad_request()
    {
        var endpoint = CreateEndpoint(
            "Elsa.Tagging.Api.Endpoints.Definitions.Create",
            new ThrowingTagDefinitionManager(new ArgumentException("The canonical key is invalid.")));

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            HandleAsync(endpoint, new CreateTagDefinitionApiRequest { CanonicalKey = "not valid" }));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Create_maps_duplicate_catalog_entries_to_conflict()
    {
        var endpoint = CreateEndpoint(
            "Elsa.Tagging.Api.Endpoints.Definitions.Create",
            new ThrowingTagDefinitionManager(new InvalidOperationException("The canonical key already exists.")));

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            HandleAsync(endpoint, new CreateTagDefinitionApiRequest { CanonicalKey = "risk.pii" }));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task Catalog_endpoint_fails_closed_without_durable_persistence()
    {
        var endpoint = CreateEndpoint(
            "Elsa.Tagging.Api.Endpoints.Definitions.Create",
            new ThrowingTagDefinitionManager(new InvalidOperationException("Must not be called.")),
            durable: false);

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            HandleAsync(endpoint, new CreateTagDefinitionApiRequest { CanonicalKey = "risk.pii" }));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task Controlled_value_endpoint_fails_closed_with_only_definition_catalog_persistence()
    {
        var endpoint = CreateEndpoint(
            "Elsa.Tagging.Api.Endpoints.Definitions.CreateValue",
            new ThrowingControlledTagValueManager(),
            new DurableCatalogPersistence(),
            null!,
            new AtomicControlledValues());

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            HandleAsync(endpoint, new CreateControlledTagValueApiRequest
            {
                TagDefinitionId = "tag-environment",
                CanonicalKey = "production"
            }));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task Update_maps_validation_failures_to_bad_request()
    {
        var endpoint = CreateEndpoint(
            "Elsa.Tagging.Api.Endpoints.Definitions.Update",
            new ThrowingTagDefinitionManager(new ArgumentException("The display name is invalid.")),
            ifMatch: "\"td:1\"");

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            HandleAsync(endpoint, new UpdateTagDefinitionApiRequest
            {
                TagDefinitionId = "tag-1",
                DisplayName = ""
            }));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    private static BaseEndpoint CreateEndpoint(
        string endpointTypeName,
        ITagDefinitionManager manager,
        string? ifMatch = null,
        bool durable = true)
    {
        var dependencies = durable
            ? new object[] { manager, new DurableCatalogPersistence() }
            : [manager, null!];
        Action<DefaultHttpContext> configureContext = context =>
        {
            context.Response.Body = new MemoryStream();
            if (ifMatch is not null)
                context.Request.Headers.IfMatch = ifMatch;
        };
        return CreateEndpoint(endpointTypeName, configureContext, dependencies);
    }

    private static BaseEndpoint CreateEndpoint(string endpointTypeName, params object[] dependencies) =>
        CreateEndpoint(
            endpointTypeName,
            context => context.Response.Body = new MemoryStream(),
            dependencies);

    private static BaseEndpoint CreateEndpoint(
        string endpointTypeName,
        Action<DefaultHttpContext> configureContext,
        object[] dependencies)
    {
        var endpointType = typeof(CreateTagDefinitionApiRequest).Assembly.GetType(
            endpointTypeName,
            throwOnError: true)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(Action<DefaultHttpContext>)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        return (BaseEndpoint)create.Invoke(null, [configureContext, dependencies])!;
    }

    private static Task HandleAsync(BaseEndpoint endpoint, object request) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [request.GetType(), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

    private sealed class ThrowingTagDefinitionManager(Exception exception) : ITagDefinitionManager
    {
        public ValueTask<TagDefinition> CreateAsync(CreateTagDefinitionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<TagDefinition>(exception);

        public ValueTask<IReadOnlyList<TagDefinition>> ListAsync(TagDefinitionListRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TagDefinitionRevisionedRecord>> ListWithRevisionsAsync(
            TagDefinitionListRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<TagDefinitionRevisionedRecord?> FindWithRevisionAsync(string tagDefinitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TagDefinitionRevisionedRecord> UpdateAsync(
            string tagDefinitionId,
            UpdateTagDefinitionRequest request,
            string expectedRevision,
            CancellationToken cancellationToken = default) => ValueTask.FromException<TagDefinitionRevisionedRecord>(exception);
    }

    private sealed class ThrowingControlledTagValueManager : IControlledTagValueManager
    {
        public ValueTask<ControlledTagValue> CreateAsync(
            CreateControlledTagValueRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called.");

        public ValueTask<IReadOnlyList<ControlledTagValueRevisionedRecord>> ListWithRevisionsAsync(
            ControlledTagValueListRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called.");

        public ValueTask<ControlledTagValueRevisionedRecord?> FindWithRevisionAsync(
            string controlledTagValueId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called.");

        public ValueTask<ControlledTagValueRevisionedRecord> UpdateAsync(
            string controlledTagValueId,
            UpdateControlledTagValueRequest request,
            string expectedRevision,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called.");
    }

    private sealed class AtomicControlledValues : IControlledTagValueAtomicChangeStore
    {
        public ValueTask<ControlledTagValueCreateResult> TryAddWithinLimitAndAppendAuditAsync(
            ControlledTagValue value,
            ControlledTagValueAuditRecord audit,
            int expectedCount,
            int maximumCount,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called.");

        public ValueTask<TagDefinitionSaveResult> SaveWithRevisionAndAppendAuditAsync(
            ControlledTagValue value,
            string expectedRevision,
            ControlledTagValueAuditRecord audit,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called.");
    }

    private sealed class DurableCatalogPersistence : ITagDefinitionCatalogPersistence;
    private sealed class DurableControlledValuePersistence : IControlledTagValueCatalogPersistence;
}
