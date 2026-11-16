using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Internal opt-in composition controls; no production feature/host or actual activity callback is installed.</summary>
public sealed class RuntimeHistoryCompositionTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_service_resolution_remains_lazy_before_an_admission_scope_exists(bool enabled)
    {
        var services = Services(new Access(global: true));
        RuntimeHistoryRegistration.AddServices(services, new() { Enabled = enabled });
        var reads = 0;
        services.AddScoped<IRuntimeHistoryStore>(_ => { reads++; throw new IOException("reader unavailable"); });
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>());
        Assert.NotEmpty(scope.ServiceProvider.GetServices<IRuntimeCheckpointCommitEnricher>());
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Module_absence_and_explicit_disable_keep_distinct_known_admission_choices_without_reading_history(bool installed)
    {
        var services = Services();
        if (installed) RuntimeHistoryRegistration.AddServices(services, new() { Enabled = false });
        var reads = 0;
        services.AddScoped<IRuntimeHistoryStore>(_ => { reads++; throw new IOException("reader unavailable"); });
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var dispatcher = Assert.IsType<WorkflowStartDispatcher>(scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>());
        var admission = dispatcher.HistoryAdmission!.Invoke("admission-run");
        Assert.Null(admission.Policy);
        if (installed) Assert.False(admission.Enabled); else Assert.Null(admission.Enabled);
        Assert.Equal(0, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Qualified_internal_composition_retains_capture_when_new_admission_is_disabled(bool decorated)
    {
        var access = new Access();
        var services = Services(access);
        var options = new RuntimeHistoryOptions();
        RuntimeHistoryRegistration.AddServices(services, options);
        RuntimeHistoryRegistration.AddServices(services, options); // idempotent, including source declaration and enricher.
        if (decorated) services.AddPersistenceModes();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var selected = scope.ServiceProvider;
        var dispatcher = Assert.IsType<WorkflowStartDispatcher>(selected.GetRequiredService<IWorkflowStartDispatcher>());
        var admission = dispatcher.HistoryAdmission!.Invoke("admission-run");
        var policy = Assert.IsType<RuntimeHistoryCapturePolicy>(admission.Policy);
        Assert.True(admission.Enabled);
        Assert.True(policy.Required);
        Assert.Contains("IncidentProjectionReconciled", policy.SupportedKinds);
        Assert.DoesNotContain("ActivityContinuationStarted", policy.SupportedKinds); // Activities owner is not composed here.
        Assert.DoesNotContain("ChildWorkflowSettled", policy.SupportedKinds); // Post-commit owner is not qualified.
        Assert.Single(selected.GetServices<IRuntimeCheckpointCommitEnricher>().OfType<RuntimeHistoryCapture>());
        var executions = selected.GetRequiredService<IWorkflowExecutionStateStore>();
        await executions.SaveAsync(Workflow(policy));
        var committer = selected.GetRequiredService<RuntimeCheckpointCommitter>();
        await committer.CommitAsync(Commit("started", ActivityExecutionStatus.Running));
        options.Enabled = false;
        var next = dispatcher.HistoryAdmission.Invoke("admission-run");
        Assert.Null(next.Policy);
        Assert.False(next.Enabled);
        await committer.CommitAsync(Commit("completed", ActivityExecutionStatus.Completed));
        var page = await selected.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("run"));
        Assert.Equal(new[] { "ActivityStarted", "ActivityCompleted" }, page.Items.Select(e => e.Name));
        Assert.Equal(new long[] { 1, 2 }, page.Items.Select(e => e.Sequence));
        Assert.True(policy.IsEquivalentTo(page.Head!.Policy));
        Assert.False(page.Head.CapturedFromStart); // This component seeds native admission; it does not claim a start journey.
    }

    [Fact]
    public void Admission_snapshots_the_enabled_choice_before_provider_qualification_can_change_options()
    {
        var options = new RuntimeHistoryOptions();
        var services = Services(new Access());
        RuntimeHistoryRegistration.AddServices(services, options);
        var readerResolutions = 0;
        services.AddScoped<IRuntimeHistoryStore>(provider =>
        {
            readerResolutions++;
            options.Enabled = false;
            return new InMemoryRuntimeHistoryStore(provider.GetRequiredService<InMemoryRuntimeCheckpointStoreState>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(), provider.GetRequiredService<IRuntimeRecoveryContinuationCodec>());
        });
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var admission = RuntimeHistoryRegistration.ResolveAdmission(scope.ServiceProvider, "admission-run");
        Assert.True(admission.Enabled);
        Assert.True(admission.Policy!.Required);
        Assert.False(options.Enabled);
        var later = RuntimeHistoryRegistration.ResolveAdmission(scope.ServiceProvider, "later-admission");
        Assert.False(later.Enabled);
        Assert.Null(later.Policy);
        Assert.Equal(1, readerResolutions);
    }

    [Theory]
    [InlineData("writer")]
    [InlineData("reader")]
    [InlineData("capture")]
    [InlineData("source")]
    [InlineData("values")]
    [InlineData("scope")]
    public void An_incoherent_component_refuses_before_new_admission(string missing)
    {
        var services = Services(new Access(global: missing == "scope"));
        RuntimeHistoryRegistration.AddServices(services);
        switch (missing)
        {
            case "writer": services.AddScoped<IRuntimeCheckpointCommitStore>(_ => new LegacyWriter()); break;
            case "reader": services.AddScoped<IRuntimeHistoryStore>(_ => throw new IOException("selected reader unavailable")); break;
            case "capture": services.RemoveAll<IRuntimeCheckpointCommitEnricher>(); break;
            case "source": services.RemoveAll<IRuntimeHistorySourceExtension>(); break;
            case "values": services.AddScoped<IRuntimeActivityExecutionInspectionAccumulator>(_ => new LegacyValues()); break;
        }
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var exception = Assert.Throws<RuntimeSchedulerWorkUnavailableException>(() => RuntimeHistoryRegistration.ResolveAdmission(scope.ServiceProvider, "admission-run"));
        Assert.Equal("runtime.history.capture-unavailable", exception.Code);
        if (missing == "reader") Assert.IsType<IOException>(exception.InnerException);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
    }

    [Fact]
    public void Qualified_vocabulary_uses_only_declared_direct_owners_and_never_silently_inherits_source_intent_support()
    {
        var services = Services(new Access());
        RuntimeHistoryRegistration.AddServices(services);
        services.AddSingleton(new RuntimeHistorySourceSupport([RuntimeHistorySourceFactKind.ActivityContinuationStarted]));
        services.AddSingleton(RuntimeHistorySourceSupport.ForSourceOwner("future-owner", [RuntimeHistorySourceFactKind.ChildWorkflowSettled]));
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var policy = RuntimeHistoryRegistration.ResolvePolicy(scope.ServiceProvider, "admission-run")!;
        Assert.Contains("ActivityContinuationStarted", policy.SupportedKinds);
        Assert.DoesNotContain("ChildWorkflowSettled", policy.SupportedKinds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Admission_dependency_errors_preserve_malformed_data_and_cancellation_instead_of_becoming_retryable(bool cancelled)
    {
        Exception cause = cancelled ? new OperationCanceledException("cancelled") : new InvalidDataException("malformed");
        var services = Services(new Access());
        RuntimeHistoryRegistration.AddServices(services);
        services.AddScoped<IRuntimeHistoryStore>(_ => throw cause);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var caught = Record.Exception(() => RuntimeHistoryRegistration.ResolvePolicy(scope.ServiceProvider, "admission-run"));
        if (cancelled) Assert.Same(cause, caught);
        else
        {
            var integrity = Assert.IsType<RuntimeHistoryIntegrityException>(caught);
            Assert.Same(cause, integrity.InnerException);
            Assert.Contains("admission-run", integrity.Message);
        }
    }

    [Theory]
    [InlineData("argument")]
    [InlineData("cancelled")]
    [InlineData("provider")]
    public void Contextual_provider_wrappers_do_not_hide_hard_validation_or_cancellation(string kind)
    {
        Exception inner = kind switch
        { "argument" => new ArgumentException("invalid"), "cancelled" => new OperationCanceledException("cancelled"), _ => new IOException("unavailable") };
        var failure = new InvalidOperationException("resolution context", inner);
        var services = Services(new Access());
        RuntimeHistoryRegistration.AddServices(services);
        services.AddScoped<IRuntimeHistoryStore>(_ => throw failure);
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var caught = Record.Exception(() => RuntimeHistoryRegistration.ResolvePolicy(scope.ServiceProvider, "admission-run"));
        if (kind == "provider") Assert.Same(failure, Assert.IsType<RuntimeSchedulerWorkUnavailableException>(caught).InnerException);
        else Assert.Same(failure, caught);
    }

    private static ServiceCollection Services(Access? access = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (access is not null) services.AddSingleton<IPersistenceAccessContextAccessor>(access);
        services.AddWorkflowRuntime();
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "composition-history-tests-signing-key-at-least-32-bytes");
        return services;
    }
    private static WorkflowExecutionState Workflow(RuntimeHistoryCapturePolicy policy) => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, null, new Dictionary<string, string>())
        { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true };
    private static RuntimeCheckpointCommit Commit(string id, ActivityExecutionStatus status)
    {
        var activity = new ActivityExecutionState(new("activity", "run", "node", "authored", "test", "1"), status, null, 1, At, At,
            status == ActivityExecutionStatus.Completed ? At : null, null, null, null, null,
            ActivitySchedulingProvenance.From("run", null, null, null, null, null, null, "test"), 0, [], [], 0, 0, new Dictionary<string, string>());
        return new(id, new("checkpoint-" + id, status == ActivityExecutionStatus.Completed ? RuntimeCheckpointNames.ActivityCompleted : RuntimeCheckpointNames.ActivityStarted,
            "run", At, ["activity"], new Dictionary<string, string>()),
            new(null, null, [new("activity", RuntimeStateChangeOperation.Upsert, activity, new Dictionary<string, string>())], [], [], [], []), [], new Dictionary<string, string>());
    }
    private sealed class Access(bool global = false) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => global ? PersistenceAccessContext.Global : PersistenceAccessContext.Scoped(new("scope-a")); }
    private sealed class LegacyWriter : IRuntimeCheckpointCommitStore
    { public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
    private sealed class LegacyValues : IRuntimeActivityExecutionInspectionAccumulator
    {
        public ValueTask<ActivityExecutionInspectionProjection> BuildProjectionAsync(ActivityExecutionState state, string checkpointId, DateTimeOffset committedAt,
            IReadOnlyCollection<string>? outcomeNames = null, IReadOnlyCollection<ActivityExecutionBookmarkSummary>? bookmarks = null,
            IReadOnlyCollection<ActivityExecutionIncidentSummary>? incidents = null, IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
