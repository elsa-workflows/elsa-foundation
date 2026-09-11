using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// <c>GET publishing/publications/{publicationId}</c> (#1625): the Publishing-owned half of the
/// activation-slot join that #1498 split between the two owners.
/// </summary>
/// <remarks>
/// The invariant this pins is the join itself: for a publishing-sourced activation, the runtime slot's
/// <c>activeActivationId</c> is the id of the publication record, and this route resolves that id to the
/// design version Publishing put behind the slot. Runtime never joins to the journal, so nothing else in
/// the tree would notice if the two ids drifted apart.
/// <para>
/// The failure that would look like success is a miss answered as an empty 200: a client building a
/// "Review &amp; publish" screen would render the absent design version as a fact about the slot instead of
/// a failed lookup. <see cref="An_id_that_names_no_record_is_404_problem_details_and_never_an_empty_success"/>
/// asserts that direction explicitly, in both the status and the body.
/// </para>
/// </remarks>
public sealed class PublicationRecordEndpointTests
{
    private const string EndpointName = "GetPublicationRecordEndpoint";
    private const string Rel = "publication-record";
    private const string DefinitionId = "definition-publication-record";
    private const string SlotName = "default";
    private const string PublicationId = "publication-record-1";
    private const string VersionId = "version-record-1";
    private const string ArtifactId = "artifact-record-1";

    // The scenario host's fixed clock, which is also what stamps the activation.
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_route_and_verb_match_the_pinned_contract()
    {
        var endpoint = PublishingMinimalApiTestSurface.Named(EndpointName);

        Assert.Equal("GET", Assert.Single(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods));
        Assert.Equal("publishing/publications/{publicationId}", endpoint.RoutePattern.RawText?.TrimStart('/'));
    }

    [Fact]
    public void The_advertised_relation_mirrors_the_mapped_route_without_moving_the_contract_major()
    {
        var link = Assert.Single(PublishingApiCapabilities.StaticDeclaration.Links, candidate => candidate.Rel == Rel);
        var route = PublishingMinimalApiTestSurface.Named(EndpointName).RoutePattern.RawText!.TrimStart('/');

        // A typo in either of the two strings a client is pinned to would otherwise ship silently.
        Assert.True(link.Templated);
        Assert.Equal("publishing/publications/{publicationId}", link.Href);
        Assert.Equal(link.Href, route);
        Assert.False(link.Href.StartsWith('/'));
        // The relation is additive, so clients that pin the major keep working.
        Assert.Equal(1, PublishingApiCapabilities.StaticDeclaration.ContractMajorVersion);
    }

