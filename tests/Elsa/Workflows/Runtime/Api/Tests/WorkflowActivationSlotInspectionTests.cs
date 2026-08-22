using System.Reflection;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using PermissionNames = Elsa.Workflows.Runtime.Api.Authorization.WorkflowRuntimePermissions;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>
/// T117: the activation ledger is read here, and only read here.
/// </summary>
/// <remarks>
/// The slot is a runtime concept (§E2.2), so Runtime serves activation slots and Publishing serves publications.
/// These endpoints replace <c>ListPublicationSlotsEndpoint</c> / <c>GetPublicationSlotEndpoint</c>, which injected
/// <see cref="IWorkflowActivationAuthority"/> from inside the publishing API.
/// </remarks>
public sealed class WorkflowActivationSlotInspectionTests
{
    private const string SlotsRoute = "runtime/workflows/activation-slots/{definitionId}";
    private const string SlotRoute = "runtime/workflows/activation-slots/{definitionId}/{slotName}";
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SlotsRoute, typeof(ListWorkflowActivationSlots), typeof(WorkflowActivationSlotListView))]
    [InlineData(SlotRoute, typeof(GetWorkflowActivationSlot), typeof(WorkflowActivationSlotView))]
    public async Task Endpoints_expose_authenticated_runtime_read_contracts(string route, Type request, Type response)
    {
        var endpoint = MappedEndpoint(route);

        // The verb and the permission are the security-relevant half of the contract, so both are asserted
        // exactly rather than merely "at least".
        Assert.Equal<string>(["GET"], Verbs(endpoint));

        var expectedPolicy = new PermissionPolicyCodec().Format(
            PermissionPolicyDescriptor.Single(PermissionNames.WorkflowRuntimeRead));
        var dispositions = endpoint.Metadata.GetOrderedMetadata<EndpointSecurityDispositionMetadata>();
        Assert.Single(dispositions);
        Assert.Equal(EndpointSecurityDispositionKind.Permission, dispositions[0].Kind);
        Assert.Equal(expectedPolicy, dispositions[0].Value);

        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.Single(authorization);
        Assert.Equal(expectedPolicy, authorization[0].Policy);
        // Replaces the FastEndpoints `Definition.AnonymousVerbs is null` check: under Minimal APIs an endpoint
        // escapes its authorization policy only by carrying IAllowAnonymous metadata.
        Assert.DoesNotContain(endpoint.Metadata, item => item is IAllowAnonymous);

        // The response half of the contract is published as endpoint metadata.
        var produces = endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(response, produces.Type);

        // A Minimal API GET publishes no request DTO, so the request half is proven by driving the mapped
        // delegate and capturing the request/response pair it actually sends through the mediator.
        Assert.Equal((request, response), await CapturedContractAsync(endpoint));
    }

    [Fact]
    public void Capabilities_advertise_the_moved_relations_without_changing_the_major_version()
    {
        var links = RuntimeApiCapabilities.StaticDeclaration.Links.ToDictionary(link => link.Rel, StringComparer.Ordinal);

        Assert.Equal(SlotsRoute, links["workflow-activation-slots"].Href);
        Assert.True(links["workflow-activation-slots"].Templated);
        Assert.Equal(SlotRoute, links["workflow-activation-slot"].Href);
        Assert.True(links["workflow-activation-slot"].Templated);
        Assert.DoesNotContain("publication-slots", links.Keys, StringComparer.Ordinal);
        Assert.Equal(1, RuntimeApiCapabilities.StaticDeclaration.ContractMajorVersion);
    }

    [Fact]
    public async Task List_projects_explicit_ownership_and_answers_an_unknown_definition_with_an_empty_list()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1",
            "default",
            "import:mounted-artifacts:1",
            WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts"),
            0,
            Now));
        var handler = new ListWorkflowActivationSlotsRequestHandler(authority);

        var listed = await handler.Handle(new ListWorkflowActivationSlots("definition-1"), CancellationToken.None);
        var empty = await handler.Handle(new ListWorkflowActivationSlots("definition-absent"), CancellationToken.None);

        var view = Assert.Single(listed.Items);
        Assert.Equal("default", view.SlotName);
        Assert.Equal("import:mounted-artifacts:1", view.ActiveActivationId);
        // Ownership is read from the slot's explicit source field, never parsed out of the activation id (P3).
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, view.SourceKind);
        Assert.Equal("mounted-artifacts", view.SourceId);
        Assert.Equal(1, view.Revision);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task Get_renders_the_live_slot_and_reports_an_absent_one_as_not_found()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1",
            "default",
            "publication-1",
            WorkflowActivationSource.Publishing,
            0,
            Now));
        var handler = new GetWorkflowActivationSlotRequestHandler(authority);

        var view = await handler.Handle(new GetWorkflowActivationSlot("definition-1", "default"), CancellationToken.None);

        Assert.Equal(WorkflowActivationSlotIdentity.Create("definition-1", "default"), view.SlotId);
        Assert.Equal("publication-1", view.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, view.SourceKind);
        Assert.Null(view.SourceId);
        // A missing slot is a domain not-found the shared endpoint base renders as 404, never a raw fault (§2.23.5).
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            handler.Handle(new GetWorkflowActivationSlot("definition-1", "blue"), CancellationToken.None));
    }

    /// <summary>
    /// The activation surface exposed by Runtime.Api is read-only, and that is a pinned decision rather than an
    /// oversight (T117).
    /// </summary>
    /// <remarks>
    /// A runtime-only engine composes no publishing and therefore has no external deactivation surface at all; it
    /// re-reconciles through a shell reload (FR-B-008). Adding slot reads is precisely the context in which a
    /// <c>DELETE</c> looks like the obvious next endpoint, so the absence is asserted rather than assumed. The
    /// second half is the one that actually bites: <c>IWorkflowActivationCoordinator</c> owns deactivation, and
    /// this API must never take a dependency on it.
    /// </remarks>
    [Fact]
    public void Runtime_api_exposes_no_activation_mutation_and_never_injects_the_coordinator()
    {
        var apiTypes = typeof(WorkflowsRuntimeApiFeature).Assembly.GetTypes();
        var activationRoutes = MappedEndpoints()
            .Where(endpoint => endpoint.RoutePattern.RawText!.Contains("activation-slots", StringComparison.Ordinal))
            .ToArray();
        var coordinatorConsumers = apiTypes
            .SelectMany(type => type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Concat(type
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .SelectMany(method => method.GetParameters()))
                .Select(parameter => (Type: type, parameter.ParameterType)))
            .Where(candidate => candidate.ParameterType == typeof(IWorkflowActivationCoordinator))
            .Select(candidate => candidate.Type.FullName!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // The traversal must actually have found the two reads, or every assertion below is vacuous.
        Assert.Equal(2, activationRoutes.Length);
        Assert.All(activationRoutes, endpoint => Assert.Equal<string>(["GET"], Verbs(endpoint)));
        Assert.Empty(coordinatorConsumers);
    }

    /// <summary>Maps the production Runtime API and returns the endpoints it actually registered.</summary>
    private static RouteEndpoint[] MappedEndpoints()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var builder = new TestEndpointRouteBuilder(services);

        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(builder);

        return builder.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
    }

    private static RouteEndpoint MappedEndpoint(string route) =>
        Assert.Single(MappedEndpoints(), endpoint => endpoint.RoutePattern.RawText == route);

    private static IReadOnlyList<string> Verbs(RouteEndpoint endpoint) =>
        Assert.IsType<HttpMethodMetadata>(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()).HttpMethods;

    private static async Task<(Type Request, Type Response)> CapturedContractAsync(RouteEndpoint endpoint)
    {
        var sender = new ContractCapturingRequestSender();
        await using var services = new ServiceCollection().AddSingleton<IRequestSender>(sender).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        context.Request.RouteValues["definitionId"] = "definition-1";
        context.Request.RouteValues["slotName"] = "default";

        await endpoint.RequestDelegate!(context);

        return Assert.IsType<(Type Request, Type Response)>(sender.Captured);
    }

    /// <summary>Captures the mediator contract the mapped delegate uses, then forces the handled 404 path.</summary>
    private sealed class ContractCapturingRequestSender : IRequestSender
    {
        public (Type Request, Type Response)? Captured { get; private set; }

        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            Captured = (request.GetType(), typeof(T));
            throw new EntityNotFoundException("Contract capture stops the request before any store is touched.");
        }
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
