using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Events;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Events;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Studio.Dashboard.Management;
using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Elsa.Agent.Dashboard.Tests;

// The actual registered Agent collector, registry, invoker, session store and turn engine run here.
// Only the model and application-owned source/identity/host-authority boundaries are deterministic.
internal sealed class DashboardWeaverHost : IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;
    private readonly string database = Path.Combine(Path.GetTempPath(), "elsa-3604-weaver-" + Guid.NewGuid().ToString("N") + ".db");
    internal FixtureActor Actors { get; } = new();
    internal FixtureSource Source { get; }
    internal ScriptedProvider Model { get; } = new();
    internal IServiceProvider Services => scope.ServiceProvider;
    internal IDashboardAuthoringService Authoring => Services.GetRequiredService<IDashboardAuthoringService>();
    internal IDashboardResourceService Resources => Services.GetRequiredService<IDashboardResourceService>();
    internal IAgentSessionService Sessions => Services.GetRequiredService<IAgentSessionService>();
    internal IAgentContextCollector Collector => Services.GetRequiredService<IAgentContextCollector>();

    internal DashboardWeaverHost(FixtureSource? source = null, bool providerAvailable = true)
    {
        Source = source ?? new();
        Model.Available = providerAvailable;
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        new DashboardManagementFeature().ConfigureServices(services);
        services.AddSingleton<IDashboardDatasetExtension>(Source);
        services.AddSingleton<IDashboardQueryValidationExtension>(Source);
        services.AddSingleton<IDashboardQueryExecutionExtension>(Source);
        services.AddSingleton<IDashboardActorContext>(Actors);
        services.AddSingleton<IHostEnvironment>(new FixtureEnvironment());
        services.AddSingleton<IDashboardAudienceDirectory, FixtureDirectory>();
        services.AddDashboardResourcesEntityFrameworkCore(new() { ConnectionString = "Data Source=" + database + ";Pooling=False" });
        new DashboardWeaverFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Singleton<IAgentToolAuthorizer>(new FixtureAuthority(Actors)));
        services.AddSingleton<IAgentProvider>(Model);
        provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        scope = provider.CreateAsyncScope();
    }

    internal async Task InitializeAsync() => await Services.GetRequiredService<DashboardResourceDbContext>().Database.MigrateAsync();
    internal static DashboardQueryRequest Question() => new()
    {
        Query = new() { Dataset = new("fixture.records", "1"), Form = "records", Fields = ["id", "amount"] },
        Visualization = new() { Kind = "table" }
    };
    internal static DashboardAuthoringDraft Draft(DashboardQueryRequest? question = null) => new(
        new(DashboardWeaverProtocol.ModuleId, "question-draft", "local-draft", "draft-1"),
        new("editor-1", "generation-1"), "working-1", question);

    internal async Task<AgentSession> SessionAsync(string? subject = null, string? tenant = null) => await Sessions.CreateAsync(new(
        tenant ?? Actors.Actor.TenantId, subject ?? Actors.Actor.AuthenticationSubjectId!, "fixture-conversation", Model.ProviderId,
        "build", "Authoring test", AgentPolicy.Default with { AutonomyMode = AgentAutonomyMode.AutoReadOnly },
        new Dictionary<string, string> { ["route"] = "/questions" }) { Workspace = "dashboards" });

    internal async Task<AgentResult<AgentContextContribution>> CollectAsync(AgentSession session, DashboardAuthoringDraft draft,
        AgentClientBinding? client = null) => await Collector.CollectAsync(session.Policy, new(session.Id, "dashboard-question", new Dictionary<string, string>())
        { CallerAttachments = [DashboardWeaverProtocol.ClientAttachment(draft)], Client = client ?? DashboardWeaverProtocol.Client(draft),
            Target = DashboardWeaverProtocol.Target(draft.Target) });

    internal async Task<AgentToolInvocation> InvocationAsync(string tool, object arguments, DashboardAuthoringDraft? draft = null)
    {
        draft ??= Draft(Question());
        var session = await SessionAsync();
        var collected = await CollectAsync(session, draft);
        if (!collected.Succeeded) throw new InvalidOperationException(collected.Error?.Message);
        var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(arguments, DashboardWeaverProtocol.Json))!;
        return new("call-1", tool, session.Id, Actors.Actor.AuthenticationSubjectId!, values, collected.Value!.Attachments)
        { TenantId = Actors.Actor.TenantId, TurnId = "turn-1", Client = DashboardWeaverProtocol.Client(draft), AllowMutations = false };
    }

    internal async Task<AgentPreparedToolInvocation> PrepareAsync(IAgentTool tool, AgentToolInvocation invocation)
    {
        var prepared = await tool.PrepareAsync(invocation);
        if (!prepared.Succeeded) throw new InvalidOperationException(prepared.Error?.Message);
        var value = prepared.Value!;
        return new("invocation-1", invocation.SessionId, invocation.TurnId, invocation.ToolCallId, tool.Descriptor.Name,
            tool.Descriptor.ContractVersion, tool.Descriptor.ModuleId, invocation.ActorId, invocation.TenantId,
            value.Target, value.Arguments, "fixture-digest", value.Risk, value.Preview, AgentExecutionLocation.Server,
            false, value.Client, DateTimeOffset.UtcNow.AddMinutes(5)) { DisclosureSourceId = tool.Descriptor.DisclosureSourceId };
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync(); await provider.DisposeAsync();
        foreach (var suffix in new[] { "", "-shm", "-wal" }) if (File.Exists(database + suffix)) File.Delete(database + suffix);
    }
}

