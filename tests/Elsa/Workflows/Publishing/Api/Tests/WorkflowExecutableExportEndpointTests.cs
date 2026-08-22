using System.Net;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// <c>GET publishing/workflows/{versionId}/executable-export</c> (FR-B-010a, T085–T087): the four contracted
/// responses, the two engine-fault ones the contract's table omits, and the download name.
/// </summary>
/// <remarks>
/// The handler is driven over real HTTP through the module's Minimal API test host, which is this module's
/// established idiom for behavioural endpoint tests. The closure factory is the only substituted collaborator —
/// the response cases are defined by which exception it raises, and all of them are unreachable from a healthy
/// store by construction. Everything downstream is production code: the real download target and the real
/// runtime-owned closure codec, which is what makes the round-trip assertion mean something.
/// </remarks>
public sealed class WorkflowExecutableExportEndpointTests
{
    private const string EndpointName = "ExportWorkflowExecutableClosureEndpoint";
    private const string VersionId = "wf-orders:1.4.0";
    private const string ExportRel = "workflow-executable-export";

    [Fact]
    public void The_export_route_and_verb_match_the_pinned_contract()
    {
        var endpoint = PublishingMinimalApiTestSurface.Named(EndpointName);

        Assert.Equal("GET", Assert.Single(endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods));
        Assert.Equal(
            "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/executable-export",
            endpoint.RoutePattern.RawText?.TrimStart('/'));
    }

    [Fact]
    public void The_advertised_capability_href_mirrors_the_mapped_route()
    {
        // Nothing in the repo cross-checks a capability href against a registered route, so a typo in either of
        // the two strings studio#493 is pinned to would ship silently.
        var link = Assert.Single(PublishingApiCapabilities.StaticDeclaration.Links, candidate => candidate.Rel == ExportRel);
        var route = PublishingMinimalApiTestSurface.Named(EndpointName).RoutePattern.RawText!.TrimStart('/');

        Assert.True(link.Templated);
        Assert.Equal("publishing/workflows/{versionId}/executable-export", link.Href);
        Assert.Equal(link.Href, StripRouteConstraints(route));
        Assert.False(link.Href.StartsWith('/'));
    }

    [Fact]
    public void The_export_route_is_gated_by_the_publishing_read_permission_and_owns_its_metadata()
    {
        var endpoint = PublishingMinimalApiTestSurface.Named(EndpointName);

        // The shared Map convention composes one canonical permission policy and attaches owner and authoring
        // model. No new permission key is introduced: export is gated by the same read permission that already
        // exposes executable content.
        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        var parsed = new PermissionPolicyCodec().Parse(authorization.Policy!);
        Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
        var descriptor = Assert.IsType<PermissionPolicyDescriptor>(parsed.Descriptor);
        Assert.Equal(PermissionRequirementMode.Single, descriptor.Mode);

        // The policy is an encoded descriptor, so it is compared against the permission it must carry — and
        // against the one it must not.
        Assert.Equal(PermissionKey.Normalize(WorkflowPublishingPermissions.Read), Assert.Single(descriptor.Permissions));
        Assert.NotEqual(PermissionKey.Normalize(WorkflowPublishingPermissions.Manage), descriptor.Permissions[0]);

        Assert.Equal("Elsa.Workflows.Publishing.Api", endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.Owner);
        Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
    }

    // ---------------------------------------------------------------------------------------------------------
    // The four contracted responses.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Success_returns_the_closure_as_an_attachment_that_reads_back_through_the_import_codec()
    {
        var closure = Fixture.Closure();
        await using var host = await StartAsync(new StubClosureFactory(closure));

        using var response = await ExportAsync(host, VersionId);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            $"attachment; filename=\"{Fixture.DefinitionId}-{Fixture.ArtifactVersion}-closure.json\"",
            ContentDisposition(response));

