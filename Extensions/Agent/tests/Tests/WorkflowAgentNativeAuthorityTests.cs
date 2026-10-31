using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Agent.Api.Extensions;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Workflows;
using Elsa.Agent.Workflows.Services;
using Elsa.Agent.Workflows.Tools;
using Elsa.Api.AspNetCore;
using Elsa.Events;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Exceptions;
using Elsa.Testing;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Api.Endpoints.Definitions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>In-process native endpoint/permission composition. No listener, database or live provider.</summary>
public sealed class WorkflowAgentNativeAuthorityTests
{
    [Theory]
    [InlineData("definition")]
    [InlineData("folder")]
    public async Task Owning_GET_read_refusal_also_withholds_initial_workflow_context(string deniedResource)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Policy.Deny = deniedResource;
        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());

        var collected = await fixture.CollectAsync();

        Assert.False(collected.Succeeded);
        Assert.DoesNotContain(collected.Value?.Attachments ?? [], item => item.ContentType == "workflow.definition");
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("folder")]
    public async Task Owning_GET_refusal_after_message_acceptance_prevents_provider_entry(string deniedResource)
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.AcceptAsync();
        fixture.Policy.Deny = deniedResource;
        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());

        var events = await fixture.StreamAsync();

        Assert.Equal(0, fixture.Provider.Calls);
        Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Error);
        Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.MessageDelta);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("folder")]
    public async Task Owning_GET_refusal_withholds_previously_authorized_resource_derived_history(string deniedResource)
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.AcceptAsync();
        var original = await fixture.StreamAsync();
        Assert.Contains(original, item => item.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Equal(1, fixture.Provider.Calls);
        await fixture.Sessions.AddMessageAsync(fixture.Session.Id, new(AgentRole.User, "Continue explaining the earlier graph.",
            AgentMessageStatus.Pending, null, [], []));
        fixture.Policy.Deny = deniedResource;
        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());

        var events = await fixture.StreamAsync();

        Assert.Equal(1, fixture.Provider.Calls);
        Assert.Contains(events, item => item.Kind == AgentStreamEventKind.Error);
        Assert.DoesNotContain(events, item => item.Kind == AgentStreamEventKind.MessageDelta);
    }

    [Theory]
    [InlineData("definition", false, false)]
    [InlineData("folder", false, false)]
    [InlineData("definition", true, false)]
    [InlineData("folder", true, false)]
    [InlineData("definition", false, true)]
    [InlineData("folder", false, true)]
    public async Task Owning_GET_refusal_withholds_genuine_read_results_even_without_a_context_attachment(string deniedResource, bool historical, bool continuation)
    {
        using var fixture = await Fixture.CreateAsync();
        var context = await fixture.CollectAsync();
        Assert.True(context.Succeeded, context.Error?.Message);
        var invocation = new AgentToolInvocation("native-read", WorkflowReadGraphTool.ToolName, fixture.Session.Id, "actor",
            new Dictionary<string, object?>(), context.Value!.Attachments) { TenantId = "tenant", TurnId = "read-turn" };
        var dispatch = await fixture.Services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(invocation, fixture.Session.Policy);
        Assert.Equal(AgentToolDispatchOutcome.Executed, dispatch.Outcome);
        var result = Assert.IsType<AgentToolResult>(dispatch.Result);
        var originalResult = JsonSerializer.Serialize(result);
        fixture.Policy.Deny = deniedResource;
        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());

        var admission = await fixture.Source.AdmitDisclosureAsync(new(fixture.Session.Id, [], [result], continuation) { IsHistorical = historical });

        Assert.Contains(admission.Diagnostics, item => item.Required);
        Assert.Empty(admission.Attachments);
        Assert.Equal(originalResult, JsonSerializer.Serialize(result));
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("folder")]
    public async Task Owning_GET_refusal_after_approval_prevents_an_executable_local_working_state_request(string deniedResource)
    {
        using var fixture = await Fixture.CreateAsync();
        var proposal = await fixture.PrepareAndApproveAsync();
        fixture.Policy.Deny = deniedResource;
        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());

        var execution = await fixture.Proposals.ExecuteAsync(proposal.Id, "actor", proposal.BaseRevision);

        Assert.False(execution.Succeeded);
        Assert.Null(execution.Value?.ClientInvocation);
        Assert.False(execution.Value?.Executed ?? false);
        Assert.Equal(0, fixture.Definitions.Writes);
    }

    [Fact]
    public async Task Visible_definition_retains_dirty_snapshot_and_reaches_provider_and_reviewed_local_dispatch()
    {
        using var fixture = await Fixture.CreateAsync();
        Assert.Equal(StatusCodes.Status200OK, await fixture.OwningGetAsync());
        var context = await fixture.CollectAsync();
        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal("dirty-revision", Assert.Single(context.Value!.Attachments, item => item.ContentType == "workflow.definition").Target?.Revision);
        await fixture.AcceptAsync();
        Assert.Contains(await fixture.StreamAsync(), item => item.Kind == AgentStreamEventKind.MessageDelta);
        Assert.Equal(1, fixture.Provider.Calls);
        var proposal = await fixture.PrepareAndApproveAsync();

        var execution = await fixture.Proposals.ExecuteAsync(proposal.Id, "actor", proposal.BaseRevision);

        Assert.True(execution.Succeeded, execution.Error?.Message);
        Assert.Equal("awaiting-client", execution.Value?.Status);
        Assert.NotNull(execution.Value?.ClientInvocation);
        Assert.Equal("dirty-revision", execution.Value?.ClientInvocation?.Target?.Revision);
        Assert.Equal(0, fixture.Definitions.Writes);
        Assert.True(fixture.Policy.TypedResourceChecks > 0);
    }

    [Fact]
    public async Task A_typed_management_refusal_is_rechecked_after_approval_even_when_native_read_stays_allowed()
    {
        using var fixture = await Fixture.CreateAsync();
        var proposal = await fixture.PrepareAndApproveAsync();
        fixture.Policy.Deny = "manage";
        Assert.Equal(StatusCodes.Status200OK, await fixture.OwningGetAsync());
        var native = await fixture.Services.GetRequiredService<IPermissionAuthorizationService>().AuthorizeAsync(new(
            fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User, "workflows/design:manage", "tenant", new WorkflowLibraryResource(DefinitionId: "wf-1")));
        Assert.False(native.Succeeded);

        var execution = await fixture.Proposals.ExecuteAsync(proposal.Id, "actor", proposal.BaseRevision);

        Assert.False(execution.Succeeded);
        Assert.Null(execution.Value?.ClientInvocation);
        Assert.Equal(0, fixture.Definitions.Writes);
    }

    [Fact]
    public async Task Absent_owner_authority_fails_closed_for_native_details_context_and_tool_dispatch()
    {
        using var fixture = await Fixture.CreateAsync(services => services.RemoveAll<IWorkflowDefinitionAuthorizer>());

        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());
        Assert.False((await fixture.CollectAsync()).Succeeded);
        var read = Assert.Single(await fixture.Services.GetRequiredService<IAgentToolRegistry>().ListAsync(),
            item => item.Descriptor.Name == WorkflowReadGraphTool.ToolName);
        Assert.False((await Assert.IsAssignableFrom<IAgentToolAuthorization>(read).AuthorizeAsync("actor", "tenant",
            new(WorkflowAgentSnapshot.ModuleId, "workflow-definition", "wf-1", "dirty-revision"))).Allowed);
    }

    [Fact]
    public async Task Duplicate_owner_authority_fails_startup_and_cannot_choose_an_allowing_implementation()
    {
        using var fixture = await Fixture.CreateAsync(services => services.AddSingleton<IWorkflowDefinitionAuthorizer>(new AllowOwner()));
        var registrations = fixture.Services.GetRequiredService<IServiceCollection>();
        var registration = Assert.Single(registrations, item => item.ServiceType == typeof(IStartupTask)
            && (item.ImplementationInstance?.GetType().Name == "WorkflowDefinitionAuthorizationStartupTask"
                || item.ImplementationType?.Name == "WorkflowDefinitionAuthorizationStartupTask"));
        var startup = registration.ImplementationInstance is IStartupTask instance ? instance
            : Assert.IsAssignableFrom<IStartupTask>(ActivatorUtilities.CreateInstance(fixture.Services, registration.ImplementationType!));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => startup.ExecuteAsync(CancellationToken.None));

        Assert.Contains(nameof(IWorkflowDefinitionAuthorizer), error.Message);
        Assert.Contains("multiple registrations", error.Message);
        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());
        Assert.False((await fixture.CollectAsync()).Succeeded);
    }

    [Fact]
    public async Task A_missing_native_definition_keeps_GET_404_and_cannot_authorize_a_browser_supplied_graph()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Definitions.Exists = false;
        Assert.Equal(StatusCodes.Status404NotFound, await fixture.OwningGetAsync());
        var nativeReads = fixture.Definitions.GetReads;

        var context = await fixture.CollectAsync();

        Assert.False(context.Succeeded);
        Assert.Equal(nativeReads, fixture.Definitions.GetReads);
        Assert.True(fixture.Definitions.Finds > 0);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Metadata_from_another_tenant_cannot_authorize_the_browser_supplied_working_state()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Definitions.Tenant = "another-tenant";

        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());
        Assert.False((await fixture.CollectAsync()).Succeeded);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Installed_guidance_cannot_supply_concrete_resource_authority_to_workflow_tools()
    {
        using var fixture = await Fixture.CreateAsync();
        var read = Assert.Single(await fixture.Services.GetRequiredService<IAgentToolRegistry>().ListAsync(),
            item => item.Descriptor.Name == WorkflowReadGraphTool.ToolName);

        var decision = await Assert.IsAssignableFrom<IAgentToolAuthorization>(read).AuthorizeAsync("actor", "tenant", null);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Unexpected_owner_reader_failures_are_not_downgraded_to_an_allowing_resource_decision()
    {
        using var fixture = await Fixture.CreateAsync(services => services.Replace(
            ServiceDescriptor.Scoped<IWorkflowDefinitionDetailsReader>(_ => new FailingReader())));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Services
            .GetRequiredService<IWorkflowDefinitionAuthorizer>().CanReadAsync("wf-1").AsTask());

        Assert.Equal("Owned reader is unavailable.", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_tenant_metadata_does_not_become_visible_to_a_tenant_reader_through_source_ownership(bool sourceOwned)
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Definitions.Tenant = null;
        fixture.Definitions.SourceOwned = sourceOwned;

        Assert.Equal(StatusCodes.Status403Forbidden, await fixture.OwningGetAsync());
        Assert.False((await fixture.CollectAsync()).Succeeded);
        Assert.Equal(0, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Source_owned_metadata_in_the_current_tenant_retains_its_native_read_authority()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Definitions.SourceOwned = true;
        Assert.Equal(StatusCodes.Status200OK, await fixture.OwningGetAsync());

        var context = await fixture.CollectAsync();

        Assert.True(context.Succeeded, context.Error?.Message);
        Assert.Equal("dirty-revision", Assert.Single(context.Value!.Attachments, item => item.ContentType == "workflow.definition").Target?.Revision);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal IServiceProvider Services
        {
            get
            {
                var provider = scope.ServiceProvider;
                var accessor = provider.GetRequiredService<IHttpContextAccessor>();
                // Each direct in-process operation supplies its own request boundary. AsyncLocal
                // assignment in a completed async GET helper must not stand in for its caller's request.
                if (accessor.HttpContext is null)
                    accessor.HttpContext = new DefaultHttpContext { RequestServices = provider, User = principal };
                return provider;
            }
        }
        internal TypedPolicy Policy { get; } = new();
        internal DefinitionStore Definitions { get; } = new();
        internal ObservationProvider Provider { get; } = new();
        internal AgentSession Session { get; private set; } = null!;
        internal IAgentSessionService Sessions => Services.GetRequiredService<IAgentSessionService>();
        internal IAgentProposalService Proposals => Services.GetRequiredService<IAgentProposalService>();
        internal DefaultWorkflowAgentContextProvider Source => Services.GetServices<IAgentContextProvider>().OfType<DefaultWorkflowAgentContextProvider>().Single();
        private readonly ClaimsPrincipal principal = new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "actor"), new Claim(IdentityClaimTypes.Normalized, "v1"),
            new Claim(IdentityClaimTypes.TenantId, "tenant"), new Claim(IdentityClaimTypes.Permission, "workflows/design:manage")], "WorkflowNativeAuthority"));

        private Fixture(Action<IServiceCollection>? configure)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRouting().AddElsaEndpoints();
            new EventsFeature().ConfigureServices(services);
            services.AddFoundationAgentAbstractions();
            services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(["WorkflowNativeAuthority"], StringComparer.Ordinal));
            new WorkflowsDesignApiFeature().ConfigureServices(services);
            services.AddFoundationAgentApi();
            new FoundationWorkflowsAgentFeature().ConfigureServices(services);
            services.AddSingleton<IPermissionResourceHandler>(Policy);
            services.AddSingleton<IWorkflowLibraryVisibilityPolicy>(new Visibility());
            services.AddSingleton<IWorkflowDefinitionStore>(Definitions);
            services.AddSingleton<IWorkflowDefinitionVersionStore>(EmptyStore<IWorkflowDefinitionVersionStore>.Create());
            services.AddSingleton<IWorkflowDefinitionDraftStore>(EmptyStore<IWorkflowDefinitionDraftStore>.Create());
            services.AddSingleton<IWorkflowLibraryStore>(EmptyStore<IWorkflowLibraryStore>.Create());
            services.AddSingleton<IAgentProvider>(Provider);
            configure?.Invoke(services);
            // Only the selected endpoint is activated. Other API routes retain their ordinary
            // registrations without requiring unrelated authoring/runtime fixtures here.
            root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            scope = root.CreateScope();
            SetContext(new DefaultHttpContext());
            Assert.IsType<PermissionAuthorizationService>(Services.GetRequiredService<IPermissionAuthorizationService>());
        }

        internal static async Task<Fixture> CreateAsync(Action<IServiceCollection>? configure = null)
        {
            var fixture = new Fixture(configure);
            fixture.Session = await fixture.Sessions.CreateAsync(new("tenant", "actor", "conversation", fixture.Provider.ProviderId,
                "build", null, AgentPolicy.Default, new Dictionary<string, string>()));
            return fixture;
        }

        private void SetContext(HttpContext context)
        {
            context.RequestServices = scope.ServiceProvider;
            context.User = principal;
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        }

        internal async Task<int> OwningGetAsync()
        {
            var routes = new TestEndpointRouteBuilder(Services);
            WorkflowsDesignApi.MapWorkflowsDesignApi(routes);
            var endpoint = routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().Single(item =>
                item.RoutePattern.RawText?.EndsWith("/definitions/{definitionId}", StringComparison.Ordinal) == true
                && item.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true);
            var context = new DefaultHttpContext();
            SetContext(context);
            context.SetEndpoint(endpoint);
            context.Request.Method = "GET";
            context.Request.Path = "/design/workflows/definitions/wf-1";
            context.Request.RouteValues["definitionId"] = "wf-1";
            context.Response.Body = new MemoryStream();
            await endpoint.RequestDelegate!(context);
            return context.Response.StatusCode;
        }

        internal Task<AgentResult<AgentContextContribution>> CollectAsync() => Services.GetRequiredService<IAgentContextCollector>()
            .CollectAsync(Session.Policy, WorkflowAgentTests.Request(Snapshot()) with { SessionId = Session.Id });

        private static AgentContextAttachment Snapshot() => WorkflowAgentTests.Snapshot() with
        {
            Target = new(WorkflowAgentSnapshot.ModuleId, "workflow-definition", "wf-1", "dirty-revision"),
            Content = JsonSerializer.SerializeToElement(new
            {
                workflowDefinitionId = "wf-1", draftId = "draft-1", revision = "dirty-revision", selectedNodeId = "node-1",
                activities = new[] { new { id = "node-1", type = "Elsa.WriteLine", displayName = "Unsaved private graph" } },
                connections = Array.Empty<object>(), positions = new[] { new { nodeId = "node-1", x = 10, y = 20 } }
            })
        };

        internal async Task AcceptAsync()
        {
            var context = await CollectAsync();
            Assert.True(context.Succeeded, context.Error?.Message);
            await Sessions.AddMessageAsync(Session.Id, new(AgentRole.User, "Explain the unsaved graph.", AgentMessageStatus.Pending,
                null, context.Value!.Attachments.Select(item => item.Id).ToArray(), context.Value.Attachments));
        }

        internal async Task<List<AgentStreamEvent>> StreamAsync()
        {
            var events = new List<AgentStreamEvent>();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await foreach (var item in Services.GetRequiredService<IAgentStreamingService>().StreamAsync(Session.Id, cancellation.Token)) events.Add(item);
            return events;
        }

        internal async Task<AgentActionProposal> PrepareAndApproveAsync()
        {
            var context = await CollectAsync();
            Assert.True(context.Succeeded, context.Error?.Message);
            var dispatch = await Services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(new("native-write", "workflow_set_designer_position",
                Session.Id, "actor", new Dictionary<string, object?> { ["activityId"] = "node-1", ["x"] = 50, ["y"] = 60 }, context.Value!.Attachments)
            { TenantId = "tenant", TurnId = "write-turn", Client = Snapshot().Client }, Session.Policy);
            Assert.Equal(AgentToolDispatchOutcome.ProposalCreated, dispatch.Outcome);
            var proposal = Assert.IsType<AgentActionProposal>(dispatch.Proposal);
            var approved = await Proposals.ApproveAsync(proposal.Id, "actor", proposal.BaseRevision);
            Assert.True(approved.Succeeded, approved.Error?.Message);
            return Assert.IsType<AgentActionProposal>(approved.Value);
        }

        public void Dispose() { scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null; scope.Dispose(); root.Dispose(); }
    }

    private sealed class TypedPolicy : IPermissionResourceHandler
    {
        internal string? Deny;
        internal int TypedResourceChecks;
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            if (context.Resource is not WorkflowLibraryResource resource) return ValueTask.FromResult<PermissionEvaluationResult?>(null);
            TypedResourceChecks++;
            return ValueTask.FromResult<PermissionEvaluationResult?>(Deny == "definition" && resource.DefinitionId == "wf-1"
                || Deny == "folder" && resource.FolderId == "finance"
                || Deny == "manage" && resource.DefinitionId == "wf-1" && context.Permission == "workflows/design:manage" ? PermissionEvaluationResult.Denied() : null);
        }
    }

    private sealed class Visibility : IWorkflowLibraryVisibilityPolicy
    {
        public ValueTask<WorkflowLibraryReadScope> GetScopeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WorkflowLibraryReadScope("native-policy-v1", DeniedDefinitionIds: ["hidden"]));
    }

    private sealed class DefinitionStore : IWorkflowDefinitionStore
    {
        internal bool Exists = true;
        internal string? Tenant = "tenant";
        internal bool SourceOwned;
        internal int GetReads, Finds;
        internal int Writes => 0; // This port has no writes; client dispatch does not persist a workflow.
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            GetReads++;
            return Task.FromResult(Exists ? Metadata(id) : throw new EntityNotFoundException("The workflow definition was not found."));
        }
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            Finds++;
            return Task.FromResult<WorkflowDefinition?>(Exists && id == "wf-1" ? Metadata(id) : null);
        }
        private WorkflowDefinition Metadata(string id) => new() { Id = id, Name = "Persisted workflow metadata", TenantId = Tenant, IsSourceOwned = SourceOwned };
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("No workflow inventory is needed for one-resource authority.");
    }

    private sealed class AllowOwner : IWorkflowDefinitionAuthorizer
    {
        public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class FailingReader : IWorkflowDefinitionDetailsReader
    {
        public Task AuthorizeReadAsync(string definitionId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Owned reader is unavailable.");
        public Task<WorkflowDefinitionDetailsView> ReadAsync(string definitionId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Owned reader is unavailable.");
    }

    /// <summary>Read-only owner-store data, never an authorization substitute.</summary>
    public class EmptyStore<T> : DispatchProxy where T : class
    {
        internal static T Create() => DispatchProxy.Create<T, EmptyStore<T>>();
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IWorkflowLibraryStore.GetOrganizationAsync) => Task.FromResult<WorkflowOrganization?>(new("wf-1", "invoices",
                [new("finance", null, "Finance", "finance", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "folder-1")], [], "organization-1")),
            nameof(IWorkflowDefinitionVersionStore.ListByDefinitionAsync) => Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([]),
            nameof(IWorkflowDefinitionDraftStore.FindByWorkflowDefinitionIdAsync) => Task.FromResult<WorkflowDefinitionDraft?>(null),
            _ => throw new NotSupportedException("The authority check must not read authored content or inventory: " + method?.Name)
        };
    }

    private sealed class ObservationProvider : IAgentProvider
    {
        private readonly DeterministicAgentProvider inner = new();
        internal int Calls;
        public string ProviderId => inner.ProviderId;
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => inner.CreateSessionAsync(session, cancellationToken);
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => inner.GetDiagnosticsAsync(cancellationToken);
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            Calls++;
            yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.MessageDelta, "Resource-derived graph explanation.", null, null, DateTimeOffset.UtcNow);
            yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }
    }
}
