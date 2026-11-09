using System.Security.Claims;
using Elsa.Foundation.Identity;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutionStartServiceTests : IAsyncLifetime
{
    private readonly InMemoryWorkflowExecutableStore _store = new();
    private readonly HttpContextAccessor _httpContextAccessor = new();

    public async Task InitializeAsync() => await _store.SaveAsync(NewExecutable());

    public Task DisposeAsync()
    {
        _httpContextAccessor.HttpContext = null;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RejectsUnknownArtifactId()
    {
        var handler = new WorkflowExecutionStartService(await NewDispatcherAsync(), _store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableNotFoundException>(() => handler.ExecuteAsync(new ExecuteWorkflow("missing-artifact"), CancellationToken.None));

        Assert.Contains("missing-artifact", exception.Message);
        Assert.Equal("missing-artifact", exception.ArtifactId);
    }

    [Fact]
    public async Task ReturnsAgentDispatchView()
    {
        var handler = new WorkflowExecutionStartService(await NewDispatcherAsync(), _store);

        var result = await handler.ExecuteAsync(new ExecuteWorkflow("artifact-1"), CancellationToken.None);

        Assert.Equal("wfexec-fixed", result.WorkflowExecutionId);
        Assert.Equal("artifact-1", result.ArtifactId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted.ToString(), result.CommandDispatchStatus);
        Assert.Equal("envelope-fixed", result.EnvelopeId);
        Assert.Equal("recording:wfexec-fixed", result.AgentId);
        Assert.Equal("Recording", result.AgentProviderName);
    }

    [Fact]
    public async Task ClassifiesDirectExecutableStartsAsPublishedRuns()
    {
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync());
        var handler = new WorkflowExecutionStartService(dispatcher, _store);

        await handler.ExecuteAsync(new ExecuteWorkflow("artifact-1"), CancellationToken.None);

        Assert.Equal(WorkflowRunKind.PublishedRun, Assert.Single(dispatcher.Requests).RunKind);
        Assert.Equal(
            WorkflowExecutableProvenanceRequirement.RequireLiveReference,
            Assert.Single(dispatcher.Requests).ProvenanceRequirement);
    }

    [Fact]
    public async Task CopiesNormalizedTenantToDispatchWithoutInferringPersistencePartition()
    {
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync());
        var handler = HttpHandler(dispatcher,
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-from-identity")
        ]);

        await handler.ExecuteAsync(new ExecuteWorkflow("artifact-1"), CancellationToken.None);

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal("tenant-from-identity", request.TenantId);
        Assert.Null(request.Partition);
    }

    [Fact]
    public async Task AllowsAnExplicitNormalizedGlobalIdentityWithoutTenantClaim()
    {
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync());
        var handler = HttpHandler(dispatcher, [new Claim(IdentityClaimTypes.Normalized, "v1")]);

        await handler.ExecuteAsync(new ExecuteWorkflow("artifact-1"), CancellationToken.None);

        Assert.Null(Assert.Single(dispatcher.Requests).TenantId);
    }

    [Theory]
    [InlineData("untrusted-authentication-type", true)]
    [InlineData("trusted", false)]
    public async Task RefusesHttpStartWithoutOneTrustedNormalizedPrincipal(string authenticationType, bool hasMarker)
    {
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync());
        var claims = new List<Claim> { new(IdentityClaimTypes.TenantId, "tenant-a") };
        if (hasMarker)
            claims.Add(new Claim(IdentityClaimTypes.Normalized, "v1"));
        var handler = HttpHandler(dispatcher, claims, authenticationType);

        await AssertHttpIdentityRefusedAsync(handler, dispatcher);
    }

    [Fact]
    public async Task RefusesHttpStartWithDuplicateTenantClaims()
    {
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync());
        var handler = HttpHandler(dispatcher,
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-a"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-a")
        ]);

        await AssertHttpIdentityRefusedAsync(handler, dispatcher);
    }

    [Fact]
    public async Task RefusesHttpStartWithBlankTenantClaim()
    {
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync());
        var handler = HttpHandler(dispatcher,
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, " ")
        ]);

        await AssertHttpIdentityRefusedAsync(handler, dispatcher);
    }

    [Fact]
    public async Task TwoArgumentEmbeddedConstructionKeepsTenantUnspecified()
    {
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync());
        var handler = new WorkflowExecutionStartService(dispatcher, _store);

        await handler.ExecuteAsync(new ExecuteWorkflow("artifact-1"), CancellationToken.None);

        Assert.Null(Assert.Single(dispatcher.Requests).TenantId);
    }

    [Fact]
    public async Task HttpContextFailsClosedWhenValidatorIsMissingFromDependencyInjection()
    {
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, "tenant-a")
        ], "trusted"));
        _httpContextAccessor.HttpContext = new DefaultHttpContext { User = principal };
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(_httpContextAccessor);
        services.AddScoped<IWorkflowStartDispatcher>(_ => dispatcher);
        services.AddScoped<IWorkflowExecutableStore>(_ => _store);
        services.AddScoped<WorkflowExecutionStartService>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<WorkflowExecutionStartService>();

        await AssertHttpIdentityRefusedAsync(handler, dispatcher);
    }

    [Fact]
    public async Task UsesCallerSourceReferenceOnlyAsAValidatedDispatchSelector()
    {
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(new WorkflowExecutableSourceReference(
            "published-ref-1", "artifact-1", "WorkflowDefinitionVersion", "version-1", "1.0.0",
            "definition-1", "version-1", "1.0.0", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            WorkflowExecutableReferenceScope.Published));
        var dispatcher = new CapturingStartDispatcher(await NewDispatcherAsync(references));
        var handler = new WorkflowExecutionStartService(dispatcher, _store);

        await handler.ExecuteAsync(new ExecuteWorkflow("artifact-1", SourceReferenceId: "published-ref-1"), CancellationToken.None);

        var selection = Assert.Single(dispatcher.Requests).SourceSelection;
        Assert.Equal("published-ref-1", selection!.SourceReferenceId);
        Assert.Null(selection.ActivationId);
        Assert.Null(selection.SlotId);
    }

    [Fact]
    public async Task RejectsDirectExecutionWhenArtifactHasNoPublishedReference()
    {
        var handler = new WorkflowExecutionStartService(
            await NewDispatcherAsync(new InMemoryWorkflowExecutableSourceReferenceStore()),
            _store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            handler.ExecuteAsync(new ExecuteWorkflow("artifact-1"), CancellationToken.None));

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.NoLiveReference, exception.Reason);
    }

    [Fact]
    public async Task RejectsBlankSourceReferenceSelectorAsInvalidInput()
    {
        var handler = new WorkflowExecutionStartService(await NewDispatcherAsync(), _store);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.ExecuteAsync(new ExecuteWorkflow("artifact-1", SourceReferenceId: " "), CancellationToken.None));

        Assert.Equal("sourceReferenceId", exception.ParamName);
    }

    [Fact]
    public async Task DirectExecutionSelectsOneOfMultiplePublicationsByValidatedReferenceId()
    {
        var references = new InMemoryWorkflowExecutableSourceReferenceStore();
        await references.SaveAsync(PublishedReference("ref-v1", "version-1", "1.0.0"));
        await references.SaveAsync(PublishedReference("ref-v2", "version-2", "2.0.0"));
        var handler = new WorkflowExecutionStartService(await NewDispatcherAsync(references), _store);

        var result = await handler.ExecuteAsync(new ExecuteWorkflow("artifact-1", SourceReferenceId: "ref-v2"), CancellationToken.None);

        Assert.Equal("2.0.0", result.ArtifactVersion);
    }

    [Fact]
    public void ExecuteWorkflowService_DoesNotDependOnInlineExecutor()
    {
        var runtimeCoreAssembly = typeof(IWorkflowStartDispatcher).Assembly;
        var constructorParameters = typeof(WorkflowExecutionStartService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName)
            .ToArray();

        Assert.DoesNotContain("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutor", constructorParameters);
        Assert.Null(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutor"));
    }

    private async Task<WorkflowStartDispatcher> NewDispatcherAsync(
        InMemoryWorkflowExecutableSourceReferenceStore? referenceStore = null)
    {
        if (referenceStore is null)
        {
            referenceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
            await referenceStore.SaveAsync(new WorkflowExecutableSourceReference(
                "published-ref-default", "artifact-1", "WorkflowDefinitionVersion", "version-1", "1.0.0",
                "definition-1", "version-1", "1.0.0", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                WorkflowExecutableReferenceScope.Published));
        }

        return new(
            _store,
            referenceStore,
            new RecordingAgentProvider(),
            new FixedRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero)));
    }

    private WorkflowExecutionStartService HttpHandler(
        IWorkflowStartDispatcher dispatcher,
        IEnumerable<Claim> claims,
        string authenticationType = "trusted")
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
        _httpContextAccessor.HttpContext = new DefaultHttpContext { User = principal };
        var validator = new NormalizedPrincipalValidator(Options.Create(new FoundationIdentityOptions
        {
            NormalizedAuthenticationTypes = new HashSet<string>(["trusted"], StringComparer.Ordinal)
        }));
        return new WorkflowExecutionStartService(dispatcher, _store, _httpContextAccessor, validator);
    }

    private static async Task AssertHttpIdentityRefusedAsync(
        WorkflowExecutionStartService handler,
        CapturingStartDispatcher dispatcher)
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            handler.ExecuteAsync(new ExecuteWorkflow("artifact-1"), CancellationToken.None));

        Assert.Equal("Elsa.Workflows.Runtime.Api.Endpoints.RuntimeResourceMissingSignal", exception.GetType().FullName);
        Assert.Empty(dispatcher.Requests);
    }

    private static WorkflowExecutable NewExecutable() =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: NewNode("node-root"),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static WorkflowExecutableSourceReference PublishedReference(
        string sourceReferenceId,
        string definitionVersionId,
        string artifactVersion) =>
        new(
            sourceReferenceId, "artifact-1", "WorkflowDefinitionVersion", definitionVersionId, artifactVersion,
            "definition-1", definitionVersionId, artifactVersion, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            WorkflowExecutableReferenceScope.Published);

    private static ExecutableNode NewNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, System.Text.Json.JsonSerializer.SerializeToElement(new { type = "test" })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

    private sealed class RecordingAgentProvider : IWorkflowExecutionActorProvider
    {
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IWorkflowExecutionActor>(new RecordingAgent(request.WorkflowExecutionId));

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAgent(string workflowExecutionId) : IWorkflowExecutionActor
    {
        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            workflowExecutionId: workflowExecutionId,
            agentId: $"recording:{workflowExecutionId}",
            providerName: "Recording",
            status: WorkflowExecutionActorStatus.Active,
            capabilities: WorkflowExecutionActorCapabilities.None,
            activatedAt: DateTimeOffset.UtcNow);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: envelope.WorkflowExecutionId,
                status: WorkflowExecutionCommandDispatchStatus.Accepted,
                recordedAt: DateTimeOffset.UtcNow));
    }

    private sealed class FixedRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        public string NewWorkflowExecutionId() => "wfexec-fixed";

        public string NewWorkflowExecutionCommandId() => "command-fixed";

        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-fixed";

        public string NewActivityExecutionId() => throw new NotSupportedException("HTTP start dispatch does not schedule activities.");
    }

    private sealed class CapturingStartDispatcher(IWorkflowStartDispatcher inner) : IWorkflowStartDispatcher
    {
        public List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.DispatchAsync(request, requiredScope, dispatchOptions, cancellationToken);
        }
    }
}