internal sealed class FixtureActor : IDashboardActorContext
{
    internal DashboardActor Actor { get; set; } = new("tenant-a", "canonical-user", new HashSet<string>(), new HashSet<string>(["*"]))
        { AuthenticationSubjectId = "external-subject" };
    public ValueTask<DashboardActor> GetAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Actor);
}

internal sealed class FixtureAuthority(FixtureActor actors) : IAgentToolAuthorizer
{
    public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId,
        AgentResourceTarget? target, CancellationToken cancellationToken = default) => Task.FromResult(new AgentPolicyDecision(
        actorId == actors.Actor.AuthenticationSubjectId && tenantId == actors.Actor.TenantId, []));
}

internal sealed class FixtureEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "owned-test-host";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class FixtureDirectory : IDashboardAudienceDirectory
{
    public ValueTask<bool> IsValidAsync(DashboardActor actor, DashboardAudienceGrant grant, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(grant.Kind == "user" && grant.Id == "viewer");
    public ValueTask<DashboardAudienceCandidatePage> SearchAsync(DashboardActor actor, string kind, string? search,
        string? continuationToken = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(new DashboardAudienceCandidatePage([]));
}

internal sealed class FixtureSource : IDashboardDatasetExtension, IDashboardQueryValidationExtension, IDashboardQueryExecutionExtension
{
    internal const string RowCanary = "protected-row-7d820b";
    internal const string ValueCanary = "93746.28";
    internal bool Hidden { get; set; }
    internal bool AmountWithdrawn { get; set; }
    internal bool ThrowOnDiscovery { get; set; }
    internal int AdditionalDatasets { get; set; }
    internal int ExecutionCalls { get; private set; }
    internal List<DashboardRequestScope> ExecutionScopes { get; } = [];
    internal string Title { get; set; } = "Fixture records";
    public IReadOnlyCollection<DashboardDatasetOwner> Ownership => Enumerable.Range(0, AdditionalDatasets + 1)
        .Select(i => new DashboardDatasetOwner(i == 0 ? "fixture.records" : "fixture.extra-" + i, "Fixture")).ToArray();
    public ValueTask CollectAsync(DashboardDatasetsCollecting context, CancellationToken cancellationToken)
    {
        if (ThrowOnDiscovery) throw new IOException("protected-database-connection-canary");
        if (Hidden) return ValueTask.CompletedTask;
        foreach (var owner in Ownership)
            context.Datasets.Add(new()
            {
                Id = owner.DatasetId, OwnerModuleId = "Fixture", SchemaRevision = "1", Title = Title,
                Description = "Authorized source with intentionally unknown coverage.",
                Fields = AmountWithdrawn ? [new("id", "Record", "string", ["eq"], Sortable: true)] :
                    [new("id", "Record", "string", ["eq"], Sortable: true), new("amount", "Amount", "decimal", ["eq", "gte"], Sortable: true)]
            });
        return ValueTask.CompletedTask;
    }
    public ValueTask ValidateAsync(DashboardQueryValidating context, CancellationToken cancellationToken)
    { context.Results.Add(new(true, [])); return ValueTask.CompletedTask; }
    public ValueTask ExecuteAsync(DashboardQueryExecuting context, CancellationToken cancellationToken)
    {
        ExecutionCalls++; ExecutionScopes.Add(context.Query.Scope);
        context.Results.Add(new()
        {
            Columns = [new("id", "Record", "string"), new("amount", "Amount", "decimal")],
            Rows = [new("record-1", new Dictionary<string, DashboardResultCell>
                { ["id"] = new("string", RowCanary), ["amount"] = new("decimal", ValueCanary) })]
        });
        return ValueTask.CompletedTask;
    }
}

internal sealed class ScriptedProvider : IAgentProvider
{
    public string ProviderId => "deterministic-dashboard-test";
    internal bool Available { get; set; } = true;
    internal List<AgentTurnContext> Seen { get; } = [];
    internal List<AgentSession> Created { get; } = [];
    internal string Tool { get; set; } = "dashboard_preview_question";
    internal object Arguments { get; set; } = new { question = DashboardWeaverHost.Question() };
    public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
    { Created.Add(session); return Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>())); }
    public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield(); Seen.Add(context);
        if (context.PendingToolResults.Count == 0)
            yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.ToolCallRequested, null, "call-1", null, DateTimeOffset.UtcNow,
                Payload: new AgentToolCallRequest("call-1", Tool, JsonSerializer.Serialize(Arguments, DashboardWeaverProtocol.Json), AgentRisk.ReadOnly, false));
        else yield return new(Guid.NewGuid().ToString("N"), AgentStreamEventKind.MessageDelta, "Inspect the unsaved native preview.", null, null, DateTimeOffset.UtcNow, AgentResultKind.Message);
    }
    public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AgentToolApprovalResult(false, "Read-only fixture."));
    public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AgentProviderDiagnostics(
        ProviderId, Available, "Deterministic backend test fixture; no model access.", AgentProviderKind.ProviderSdkBinding,
        [AgentProviderOperation.Chat, AgentProviderOperation.Streaming, AgentProviderOperation.ToolApproval], AgentProviderRiskProfile.ReadOnly,
        new Dictionary<string, string>()));
}
