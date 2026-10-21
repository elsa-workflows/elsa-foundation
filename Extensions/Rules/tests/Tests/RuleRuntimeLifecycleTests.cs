using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Runtime;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed partial class RuleRuntimeCompositionTests
{
    [Fact]
    public async Task Tasks_first_start_runs_after_activation_cancellation_without_root_host_started_signal()
    {
        await using var database = new RuntimeDatabase();
        var identitySource = new TestIdentitySource(Invocation().ExpectedImplementation);

        // Use the real Features and migration path to prepare a clean database, then create the
        // shell-style host whose Tasks background task starts before its root host does.
        await using (var migrationHost = await StartHostAsync(database.DatabasePath, identitySource))
            await migrationHost.StopAsync();

        using var host = BuildHost(database.DatabasePath, identitySource);
        var worker = host.Services.GetRequiredService<RulesRuntimeWorker>();
        var backgroundTask = host.Services.GetRequiredService<IBackgroundTask>();
        Assert.Same(worker, backgroundTask);
        Assert.Same(worker, Assert.Single(host.Services.GetServices<IHostedService>().OfType<RulesRuntimeWorker>()));
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);

        Task? drainedTask = null;
        using var activation = new CancellationTokenSource();
        try
        {
            var start = backgroundTask.StartAsync(activation.Token);
            Assert.Same(start, backgroundTask.StartAsync(CancellationToken.None));
            Assert.Same(start, worker.StartAsync(CancellationToken.None));
            await start.WaitAsync(TimeSpan.FromSeconds(10));
            drainedTask = Assert.IsAssignableFrom<Task>(worker.ExecuteTask);
            await activation.CancelAsync();
            Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
            Assert.False(drainedTask.IsCompleted);

            RuleRunIdentity identity;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var services = scope.ServiceProvider;
                var factory = services.GetRequiredService<IRuleInvocationFactory>();
                var store = services.GetRequiredService<IRuleRunStore>();
                var reader = services.GetRequiredService<IRuleRunReader>();
                Assert.Same(store, reader);

                identity = NewIdentity("tasks-first");
                var prepared = await factory.PrepareAsync(identity, Witness(), Facts("seed"));
                Assert.Equal(RuleInvocationPreparationDisposition.Prepared, prepared.Disposition);
                Assert.Equal(RuleAdmissionDisposition.Admitted,
                    (await store.AdmitAsync(new(prepared.Invocation!))).Disposition);
                var terminal = await WaitForTerminalAsync(reader, identity);
                Assert.Equal(RuleRunStatus.Succeeded, terminal.Status);
                Assert.Equal(4, terminal.CommittedRounds);
            }

            Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
            await backgroundTask.StopAsync(CancellationToken.None);
            Assert.True(drainedTask.IsCompletedSuccessfully);
        }
        finally
        {
            if (drainedTask is null || !drainedTask.IsCompleted)
                await backgroundTask.StopAsync(CancellationToken.None);
        }
    }

}
