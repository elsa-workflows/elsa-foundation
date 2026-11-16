using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Runtime;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Rules.Tests;

// Real host/Tasks lifecycle, Rules feature registration and SQLite acknowledgement. Native stores
// and dispatch remain controlled doubles; persisted native composite acceptance is a separate gate.
public sealed class RulesNativeHostingTests : IAsyncLifetime
{
    private readonly RuleStoreTestFixture database = new();
    private readonly RulesNativeDeliveryTestFixture native = new();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registered_delivery_worker_consumes_durable_terminal_through_host_or_Tasks_lifecycle(bool tasksFirst)
    {
        var dispatcher = new CompletingDispatcher(native);
        using var host = BuildHost(dispatcher);
        var worker = host.Services.GetRequiredService<RulesNativeDeliveryWorker>();
        var backgroundTask = Assert.Single(host.Services.GetServices<IBackgroundTask>().OfType<RulesNativeDeliveryWorker>());
        Assert.Same(worker, backgroundTask);
        Assert.Same(worker, Assert.Single(host.Services.GetServices<IHostedService>().OfType<RulesNativeDeliveryWorker>()));
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);

        await SeedTerminalAsync(host.Services);
        AssertRegistrations(host.Services);
        await using (var probe = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new(RulesNativeDeliveryTestFixture.Partition)))
        {
            var found = await probe.ServiceProvider.GetRequiredService<RulesNativeDeliveryInspector>()
                .InspectAsync(native.Invocation.Identity, new(RuleRecordCodec.Hash(RuleRecordCodec.InvocationBytes(native.Invocation))),
                    native.Envelope, CancellationToken.None);
            Assert.NotNull(found.Bookmark);
        }
        Task? running = null;
        using var activation = new CancellationTokenSource();
        try
        {
            if (tasksFirst)
                await ((IBackgroundTask)backgroundTask).StartAsync(activation.Token);
            else
                await host.StartAsync(activation.Token);
            running = Assert.IsAssignableFrom<Task>(worker.ExecuteTask);
            var repeated = ((IBackgroundTask)backgroundTask).StartAsync(CancellationToken.None);
            Assert.Same(repeated, worker.StartAsync(CancellationToken.None));
            await repeated;
            Assert.Same(running, ((ITask)backgroundTask).ExecuteAsync(CancellationToken.None));
            await activation.CancelAsync();

            await WaitForAcknowledgementAsync();

            Assert.Equal(1, dispatcher.Calls);
            Assert.Equal(!tasksFirst, lifetime.ApplicationStarted.IsCancellationRequested);
            Assert.False(running.IsCompleted);
            await using var scope = host.Services.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IRuleRunStore>();
            Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
                (await store.ClaimDeliveryAsync(new("post-host-check", [1]))).Disposition);
        }
        finally
        {
            await ((IBackgroundTask)backgroundTask).StopAsync(CancellationToken.None);
            await host.StopAsync();
        }
        Assert.NotNull(running);
        Assert.True(running.IsCompletedSuccessfully);
    }

    private IHost BuildHost(IBookmarkResumeDispatcher dispatcher)
    {
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddPersistenceCore(RulesNativeDeliveryTestFixture.Partition);
        services.AddSingleton<IWorkflowExecutionStateStore>(native.Workflows);
        services.AddSingleton<IActivityExecutionStateStore>(native.Activities);
        services.AddSingleton<IBookmarkStateStore>(native.Bookmarks);
        services.AddSingleton<IExternalPayloadStore>(native.ExternalPayloads);
        services.AddSingleton(dispatcher);
        // This fixture starts with an already refused invocation. It makes no package provenance claim.
        services.AddSingleton<IRuleImplementationIdentitySource, UnavailableIdentitySource>();
        services.AddSingleton<IRuleAdmissionValidator>(new RuleStoreTestFixture.FixedAdmissionValidator(
            RuleAdmissionValidationDisposition.Refused, new("rules.test-refusal")));
        new RulesRuntimeFeature { MaxConcurrentAttempts = 1, PollIntervalMilliseconds = 10 }.ConfigureServices(services);
        new RulesWorkflowsRuntimeFeature { MaxConcurrentDeliveries = 2, PollIntervalMilliseconds = 10 }.ConfigureServices(services);
        new RulesRuntimeEntityFrameworkCoreFeature
        {
            ConnectionString = $"Data Source={database.DatabasePath};Pooling=False"
        }.ConfigureServices(services);
        services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
        return builder.Build();
    }

    private async Task SeedTerminalAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRuleRunStore>();
        Assert.Equal(RuleRefusalDisposition.Refused,
            (await store.RefuseAdmissionAsync(new(native.Invocation, new("rules.test-refusal")))).Disposition);
        await using var db = database.Contexts.CreateDbContext();
        var row = await db.Runs.AsNoTracking().SingleAsync();
        Assert.False(row.DeliveryAcknowledged);
        Assert.Null(row.DeliveryOwner);
        native.SetEnvelope(RuleRecordCodec.ReadDeliveryRecord(Encoding.UTF8.GetBytes(row.DeliveryPayloadJson!)));
    }

    private static void AssertRegistrations(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var scoped = scope.ServiceProvider;
        Assert.IsType<RulesNativeDeliveryProofValidator>(scoped.GetRequiredService<IRuleDeliveryProofValidator>());
        Assert.NotNull(scoped.GetRequiredService<RulesNativeDeliveryExecution>());
        Assert.NotNull(scoped.GetRequiredService<RulesIntentHandler>());
        Assert.Single(scoped.GetServices<IRuntimeCheckpointCommitEnricher>().OfType<RulesCheckpointEnricher>());
        var handlers = scoped.GetServices<RuntimePostCommitIntentHandlerContribution>().ToArray();
        Assert.Equal(new[] { RulesNativeBoundary.CancelKind, RulesNativeBoundary.StartKind }.Order(),
            handlers.Select(handler => handler.IntentKind).Order());
        Assert.All(handlers, handler =>
        {
            Assert.Equal(typeof(RulesIntentHandler), handler.HandlerType);
            Assert.True(handler.RetryPolicy.RetryUntilAcknowledged);
            Assert.Equal(TimeSpan.FromSeconds(1), handler.RetryPolicy.Delay);
        });
    }

    private async Task WaitForAcknowledgementAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await using var db = database.Contexts.CreateDbContext();
            var row = await db.Runs.AsNoTracking().SingleAsync(timeout.Token);
            if (row.DeliveryAcknowledged)
            {
                Assert.NotNull(row.DeliveryAcknowledgementJson);
                Assert.NotNull(row.DeliveryPayloadJson);
                return;
            }
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class UnavailableIdentitySource : IRuleImplementationIdentitySource
    {
        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<RuleSemanticPin?>(null);
        }
    }

    private sealed class CompletingDispatcher(RulesNativeDeliveryTestFixture native) : IBookmarkResumeDispatcher
    {
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref calls);
            native.SetCompleted(retirePrivateState: true, retireReceiptPayload: true);
            return ValueTask.FromResult(new BookmarkResumeDispatchResult(BookmarkResumeDispatchStatus.Duplicate,
                request.WorkflowExecutionId, reason: "Controlled dispatch; native state supplies consumption proof."));
        }
    }
}
