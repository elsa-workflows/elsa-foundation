using System.Text.Json;
using CShells;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

public sealed class ScheduleTestDriveTests
{
    [Theory]
    [InlineData("Elsa.Timer", "Interval", "PT5M", "2026-10-16T12:05:00+00:00")]
    [InlineData("Elsa.Cron", "Expression", "0 9 * * *", "2026-10-17T09:00:00+00:00")]
    public async Task ModuleDriver_UsesProductionCalculator_AndFiresOnlyOneImmediateOccurrence(string activity, string input, string expression, string next)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestRunTriggerFixture.Now));
        services.AddSingleton<IRecurringScheduleCalculator, RecurringScheduleCalculator>();
        new ActivitiesSchedulingFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        await AssertImmediateOccurrenceAsync(provider, activity, input, expression, next);
    }

    [Theory]
    [InlineData("Elsa.Timer", "Interval", "PT5M", "2026-10-16T12:05:00+00:00")]
    [InlineData("Elsa.Cron", "Expression", "0 9 * * *", "2026-10-17T09:00:00+00:00")]
    public async Task Kernel_feature_drives_authored_schedules_without_legacy_schedulers(string activity, string input, string expression, string next)
    {
        const string shellName = "kernel-scheduling";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestRunTriggerFixture.Now));
        services.AddCShells(shells => shells
            .WithAssemblyContaining<KernelActivitiesSchedulingFeature>()
            .WithAssemblyContaining<WorkflowsRuntimeScheduleCalculationFeature>()
            .AddShell(shellName, shell => shell.WithFeature<KernelActivitiesSchedulingFeature>()));
        await using var host = services.BuildServiceProvider(validateScopes: true);
        var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(shellName);
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var settings = provider.GetRequiredService<ShellSettings>();
        Assert.Equal(new[] { "KernelActivitiesScheduling", "WorkflowsRuntimeScheduleCalculation" },
            settings.EnabledFeatures.Order(StringComparer.Ordinal));
        Assert.IsType<RecurringScheduleCalculator>(provider.GetRequiredService<IRecurringScheduleCalculator>());
        Assert.Null(provider.GetService<IDurableTimerScheduler>());
        Assert.Null(provider.GetService<IRecurringTriggerScheduleStore>());
        Assert.Null(provider.GetService<IWorkflowSchedulerDrainer>());
        Assert.Null(provider.GetService<IStimulusRouter>());
        Assert.Empty(provider.GetServices<IRecurringTriggerScheduleProvider>());
        Assert.Single(provider.GetServices<IActivityTriggerStimulusProvider>(),
            stimulus => stimulus.Describe(TestRunTriggerFixture.Create(activity, input, expression).Node).IsRecognized);
        await AssertImmediateOccurrenceAsync(provider, activity, input, expression, next);
    }

    private static async Task AssertImmediateOccurrenceAsync(
        IServiceProvider provider, string activity, string input, string expression, string next)
    {
        var trigger = TestRunTriggerFixture.Create(activity, input, expression);
        var driver = Assert.Single(provider.GetServices<ITestRunTriggerDriver>(), candidate => candidate.Supports(trigger));
        var mode = Assert.Single(driver.Describe(trigger));
        Assert.Equal("UTC", mode.Configuration!.Value.GetProperty("timezone").GetString());
        Assert.Equal(DateTimeOffset.Parse(next), mode.Configuration.Value.GetProperty("nextOccurrences")[0].GetDateTimeOffset());
        Assert.Equal(expression, mode.Configuration.Value.GetProperty("expression").GetString());
        Assert.True(driver.Validate(trigger, "fire-now", null).IsValid);
        Assert.False(driver.Validate(trigger, "wait-next", null).IsValid);
        Assert.False(driver.Validate(trigger, "fire-now", JsonSerializer.SerializeToElement(new { expression = "changed" })).IsValid);
        var preparation = await driver.PrepareAsync(TestRunTriggerFixture.Prepare(trigger, "fire-now"));
        Assert.Equal(TestRunPreparationKind.Immediate, preparation.Kind);
        Assert.Null(preparation.StimulusInput);
        Assert.Equal(expression, trigger.Node.InputBindings[input].LiteralValue!.Value.GetString());
    }
}
