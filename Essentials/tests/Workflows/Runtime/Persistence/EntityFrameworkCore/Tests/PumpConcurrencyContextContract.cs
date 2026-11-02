using System.Collections.Concurrent;
using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static class PumpConcurrencyContextContract
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(string kind, string providerName, string connectionString,
        Action<IServiceCollection, IInterceptor> configureContext)
    {
        var tenant = "pump-concurrency-" + Guid.NewGuid().ToString("N");
        var probe = new QueryProbe();
        var clock = new FakeTimeProvider(Now);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceCore(tenant);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddWorkflowRuntime();
        services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
        {
            Provider = providerName, ConnectionString = connectionString,
            RecoveryContinuationSigningKey = "pump-concurrency-private-recovery-key",
            HierarchyCursorSigningKey = "pump-concurrency-private-hierarchy-key"
        });
        configureContext(services, probe);
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
        services.Replace(ServiceDescriptor.Scoped<IBookmarkResumeDispatcher, QueryDispatcher>());
        services.Replace(ServiceDescriptor.Scoped<IStimulusRouter, QueryRouter>());
        services.Replace(ServiceDescriptor.Scoped<IWorkflowExecutionActorProvider, QueryActors>());
        services.Replace(ServiceDescriptor.Singleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>());
        services.AddSingleton<IRecurringScheduleCalculator, RecurringScheduleCalculator>();
        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var factory = provider.GetRequiredService<IPersistenceOperationScopeFactory>();
        await using (var seed = await factory.CreateAsync(new PersistenceScope(tenant)))
        {
            await seed.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.EnsureCreatedAsync();
            for (var i = 0; i < 4; i++)
            {
                var id = i.ToString();
                if (kind == "timer")
                    await seed.ServiceProvider.GetRequiredService<IDurableTimerStore>().SaveAsync(new DurableTimer(id, id, "timer", id, Now, Now));
                else if (kind == "recurring")
                {
                    await seed.ServiceProvider.GetRequiredService<IRecurringTriggerScheduleStore>().SaveAsync(new RecurringTriggerSchedule(RecurringTriggerSchedule.BuildId("artifact", "node-" + id),
                        "artifact", "node-" + id, "timer", id, RecurringScheduleKind.Interval, "PT5M", Now, Now));
                    await seed.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(new WorkflowTriggerBinding("binding-" + id,
                        "artifact", "definition", "1.0.0", "hash", "node-" + id, "timer", id, null, new Dictionary<string, string>(), Now));
                }
                else
                    await seed.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(new RuntimeSchedulerWorkItem(
                        "work-" + id, id, "command-" + id, WorkflowExecutionCommandKind.RunSchedulerWork, "envelope-" + id,
                        "key-" + id, Now, Now, i + 1));
            }
        }
        await using var sweep = await factory.CreateAsync(new PersistenceScope(tenant));
        var run = kind switch
        {
            "timer" => new DurableTimerPumpTask(provider.GetRequiredService<IPersistenceScopeRunner>(),
                Options.Create(new DurableTimerPumpOptions { MaxConcurrentDispatches = 2 }), clock, NullLogger<DurableTimerPumpTask>.Instance).ExecuteAsync(CancellationToken.None),
            "recurring" => new RecurringTriggerPumpTask(provider.GetRequiredService<IPersistenceScopeRunner>(),
                Options.Create(new RecurringTriggerPumpOptions { MaxConcurrentDispatches = 2 }), clock, NullLogger<RecurringTriggerPumpTask>.Instance).ExecuteAsync(CancellationToken.None),
            _ => sweep.ServiceProvider.GetRequiredService<RuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest { MaxConcurrentDispatches = 2 }).AsTask()
        };
        try
        {
            await probe.Ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(2, probe.DispatchContexts.Count);
            Assert.Equal(2, probe.DispatchContexts.Distinct().Count());
            if (kind != "recovery")
            {
                probe.ObserveRenewal = true;
                clock.Advance(TimeSpan.FromSeconds(20));
                await probe.Renewal.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Assert.DoesNotContain(probe.RenewalContext, probe.DispatchContexts);
            }
        }
        finally { probe.Release.TrySetResult(); }
        await run;
        Assert.Equal(4, probe.DispatchContexts.Count);
        Assert.Equal(4, probe.DispatchContexts.Distinct().Count());
        Assert.Equal(0, provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().InFlightDispatches);
        await using var verify = await factory.CreateAsync(new PersistenceScope(tenant));
        if (kind == "timer")
            Assert.Empty(await verify.ServiceProvider.GetRequiredService<IDurableTimerStore>().ListDueAsync(Now.AddHours(1), 100));
        else if (kind == "recurring")
            Assert.Empty(await verify.ServiceProvider.GetRequiredService<IRecurringTriggerScheduleStore>().ClaimDueAsync(
                new RecurringTriggerOccurrenceClaimRequest("after", Now, TimeSpan.FromMinutes(1), 100)));
    }

    private static async ValueTask QueryAsync(RuntimeDbContext context, IPersistenceAccessContextAccessor access, CancellationToken ct)
    {
        Assert.StartsWith("pump-concurrency-", access.Current.RequireScope().Value, StringComparison.Ordinal);
        await context.WorkflowExecutionStates.TagWith("pump-concurrency-body").CountAsync(ct);
    }

    private sealed class QueryProbe : DbCommandInterceptor
    {
        public ConcurrentBag<Guid> DispatchContexts { get; } = [];
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Renewal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool ObserveRenewal;
        public Guid RenewalContext;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pump-concurrency-body", StringComparison.Ordinal))
            {
                DispatchContexts.Add(eventData.Context!.ContextId.InstanceId);
                if (DispatchContexts.Count == 2) Ready.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (ObserveRenewal && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                RenewalContext = eventData.Context!.ContextId.InstanceId;
                Renewal.TrySetResult();
            }
            return new(result);
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (ObserveRenewal && command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                RenewalContext = eventData.Context!.ContextId.InstanceId;
                Renewal.TrySetResult();
            }
            return new(result);
        }
    }

    private sealed class QueryDispatcher(RuntimeDbContext context, IPersistenceAccessContextAccessor access) : IBookmarkResumeDispatcher
    {
        public async ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            await QueryAsync(context, access, cancellationToken);
            return new(BookmarkResumeDispatchStatus.Duplicate, request.WorkflowExecutionId!, reason: "duplicate");
        }
    }
    private sealed class QueryRouter(RuntimeDbContext context, IPersistenceAccessContextAccessor access) : IStimulusRouter
    {
        public async ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default)
        {
            await QueryAsync(context, access, cancellationToken);
            return new([], []);
        }
    }
    private sealed class QueryActors(RuntimeDbContext context, IPersistenceAccessContextAccessor access) : IWorkflowExecutionActorProvider
    {
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) => new(new QueryActor(context, access));
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        private sealed class QueryActor(RuntimeDbContext context, IPersistenceAccessContextAccessor access) : IWorkflowExecutionActor
        {
            public WorkflowExecutionActorDescriptor Descriptor => throw new NotSupportedException();
            public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
            {
                await QueryAsync(context, access, cancellationToken);
                return new(envelope.EnvelopeId, envelope.WorkflowExecutionId, WorkflowExecutionCommandDispatchStatus.Accepted, Now);
            }
        }
    }
}