        // The assertion that matters: a body that is valid JSON but not a readable envelope would satisfy any
        // shape check and fail on import. Decode with the same codec JsonWorkflowArtifactClosureReader uses.
        var roundTripped = Fixture.Codec().Deserialize(body);
        Assert.NotNull(roundTripped);
        Assert.True(WorkflowArtifactClosureFormat.IsSupported(roundTripped!.FormatVersion));
        Assert.Equal(closure.RootArtifactId, roundTripped.RootArtifactId);
        Assert.Equal(
            closure.Artifacts.Select(artifact => artifact.Identity.ArtifactId).Order(StringComparer.Ordinal),
            roundTripped.Artifacts.Select(artifact => artifact.Identity.ArtifactId).Order(StringComparer.Ordinal));
        Assert.Equal(
            closure.Artifacts.Select(artifact => artifact.Identity.ArtifactHash).Order(StringComparer.Ordinal),
            roundTripped.Artifacts.Select(artifact => artifact.Identity.ArtifactHash).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Unknown_version_is_a_404()
    {
        await using var host = await StartAsync(new StubClosureFactory(new WorkflowArtifactClosureSourceNotFoundException(
            VersionId,
            "no executable source reference of any scope exists for it.")));

        using var response = await ExportAsync(host, VersionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(VersionId, await Messages(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_run_only_version_is_a_409_that_names_the_scopes_it_does_have()
    {
        await using var host = await StartAsync(new StubClosureFactory(new WorkflowArtifactClosureNotPublishedException(
            VersionId,
            [WorkflowExecutableReferenceScope.TestRun])));

        using var response = await ExportAsync(host, VersionId);
        var messages = await Messages(response);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("TestRun", messages, StringComparison.Ordinal);
        Assert.Contains("no Published source reference", messages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Incomplete_closure_is_a_409_that_names_every_missing_artifact_id()
    {
        await using var host = await StartAsync(new StubClosureFactory(new IncompleteWorkflowArtifactClosureException(
            VersionId,
            "artifact-root",
            [
                new("artifact-child", "sha256:child", null, "artifact-root"),
                new("artifact-grandchild", "sha256:grandchild", "sha256:other", "artifact-child")
            ])));

        using var response = await ExportAsync(host, VersionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Each unresolved id is its own error entry, so a client renders them without parsing the summary.
        var reasons = await Reasons(response);
        Assert.Contains("Dependency artifact 'artifact-child' is missing from the executable store.", reasons);
        Assert.Contains("Dependency artifact 'artifact-grandchild' is missing from the executable store.", reasons);
    }

    // ---------------------------------------------------------------------------------------------------------
    // The two engine-fault members of the same exception family, which the contract's four-row table omits.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dependency_cycle_is_reported_as_store_corruption_not_a_client_error()
    {
        await using var host = await StartAsync(new StubClosureFactory(
            new WorkflowArtifactClosureCycleException(VersionId, ["a", "b", "a"])));

        using var response = await ExportAsync(host, VersionId);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("a -> b -> a", await Messages(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_fault_never_puts_the_providers_own_message_on_the_wire()
    {
        await using var host = await StartAsync(new StubClosureFactory(new WorkflowArtifactClosureStorageException(
            VersionId,
            "read the executable source references",
            new InvalidOperationException("connection string is wrong"))));

        using var response = await ExportAsync(host, VersionId);

        // §2.23.5: the wrap happened at the factory; only its own message is client-visible.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("connection string is wrong", await Messages(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_codec_failure_is_wrapped_rather_than_escaping_as_a_raw_json_exception()
    {
        // The codec deliberately lets JsonException through — this boundary owns the version id it does not, so
        // this boundary is the one §2.23.5 obliges to wrap it.
        await using var host = await StartAsync(
            new StubClosureFactory(Fixture.Closure()),
            [new DownloadWorkflowArtifactExportTarget(new ThrowingCodec())]);

        using var response = await ExportAsync(host, VersionId);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(VersionId, await Messages(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_receipt_producing_target_can_never_answer_this_safe_method()
    {
        await using var host = await StartAsync(
            new StubClosureFactory(Fixture.Closure()),
            [new ReceiptTargetImpersonatingDownload()]);

        using var response = await ExportAsync(host, VersionId);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task No_registered_download_target_is_a_500_rather_than_a_null_dereference()
    {
        await using var host = await StartAsync(new ThrowingClosureFactory(), targets: []);

        using var response = await ExportAsync(host, VersionId);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Naming and input hardening.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Hostile_identifiers_cannot_reach_the_content_disposition_header_unfiltered()
    {
        // Both filename segments come from stored data and land in a header this handler writes by hand. A
        // definition id carrying a quote, a path separator or a newline would otherwise be echoed onto the wire.
        var closure = Fixture.Closure(definitionId: "../../etc/pa\"ss wd\r\nX-Injected: 1", artifactVersion: "1.0.0-rc.1");
        await using var host = await StartAsync(new StubClosureFactory(closure));

        using var response = await ExportAsync(host, VersionId);

        Assert.Equal(
            "attachment; filename=\"etc-pa-ss-wd-X-Injected-1-1.0.0-rc.1-closure.json\"",
            ContentDisposition(response));
        Assert.False(response.Headers.Contains("X-Injected"));
        Assert.False(response.Content.Headers.Contains("X-Injected"));
    }

    [Fact]
    public async Task A_version_id_that_names_nothing_is_a_404_before_the_factory_is_reached()
    {
        // The route constraint is `.+`, so a whitespace-only segment ("%20") binds and reaches the handler.
        await using var host = await StartAsync(new ThrowingClosureFactory());

        using var response = await ExportAsync(host, " ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Starts the module's Minimal API host with the production download target unless the caller supplies its
    /// own registrations; pass an empty array to model an engine that contributed none.
    /// </summary>
    private static Task<PublishingMinimalApiScenarioHost> StartAsync(
        IWorkflowArtifactClosureFactory closureFactory,
        IWorkflowArtifactExportTarget[]? targets = null) =>
        PublishingMinimalApiScenarioHost.StartAsync(configureServices: services =>
        {
            services.RemoveAll<IWorkflowArtifactClosureFactory>();
            services.AddSingleton(closureFactory);
            services.RemoveAll<IWorkflowArtifactExportTarget>();
            foreach (var target in targets ?? [new DownloadWorkflowArtifactExportTarget(Fixture.Codec())])
                services.AddSingleton(target);
        });

    private static async Task<HttpResponseMessage> ExportAsync(PublishingMinimalApiScenarioHost host, string versionId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/publishing/workflows/{Uri.EscapeDataString(versionId)}/executable-export");
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");
        return await host.Client.SendAsync(request);
    }

    private static string ContentDisposition(HttpResponseMessage response) =>
        Assert.Single(response.Content.Headers.GetValues("Content-Disposition"));

    /// <summary>Every <c>errors[]</c> reason the problem document carries, in order.</summary>
    private static async Task<string[]> Reasons(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("reason").GetString()!)
            .ToArray();
    }

    private static async Task<string> Messages(HttpResponseMessage response) =>
        string.Join(" | ", await Reasons(response));

    private static string StripRouteConstraints(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, @"\{(?<name>[^:}]+):[^}]+\}", "{${name}}");

    private sealed class StubClosureFactory : IWorkflowArtifactClosureFactory
    {
        private readonly WorkflowArtifactClosure? _closure;
        private readonly Exception? _fault;

        public StubClosureFactory(WorkflowArtifactClosure closure) => _closure = closure;

        public StubClosureFactory(Exception fault) => _fault = fault;

        public Task<WorkflowArtifactClosure> CreateAsync(string definitionVersionId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersionId);
            return _fault is not null ? Task.FromException<WorkflowArtifactClosure>(_fault) : Task.FromResult(_closure!);
        }
    }

    /// <summary>Fails loudly if it is ever called, so "the handler refused before the factory" is provable.</summary>
    private sealed class ThrowingClosureFactory : IWorkflowArtifactClosureFactory
    {
        public Task<WorkflowArtifactClosure> CreateAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"The closure factory must not be reached for '{definitionVersionId}'.");
    }

    /// <summary>A target that answers to "download" but delivers a receipt — the shape the GET must refuse.</summary>
    private sealed class ReceiptTargetImpersonatingDownload : IWorkflowArtifactExportTarget
    {
        public string TargetId => DownloadWorkflowArtifactExportTarget.Id;

        public Task<WorkflowArtifactExportDelivery> DeliverAsync(
            WorkflowArtifactClosure closure,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(WorkflowArtifactExportDelivery.Receipt(TargetId, "file:///exports/closure.json"));
    }

    internal sealed class ThrowingCodec : IWorkflowArtifactClosureSerializer
    {
        public string Serialize(WorkflowArtifactClosure closure) => throw new JsonException("unencodable");

        public WorkflowArtifactClosure? Deserialize(string json) => throw new JsonException("undecodable");
    }

    /// <summary>A closure whose artifact identity is derived by the production hasher, not hand-written.</summary>
    internal static class Fixture
    {
        public const string DefinitionId = "wf-orders";
        public const string ArtifactVersion = "1.4.0";

        private static readonly DateTimeOffset CreatedAt = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

        public static IWorkflowArtifactClosureSerializer Codec() =>
            new WorkflowArtifactClosureSerializer(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()));

        public static WorkflowArtifactClosure Closure(
            string definitionId = DefinitionId,
            string artifactVersion = ArtifactVersion)
        {
            var root = Executable(definitionId, artifactVersion);
            return new WorkflowArtifactClosure(
                WorkflowArtifactClosureFormat.CurrentVersion,
                root.Identity.ArtifactId,
                [root],
                [],
                []);
        }

        private static WorkflowExecutable Executable(string definitionId, string artifactVersion)
        {
            var hasher = new WorkflowExecutableHasher();
            var rootActivity = new ExecutableNode(
                executableNodeId: "node-1",
                authoredActivityId: "authored-node-1",
                activityType: "Elsa.Testing.Probe",
                activityTypeVersion: "1.0.0",
                descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
                descriptorPayload: JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Elsa.Testing.Probe")),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
                metadata: new Dictionary<string, string>(StringComparer.Ordinal));
            var inputContract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []);
            var hash = hasher.ComputeHash(
                rootActivity,
                inputContract,
                [],
                checkpointCadence: null,
                workflowVariables: [],
                incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

            return new WorkflowExecutable(
                identity: new WorkflowExecutableIdentity(
                    ArtifactId: hasher.CreateArtifactId("artifact-", hash),
                    DefinitionId: definitionId,
                    DefinitionVersionId: VersionId,
                    ArtifactVersion: artifactVersion,
                    ArtifactHash: hash),
                rootActivity: rootActivity,
                resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
                createdAt: CreatedAt,
                compatibilityMetadata: new Dictionary<string, string>(),
                inputContract: inputContract,
                dependencies: [],
                runtimeRequirements: null,
                storageDriverRequirements: null,
                incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
                checkpointCadence: null,
                workflowVariables: []);
        }
    }
}
