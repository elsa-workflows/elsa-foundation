using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.ActorFoundation.Hosting;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Inspection.Core;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Elsa.Weaver.Workflows.Runtime.Tests.WorkflowRuntimeWeaverTestHost;

namespace Elsa.Weaver.Workflows.Runtime.Tests;

// Spec 1193 SC-009 (FR-027): a real Runtime-only CShells shell exercises the retained Runtime API registrations
// over the publication stores and the Actor read port. No Workflows Design assembly is composed or loaded.
public sealed class WorkflowRuntimeOnlyHostTests : IAsyncDisposable
{
    private const string ShellName = "runtime-only-weaver";
    private readonly ServiceProvider root;

    public WorkflowRuntimeOnlyHostTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCShells(shells => shells
            .WithAssemblies(typeof(EventsFeature).Assembly, typeof(ApiCapabilitiesFeature).Assembly,
                typeof(WeaverWorkflowsRuntimeFeature).Assembly, typeof(TestWeaverInspectionFeature).Assembly)
            .AddShell(ShellName, shell => shell.WithFeature<TestWeaverInspectionFeature>()));
        root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_started_runtime_only_shell_reads_a_seeded_Executable_through_the_runtime_inspection()
    {
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var hash = await SeedAsync(shell);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        Assert.Equal("WorkflowExecutableInspection", Assert.Single(services.GetServices<IWorkflowExecutableInspection>()).GetType().Name);
        var session = await services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider", "explain",
            null, WeaverPolicy.Default, new Dictionary<string, string>()));

        var collected = await services.GetRequiredService<IWeaverContextCollector>().CollectAsync(WeaverPolicy.Default,
            new(session.Id, "workflow-executable", new Dictionary<string, string>()) { CallerAttachments = [Reference("artifact-runtime-only", hash)] });

        Assert.True(collected.Succeeded, collected.Error?.Message);
        var admitted = Assert.Single(collected.Value!.Attachments, item => item.ContentType == "workflows.executable");
        Assert.Equal(["published-runtime-only"], ((JsonElement)admitted.Content!).GetProperty("sourceReferences").EnumerateArray()
            .Select(item => item.GetProperty("sourceReferenceId").GetString()));
        var tool = services.GetServices<IWeaverTool>().Single(item => item.Descriptor.Name == "workflow_read_executable");
        var prepared = await tool.PrepareAsync(new("call-1", tool.Descriptor.Name, session.Id, session.ActorId, new Dictionary<string, object?>(),
            collected.Value.Attachments) { TenantId = session.TenantId });
        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var data = prepared.Value!.Preview.Data;
        Assert.Equal((hash, "node-runtime-only"), (data.GetProperty("artifactHash").GetString(), data.GetProperty("graph").GetProperty("executableNodeId").GetString()));

        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name ?? "").ToArray();
        Assert.Contains("Elsa.Workflows.Runtime.KernelApi", loaded);
        Assert.DoesNotContain("Elsa.Workflows.Runtime.Api", loaded);
        Assert.DoesNotContain("Elsa.Workflows.Runtime", loaded);
        Assert.DoesNotContain(loaded, name => name.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal));
        Assert.DoesNotContain(loaded, name => name == "Elsa.Weaver.Workflows");
    }

    [Theory]
    [InlineData("workflow.definition", "workflow-definition")]
    [InlineData("workflows.version", "workflow-version")]
    public async Task A_started_runtime_only_shell_reports_Design_journeys_as_explicitly_unavailable(string kind, string resourceType)
    {
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        var session = await services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider", "explain",
            null, WeaverPolicy.Default, new Dictionary<string, string>()));
        var design = new WeaverContextAttachment($"{kind}:1", "workflow", "resource-1", "Design context", kind, WeaverContextSensitivity.Internal, "selection",
            "Design context", JsonSerializer.SerializeToElement(new { definitionId = "definition-1", versionId = "resource-1" }), new Dictionary<string, string>())
        { ModuleId = ModuleId, SchemaVersion = "1", Target = new(ModuleId, resourceType, "resource-1", "resource-1"), Origin = WeaverContextOrigin.ClientSnapshot };

        var collected = await services.GetRequiredService<IWeaverContextCollector>().CollectAsync(WeaverPolicy.Default,
            new(session.Id, "workflow", new Dictionary<string, string>()) { CallerAttachments = [design] });

        Assert.False(collected.Succeeded);
        Assert.Equal("weaver.context.kind_unsupported", Assert.Single(collected.Value!.Diagnostics).Code);
        var tools = (await services.GetRequiredService<IWeaverToolRegistry>().ListAsync()).Select(tool => tool.Descriptor.Name);
        Assert.Equal(["workflow_read_executable", "workflow_read_run"], tools.Where(name => name.StartsWith("workflow_", StringComparison.Ordinal)).Order());
    }

    [Fact]
    public async Task A_started_runtime_only_shell_composes_the_real_Run_inspection_under_the_run_endpoints_authority()
    {
        // Spec 1193 slice G: the shell resolves the real WorkflowRunInspection with every run service it reads through. Under a
        // caller the run endpoints' own per-run authority does not admit (this test host has no Identity, so the inspection
        // authority is a test double that admits nothing), a stored Run reads as absent and the Run context is refused:
        // never a guess, never a broader read than the run page's.
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        var inspection = Assert.Single(services.GetServices<IWorkflowRunInspection>());
        Assert.Equal("WorkflowRunInspection", inspection.GetType().Name);
        var actor = new ActorRef(TestWeaverInspectionFeature.RunId);
        var (_, run) = await services.GetRequiredService<KernelRuns>().GetAsync(actor);
        Assert.Equal(actor, run?.View.Header.Ref);

        Assert.Null(await inspection.GetAsync(new(actor.Id)));
        Assert.Equal(actor.Id, services.GetRequiredService<NoRunInspectionAuthority>().RefusedRunId);

        var session = await services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider", "explain",
            null, WeaverPolicy.Default, new Dictionary<string, string>()));
        var collected = await services.GetRequiredService<IWeaverContextCollector>().CollectAsync(WeaverPolicy.Default,
            new(session.Id, "workflow-run", new Dictionary<string, string>()) { CallerAttachments = [RunReference(actor.Id, null)] });
        Assert.False(collected.Succeeded);
        Assert.Equal("weaver.workflow.run_not_found", Assert.Single(collected.Value!.Diagnostics, item => item.Required).Code);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name ?? ""),
            name => name.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal));
    }

    public ValueTask DisposeAsync() => root.DisposeAsync();

    // The runtime's own stores, as publication would leave them: one Executable and one live published Source Reference.
    private static async Task<string> SeedAsync(IShell shell)
    {
        await using var scope = shell.BeginScope();
        var services = scope.ServiceProvider;
        var now = DateTimeOffset.UtcNow;
        var node = new ExecutableNode("node-runtime-only", "node-runtime-only", "Test.Root", "1.0.0",
            new RuntimeActivityDescriptor("Test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
        var identity = new WorkflowExecutableIdentity("artifact-runtime-only", "definition-runtime-only", "version-runtime-only", "1.0.0", "sha256:runtime-only");
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new WorkflowExecutable(identity, node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), now, new Dictionary<string, string>(), null, null, IncidentStrategyBuiltIns.FaultReference));
        await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new WorkflowExecutableSourceReference("published-runtime-only",
            "artifact-runtime-only", "WorkflowDefinitionVersion", "version-runtime-only", "1.0.0", "definition-runtime-only", "version-runtime-only", "1.0.0",
            now.AddMinutes(-1), now.AddMinutes(-1), WorkflowExecutableReferenceScope.Published));
        return identity.ArtifactHash;
    }
}

