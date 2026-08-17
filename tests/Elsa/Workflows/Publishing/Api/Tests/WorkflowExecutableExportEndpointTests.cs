using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// <c>GET publishing/workflows/{versionId}/executable-export</c> (FR-B-010a, T085–T087): the four contracted
/// responses, the two engine-fault ones the contract's table omits, and the download name.
/// </summary>
/// <remarks>
/// The endpoint is driven through <c>Factory.Create</c> + <c>HandleAsync</c> against a real
/// <see cref="DefaultHttpContext"/>, which is this module's established idiom for behavioural endpoint tests. The
/// closure factory is the only substituted collaborator — the response cases are defined by which exception it
/// raises, and all of them are unreachable from a healthy store by construction. Everything downstream is
/// production code: the real download target and the real runtime-owned closure codec, which is what makes the
/// round-trip assertion mean something.
/// </remarks>
public sealed class WorkflowExecutableExportEndpointTests
{
    private const string EndpointTypeName = "Elsa.Workflows.Publishing.Api.Endpoints.ExportWorkflowExecutableClosureEndpoint";
    private const string VersionId = "wf-orders:1.4.0";
    private const string ExportRel = "workflow-executable-export";

    [Fact]
    public void The_export_route_and_verb_match_the_pinned_contract()
    {
        var endpoint = CreateEndpoint(new StubClosureFactory(Fixture.Closure()));
        endpoint.Configure();

        Assert.Equal("GET", Assert.Single(endpoint.Definition.Verbs));
        Assert.Equal(
            "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/executable-export",
            Assert.Single(endpoint.Definition.Routes));
    }

    [Fact]
    public void The_advertised_capability_href_mirrors_the_mapped_route()
    {
        // Nothing in the repo cross-checks a capability href against a registered route, so a typo in either of
        // the two strings studio#493 is pinned to would ship silently.
        var link = Assert.Single(PublishingApiCapabilities.StaticDeclaration.Links, candidate => candidate.Rel == ExportRel);
        var endpoint = CreateEndpoint(new StubClosureFactory(Fixture.Closure()));
        endpoint.Configure();

        var route = Assert.Single(endpoint.Definition.Routes);

        Assert.True(link.Templated);
        Assert.Equal("publishing/workflows/{versionId}/executable-export", link.Href);
        Assert.Equal(link.Href, StripRouteConstraints(route));
        Assert.False(link.Href.StartsWith('/'));
    }

    [Fact]
    public void The_export_route_is_gated_by_the_publishing_read_permission_and_owns_its_metadata()
    {
        var endpoint = CreateEndpoint(new StubClosureFactory(Fixture.Closure()));
        endpoint.Configure();

        // The Elsa base composes wildcard-or-action into one canonical policy and attaches owner, authoring model
        // and security disposition. No new PermissionNames constant is introduced: export is gated by the same
        // read permission that already exposes executable content.
        var policy = Assert.Single(ConfiguredPermissionPolicies(endpoint.Definition));

        // The policy is an encoded descriptor, so it is compared against the composition of the permission it must
        // carry — and against the one it must not.
        Assert.Equal(
            ElsaEndpointPermissions.ComposePolicy([PermissionNames.WorkflowPublishingRead]),
            policy);
        Assert.NotEqual(
            ElsaEndpointPermissions.ComposePolicy([PermissionNames.WorkflowPublishingManage]),
            policy);
    }

