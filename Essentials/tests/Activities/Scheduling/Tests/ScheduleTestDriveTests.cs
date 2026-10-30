using System.Text.Json;
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