    [Fact]
    public void The_route_is_gated_by_the_publishing_read_permission()
    {
        var endpoint = PublishingMinimalApiTestSurface.Named(EndpointName);

        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var parsed = new PermissionPolicyCodec().Parse(authorization.Policy!);
        Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
        var descriptor = Assert.IsType<PermissionPolicyDescriptor>(parsed.Descriptor);
        Assert.Equal(PermissionRequirementMode.Single, descriptor.Mode);
        Assert.Equal(PermissionKey.Normalize(WorkflowPublishingPermissions.Read), Assert.Single(descriptor.Permissions));
        Assert.NotEqual(PermissionKey.Normalize(WorkflowPublishingPermissions.Manage), descriptor.Permissions[0]);
        Assert.NotEqual(PermissionKey.Wildcard, descriptor.Permissions[0]);

        Assert.Equal("Elsa.Workflows.Publishing.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.Owner);
        Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
    }

    [Fact]
    public async Task An_unauthenticated_read_is_rejected_before_the_journal_is_touched()
    {
        await using var host = await StartWithActivePublicationAsync();

        using var response = await host.Client.GetAsync($"/publishing/publications/{PublicationId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(VersionId, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_existing_record_is_returned_as_the_publication_journal_view()
    {
        await using var host = await StartWithActivePublicationAsync();

        using var response = await ReadPublicationAsync(host, PublicationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(PublicationId, body.GetProperty("publicationId").GetString());
        Assert.Equal(DefinitionId, body.GetProperty("definitionId").GetString());
        Assert.Equal(VersionId, body.GetProperty("versionId").GetString());
        Assert.Equal(ArtifactId, body.GetProperty("artifactId").GetString());
        Assert.Equal(SlotName, body.GetProperty("slotName").GetString());
        Assert.Equal(WorkflowActivationReferenceIdentity.Create(PublicationId), body.GetProperty("sourceReferenceId").GetString());
        Assert.Equal("active", body.GetProperty("status").GetString());
        Assert.Equal(Now, body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(Now, body.GetProperty("activatedAt").GetDateTimeOffset());
        // The journal is the same shape the unpublish/restore responses already emit; no new model.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("retiredAt").ValueKind);
    }

    [Fact]
    public async Task An_id_that_names_no_record_is_404_problem_details_and_never_an_empty_success()
    {
        await using var host = await StartWithActivePublicationAsync();

        using var response = await ReadPublicationAsync(host, "publication-absent");
        var raw = (await response.Content.ReadAsStringAsync()).Trim();

        // Asserted before parsing, so a regression that answers the miss with an empty or null 200 body
        // reports the status it chose rather than a reader exception.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        // A silent success would satisfy "the call returned"; neither shape of one is reachable from a miss.
        Assert.NotEqual(string.Empty, raw);
        Assert.NotEqual("null", raw);

        using var document = JsonDocument.Parse(raw);
        var problem = document.RootElement;
        Assert.Equal(JsonValueKind.Object, problem.ValueKind);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", problem.GetProperty("title").GetString());
        Assert.Contains("publication-absent", problem.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blank_id_is_404_whichever_store_is_configured()
    {
        // The in-memory store rejects a blank key with an ArgumentException (a 400 through the owner's translator)
        // while a keyed provider simply finds nothing; the route answers before either is asked.
        await using var host = await StartWithActivePublicationAsync();

        using var response = await ReadPublicationAsync(host, " ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_publishing_sourced_slot_resolves_through_its_activation_id_to_the_published_version()
    {
        await using var host = await StartWithActivePublicationAsync();

        // 1. Runtime answers "what is active", unjoined.
        using var slotResponse = await host.Client.SendAsync(Authenticated(
            $"/runtime/workflows/activation-slots/{DefinitionId}/{SlotName}"));

        Assert.Equal(HttpStatusCode.OK, slotResponse.StatusCode);
        var slot = await ReadJsonAsync(slotResponse);
        Assert.Equal(WorkflowActivationSource.PublishingKind, slot.GetProperty("sourceKind").GetString());
        var activationId = slot.GetProperty("activeActivationId").GetString();
        Assert.Equal(PublicationId, activationId);
        // The runtime view is deliberately unjoined: the design version is not on it.
        Assert.False(slot.TryGetProperty("versionId", out _));

        // 2. Publishing answers "what was published" for that same id.
        using var recordResponse = await ReadPublicationAsync(host, activationId!);

        Assert.Equal(HttpStatusCode.OK, recordResponse.StatusCode);
        var record = await ReadJsonAsync(recordResponse);
        Assert.Equal(VersionId, record.GetProperty("versionId").GetString());
        Assert.Equal(DefinitionId, record.GetProperty("definitionId").GetString());
        Assert.Equal(SlotName, record.GetProperty("slotName").GetString());
    }

    private static async Task<PublishingMinimalApiScenarioHost> StartWithActivePublicationAsync()
    {
        var host = await StartAsync();
        try
        {
            await ActivatePublicationAsync(host.Services);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Activates one publication through the production activator, so the slot's activation id is the one the
    /// real publish path writes rather than one the test asserted into place.
    /// </summary>
    private static async Task ActivatePublicationAsync(IServiceProvider root)
    {
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        var candidate = new PublicationRecord(
            PublicationId,
            WorkflowActivationSlotIdentity.Create(DefinitionId, SlotName),
            DefinitionId,
            VersionId,
            ArtifactId,
            WorkflowActivationReferenceIdentity.Create(PublicationId),
            ExpectedSlotRevision: 0,
            PublicationStatus.Candidate,
            Now,
            ActivatedAt: null,
            RetiredAt: null,
            Failure: null,
            SlotName);
        var executable = TestExecutable.Create(TestExecutable.Identity(
            ArtifactId, "sha256:record", DefinitionId, VersionId));
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);

        var result = await services.GetRequiredService<IPublicationActivator>().ActivateAsync(new PublicationActivationRequest(
            candidate,
            executable,
            new WorkflowExecutableSourceReference(
                candidate.SourceReferenceId!,
                ArtifactId,
                WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
                VersionId,
                "1.0.0",
                DefinitionId,
                VersionId,
                "1.0.0",
                Now,
                Now,
                WorkflowExecutableReferenceScope.Published,
                ActivationId: PublicationId,
                SlotId: candidate.SlotId)));

        Assert.True(result.Succeeded, $"{result.Failure?.Code}: {result.Failure?.Message}");
    }

    /// <summary>
    /// Composes the Runtime API beside the Publishing API so the two hops of the join are exercised over real
    /// HTTP against one graph, which is the only way a drift between the two owners' ids would show up.
    /// </summary>
    private static Task<PublishingMinimalApiScenarioHost> StartAsync() =>
        PublishingMinimalApiScenarioHost.StartAsync(
            configureServices: services =>
            {
                new WorkflowsRuntimeApiFeature().ConfigureServices(services);
                // Trigger indexing is normally contributed by the runtime triggers feature, which this
                // composition does not mount. The production indexer over the host's own extractor and binding
                // store is registered instead, so activation runs its real projection-preparation step.
                services.TryAddScoped<IWorkflowTriggerIndexer, WorkflowTriggerIndexer>();
            },
            configurePipeline: app => WorkflowsRuntimeApi.MapWorkflowsRuntimeApi((IEndpointRouteBuilder)app));

    private static Task<HttpResponseMessage> ReadPublicationAsync(
        PublishingMinimalApiScenarioHost host,
        string publicationId) =>
        host.Client.SendAsync(Authenticated($"/publishing/publications/{Uri.EscapeDataString(publicationId)}"));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static HttpRequestMessage Authenticated(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return request;
    }
}
