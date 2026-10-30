using CShells.Features;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

public sealed class WorkflowsRuntimeSchedulingFeatureTests
{
    [Fact]
    public void RegistersStoreSchedulerAndPumpTask()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);

        // TS-1: single-implementation services asserted by registration presence, not implementation-type
        // or lifetime pinning (resolvability is proven by the composition test). The pump task is a named
        // participant in the multi-implementation IRecurringTask collection, preserved as a composition contract.
        Assert.Contains(services, d => d.ServiceType == typeof(IDurableTimerStore));

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IDurableTimerScheduler) &&
            d.Lifetime == ServiceLifetime.Scoped);

        Assert.Contains(services, d =>
            d.ServiceType == typeof(IRecurringTask) &&
            d.ImplementationFactory is not null &&
            d.Lifetime == ServiceLifetime.Singleton);

        Assert.Contains(services, d => d.ServiceType == typeof(TimeProvider));
    }

    [Fact]
    public void MapsSettingsOntoOptions()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeSchedulingFeature
        {
            SweepIntervalSeconds = 3,
            MaxBackoffIntervalMinutes = 2,
            MaxTimersPerTick = 42,
            NotFoundGraceSeconds = 15
        }.ConfigureServices(services);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<DurableTimerPumpOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(3), options.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), options.MaxBackoffInterval);
        Assert.Equal(42, options.MaxTimersPerTick);
        Assert.Equal(TimeSpan.FromSeconds(15), options.NotFoundGrace);
    }

    [Fact]
    public async Task Feature_scheduler_and_hosted_pump_share_the_deadline_signal()
    {
        var services = new ServiceCollection();
        var events = new CapturingPublisher();
        services.AddSingleton<IDeferredEventPublisher>(events);
        new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IDurableTimerScheduler>();
        var wakeup = provider.GetRequiredService<DurableTimerWakeup>();
        var due = DateTimeOffset.UtcNow.AddSeconds(5);
        await scheduler.ScheduleAsync(new("timer", "workflow", "Delay", "timer", due, due.AddSeconds(-5)));
        var notification = Assert.IsType<DurableTimerScheduled>(events.Last);
        Assert.Equal(due, notification.DueTime);
        Assert.Null(wakeup.Snapshot.Deadline);
        await Assert.Single(scope.ServiceProvider.GetServices<IEventHandler<DurableTimerScheduled>>())
            .Handle(notification, CancellationToken.None);
        Assert.Equal(due, wakeup.Snapshot.Deadline);
    }

    private sealed class CapturingPublisher : IDeferredEventPublisher
    {
        public IEvent? Last { get; private set; }
        public Task Publish(IEvent notification, CancellationToken cancellationToken = default)
        {
            Last = notification;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void DeclaresTasksDependencyAndServerRuntime()
    {
        var attribute = Assert.Single(
            typeof(WorkflowsRuntimeSchedulingFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeScheduling", attribute.Name);
        Assert.Contains("Tasks", attribute.DependsOn.Select(d => d?.ToString()));
    }
}