    // ---------------------------------------------------------------------------------------------------------
    // The four contracted responses.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Success_returns_the_closure_as_an_attachment_that_reads_back_through_the_import_codec()
    {
        var closure = Fixture.Closure();
        var endpoint = CreateEndpoint(new StubClosureFactory(closure));

        await InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId));
        var body = await BodyAsync(endpoint);

        Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
        Assert.Equal("application/json", endpoint.HttpContext.Response.ContentType);
        Assert.Equal(
            $"attachment; filename=\"{Fixture.DefinitionId}-{Fixture.ArtifactVersion}-closure.json\"",
            endpoint.HttpContext.Response.Headers.ContentDisposition.ToString());

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
        var endpoint = CreateEndpoint(new StubClosureFactory(new WorkflowArtifactClosureSourceNotFoundException(
            VersionId,
            "no executable source reference of any scope exists for it.")));

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId)));

        Assert.Equal(StatusCodes.Status404NotFound, failure.StatusCode);
        Assert.Contains(VersionId, Messages(failure), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_run_only_version_is_a_409_that_names_the_scopes_it_does_have()
    {
        var endpoint = CreateEndpoint(new StubClosureFactory(new WorkflowArtifactClosureNotPublishedException(
            VersionId,
            [WorkflowExecutableReferenceScope.TestRun])));

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId)));

        Assert.Equal(StatusCodes.Status409Conflict, failure.StatusCode);
        Assert.Contains("TestRun", Messages(failure), StringComparison.Ordinal);
        Assert.Contains("no Published source reference", Messages(failure), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Incomplete_closure_is_a_409_that_names_every_missing_artifact_id()
    {
        var endpoint = CreateEndpoint(new StubClosureFactory(new IncompleteWorkflowArtifactClosureException(
            VersionId,
            "artifact-root",
            [
                new("artifact-child", "sha256:child", null, "artifact-root"),
                new("artifact-grandchild", "sha256:grandchild", "sha256:other", "artifact-child")
            ])));

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId)));

        Assert.Equal(StatusCodes.Status409Conflict, failure.StatusCode);

        // Each unresolved id is its own error entry, so a client renders them without parsing the summary.
        var messages = failure.Failures.Select(item => item.ErrorMessage).ToArray();
        Assert.Contains(messages, message => message.Contains("artifact-child", StringComparison.Ordinal));
        Assert.Contains(messages, message => message.Contains("artifact-grandchild", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------------------
    // The two engine-fault members of the same exception family, which the contract's four-row table omits.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dependency_cycle_is_reported_as_store_corruption_not_a_client_error()
    {
        var endpoint = CreateEndpoint(new StubClosureFactory(
            new WorkflowArtifactClosureCycleException(VersionId, ["a", "b", "a"])));

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId)));

        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
        Assert.Contains("a -> b -> a", Messages(failure), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_fault_never_puts_the_providers_own_message_on_the_wire()
    {
        var endpoint = CreateEndpoint(new StubClosureFactory(new WorkflowArtifactClosureStorageException(
            VersionId,
            "read the executable source references",
            new InvalidOperationException("connection string is wrong"))));

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId)));

        // §2.23.5: the wrap happened at the factory; only its own message is client-visible.
        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
        Assert.DoesNotContain("connection string is wrong", Messages(failure), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_codec_failure_is_wrapped_rather_than_escaping_as_a_raw_json_exception()
    {
        // The codec deliberately lets JsonException through — this boundary owns the version id it does not, so
        // this boundary is the one §2.23.5 obliges to wrap it.
        var endpoint = CreateEndpoint(
            new StubClosureFactory(Fixture.Closure()),
            [new DownloadWorkflowArtifactExportTarget(new ThrowingCodec())]);

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId)));

        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
        Assert.Contains(VersionId, Messages(failure), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_receipt_producing_target_can_never_answer_this_safe_method()
    {
        var endpoint = CreateEndpoint(new StubClosureFactory(Fixture.Closure()), [new ReceiptTargetImpersonatingDownload()]);

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId)));

        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
    }

    [Fact]
    public async Task No_registered_download_target_is_a_500_rather_than_a_null_dereference()
    {
        var endpoint = CreateEndpoint(new ThrowingClosureFactory(), targets: []);

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId)));

        Assert.Equal(StatusCodes.Status500InternalServerError, failure.StatusCode);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Naming and input hardening.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Hostile_identifiers_cannot_reach_the_content_disposition_header_unfiltered()
    {
        // Both filename segments come from stored data and land in a header this endpoint writes by hand. A
        // definition id carrying a quote, a path separator or a newline would otherwise be echoed onto the wire.
        var closure = Fixture.Closure(definitionId: "../../etc/pa\"ss wd\r\nX-Injected: 1", artifactVersion: "1.0.0-rc.1");
        var endpoint = CreateEndpoint(new StubClosureFactory(closure));

        await InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(VersionId));

        Assert.Equal(
            "attachment; filename=\"etc-pa-ss-wd-X-Injected-1-1.0.0-rc.1-closure.json\"",
            endpoint.HttpContext.Response.Headers.ContentDisposition.ToString());
        Assert.False(endpoint.HttpContext.Response.Headers.ContainsKey("X-Injected"));
    }

    [Fact]
    public async Task A_version_id_that_names_nothing_is_a_404_before_the_factory_is_reached()
    {
        // The route constraint is `.+`, so a whitespace-only segment ("%20") binds and reaches the handler.
        var endpoint = CreateEndpoint(new ThrowingClosureFactory());

        var failure = await Assert.ThrowsAsync<ValidationFailureException>(() =>
            InvokeAsync(endpoint, new ExportWorkflowExecutableClosure(" ")));

        Assert.Equal(StatusCodes.Status404NotFound, failure.StatusCode);
    }

    /// <summary>
    /// The Foundation Identity permission policies <c>Configure()</c> installed on the definition.
    /// </summary>
    /// <remarks>
    /// FastEndpoints exposes <c>Policies(...)</c> as a configuration method and keeps the result in state it does
    /// not surface as a typed property, so this reads whichever string collection on the definition holds values
    /// carrying the codec's policy prefix. That prefix is the stable contract here, not the member name.
    /// </remarks>
    private static IReadOnlyList<string> ConfiguredPermissionPolicies(EndpointDefinition definition) =>
        typeof(EndpointDefinition)
            .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(member => member switch
            {
                PropertyInfo property when property.GetIndexParameters().Length == 0 => TryRead(() => property.GetValue(definition)),
                FieldInfo field => TryRead(() => field.GetValue(definition)),
                _ => null
            })
            .OfType<IEnumerable<string>>()
            .SelectMany(values => values)
            .Where(value => value.StartsWith(PermissionPolicyCodec.Prefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static object? TryRead(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is TargetInvocationException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Messages(ValidationFailureException failure) =>
        string.Join(" | ", failure.Failures.Select(item => item.ErrorMessage));

    private static string StripRouteConstraints(string route) =>
        System.Text.RegularExpressions.Regex.Replace(route, @"\{(?<name>[^:}]+):[^}]+\}", "{${name}}");

    /// <summary>
    /// Builds the endpoint with the production download target unless the caller supplies its own registrations;
    /// pass an empty array to model an engine that contributed none.
    /// </summary>
    private static BaseEndpoint CreateEndpoint(
        IWorkflowArtifactClosureFactory factory,
        IWorkflowArtifactExportTarget[]? targets = null)
    {
        targets ??= [new DownloadWorkflowArtifactExportTarget(Fixture.Codec())];
        var endpointType = typeof(WorkflowsPublishingApiFeature).Assembly.GetType(EndpointTypeName, throwOnError: true)!;
        var loggerType = typeof(NullLogger<>).MakeGenericType(endpointType);
        var logger = loggerType.GetProperty(nameof(NullLogger<object>.Instance))?.GetValue(null)
                     ?? loggerType.GetField(nameof(NullLogger<object>.Instance))!.GetValue(null)!;
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var second] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) &&
                              second.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        Action<DefaultHttpContext> configure = context => context.Response.Body = new MemoryStream();
        return (BaseEndpoint)create.Invoke(null, [configure, new object[] { factory, targets, logger }])!;
    }

    private static Task InvokeAsync(BaseEndpoint endpoint, ExportWorkflowExecutableClosure request) =>
        (Task)endpoint.GetType()
            .GetMethod("HandleAsync", [typeof(ExportWorkflowExecutableClosure), typeof(CancellationToken)])!
            .Invoke(endpoint, [request, CancellationToken.None])!;

    private static async Task<string> BodyAsync(BaseEndpoint endpoint)
    {
        endpoint.HttpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(endpoint.HttpContext.Response.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

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