/// <summary>Composes retained inspection with the Actor read port and caller authority supplied by this fixture.</summary>
[ShellFeature(name: "TestWeaverInspection", DisplayName = "Test Weaver inspection", Description = "Supplies retained Runtime inspection in composition tests.",
    DependsOn = new object[] { typeof(WeaverWorkflowsRuntimeFeature), typeof(ApiCapabilitiesFeature) })]
public sealed class TestWeaverInspectionFeature : IShellFeature, IPostConfigureShellServices
{
    internal const string RunId = "run-runtime-only";
    private const string Tenant = "tenant-1";
    private readonly WorkflowsRuntimeKernelRetainedApiFeature runtime = new();

    public void ConfigureServices(IServiceCollection services)
    {
        WorkflowPublicationDefaults.Add(services);
        services.AddFoundationIdentityAbstractions();
        services.AddSingleton<IPersistenceAccessContextAccessor, Access>();
        services.AddSingleton(new KernelRuns(EmbeddedTenant.Bind(Tenant), _ => ValueTask.FromResult<IActorReads>(new Reads())));
        runtime.ConfigureServices(services);

        services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer>(_ => new WorkflowRuntimeWeaverTestHost.Authorizer()));
        services.AddScoped<NoRunInspectionAuthority>();
        services.Replace(ServiceDescriptor.Scoped<IActivityInspectionContextAsync>(sp => sp.GetRequiredService<NoRunInspectionAuthority>()));
        services.Replace(ServiceDescriptor.Scoped<IWorkflowRuntimeOperatorAccess>(sp => sp.GetRequiredService<NoRunInspectionAuthority>()));
    }

    public void PostConfigureServices(IServiceCollection services) => runtime.PostConfigureServices(services);

    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(Tenant));
    }

    private sealed class Reads : IActorReads
    {
        private static readonly ActorRef Actor = new(RunId);
        private static readonly ActorView View = new(new ActorHeader(
            Actor, WorkflowActor.ActorType, Tenant, ActorStatus.Faulted, null, null, 2_000,
            JsonSerializer.Serialize(new
            {
                Model = "native", Workflow = "artifact-runtime-only", Definition = "definition-runtime-only",
                StartedAt = 1_000, History = History.Full
            }),
            new Dictionary<string, string> { ["definition"] = "definition-runtime-only" }), null, null, 2_000);

        public ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Tenant, scope.Tenant);
            return ValueTask.FromResult<ActorView?>(actor == Actor ? View : null);
        }

        public ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Tenant, scope.Tenant);
            Assert.Equal(WorkflowActor.ActorType, query.Type);
            Assert.Null(from);
            var matches = query.Labels is null || query.Labels.All(label =>
                View.Header.Labels.GetValueOrDefault(label.Key) == label.Value);
            return ValueTask.FromResult(new Page<ActorView>(matches ? [View] : [], null, View.AsOf));
        }

        public ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

/// <summary>The run endpoints' per-run authority for a caller it does not admit: no structure, values or payloads.</summary>
internal sealed class NoRunInspectionAuthority : IActivityInspectionContextAsync, IWorkflowRuntimeOperatorAccess
{
    public string? RefusedRunId { get; private set; }
    public ValueTask<bool> CanManageAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    public string TenantScope => "global";
    public string AuditSubject => string.Empty;
    public string RequestCorrelationId => string.Empty;
    public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("untrusted");
    public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default)
    {
        RefusedRunId = workflowExecution.WorkflowExecutionId;
        return ValueTask.FromResult(false);
    }
    public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("A caller without run inspection cannot request values.");
    public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("A caller without run inspection cannot request values.");
}
