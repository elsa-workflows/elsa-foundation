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
    [InlineData("Elsa.Timer", "Interval", " PT5M ", "2026-10-16T12:05:00+00:00")]
    [InlineData("Elsa.Timer", "Interval", "PT1S", "2026-10-16T12:00:01+00:00")]
    [InlineData("Elsa.Cron", "Expression", "0 9 * * *", "2026-10-17T09:00:00+00:00")]
    public async Task Kernel_feature_drives_authored_schedules_without_legacy_schedulers(string activity, string input, string expression, string next)
    {
        await using var host = CreateKernelHost();
        var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(KernelShellName);
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

    [Fact]
    public async Task Kernel_timer_checks_its_interval_before_using_the_selected_calculator()
    {
        var calculator = new RecordingCalculator();
        await using var host = CreateKernelHost(calculator);
        var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(KernelShellName);
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var invalid = TestRunTriggerFixture.Create("Elsa.Timer", "Interval", "PT0.5S");
        var driver = Assert.Single(provider.GetServices<ITestRunTriggerDriver>(), candidate => candidate.Supports(invalid));
        Assert.Same(calculator, provider.GetRequiredService<IRecurringScheduleCalculator>());
        Assert.Throws<FormatException>(() => driver.Describe(invalid));
        Assert.False(driver.Validate(invalid, "fire-now", null).IsValid);
        await Assert.ThrowsAsync<ArgumentException>(() => driver.PrepareAsync(TestRunTriggerFixture.Prepare(invalid, "fire-now")).AsTask());
        Assert.Empty(calculator.Expressions);

        await AssertImmediateOccurrenceAsync(provider, "Elsa.Timer", "Interval", " PT5M ", "2026-10-16T12:00:07+00:00");
        Assert.NotEmpty(calculator.Expressions);
        Assert.All(calculator.Expressions, expression => Assert.Equal("PT5M", expression));
    }

    private const string KernelShellName = "kernel-scheduling";

    private static ServiceProvider CreateKernelHost(IRecurringScheduleCalculator? calculator = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestRunTriggerFixture.Now));
        if (calculator is not null)
            services.AddSingleton(calculator);
        services.AddCShells(shells => shells
            .WithAssemblyContaining<KernelActivitiesSchedulingFeature>()
            .WithAssemblyContaining<WorkflowsRuntimeScheduleCalculationFeature>()
            .AddShell(KernelShellName, shell => shell.WithFeature<KernelActivitiesSchedulingFeature>()));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class RecordingCalculator : IRecurringScheduleCalculator
    {
        public List<string> Expressions { get; } = [];

        public DateTimeOffset? ComputeNext(RecurringScheduleKind kind, string expression, DateTimeOffset after)
        {
            Expressions.Add(expression);
            return after.AddSeconds(7);
        }
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
