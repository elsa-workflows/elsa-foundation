using System.Text.Json;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using Timer = Elsa.Activities.Scheduling.Activities.Timer;

namespace Elsa.Activities.Scheduling.Tests;

/// <summary>
/// Unit coverage for the Timer/Cron start-trigger providers (W16): each activity contributes both an
/// <c>IActivityTriggerStimulusProvider</c> (trigger index identity) and an
/// <c>IRecurringTriggerScheduleProvider</c> (recurrence spec), derived from an authored literal so the schedule
/// is fixed at publish time.
/// </summary>
public sealed class TimerCronProviderTests
{
    private readonly TimerTriggerStimulusProvider _timerTrigger = new();
    private readonly TimerRecurringScheduleProvider _timerSchedule = new();
    private readonly CronTriggerStimulusProvider _cronTrigger = new();
    private readonly CronRecurringScheduleProvider _cronSchedule = new();

    [Fact]
    public void Timer_TriggerAndSchedule_ShareStimulusIdentity()
    {
        var node = Node(Timer.ActivityType, nameof(Timer.Interval), "PT5M");

        var trigger = _timerTrigger.Describe(node);
        var schedule = _timerSchedule.Describe(node);

        Assert.NotNull(trigger);
        Assert.NotNull(schedule);
        Assert.Equal("Timer", trigger!.StimulusType);
        Assert.Equal(TimerStimulus.Hash("PT5M"), trigger.StimulusHash);
        Assert.Equal(trigger.StimulusHash, schedule!.StimulusHash);
        Assert.Equal(RecurringScheduleKind.Interval, schedule.Kind);
        Assert.Equal("PT5M", schedule.Expression);
    }

    [Fact]
    public void Cron_TriggerAndSchedule_ShareStimulusIdentity()
    {
        var node = Node(Cron.ActivityType, nameof(Cron.Expression), "0 * * * *");

        var trigger = _cronTrigger.Describe(node);
        var schedule = _cronSchedule.Describe(node);

        Assert.Equal("Cron", trigger!.StimulusType);
        Assert.Equal(CronStimulus.Hash("0 * * * *"), trigger.StimulusHash);
        Assert.Equal(trigger.StimulusHash, schedule!.StimulusHash);
        Assert.Equal(RecurringScheduleKind.Cron, schedule.Kind);
        Assert.Equal("0 * * * *", schedule.Expression);
    }

    [Fact]
    public void Providers_ReturnNull_ForForeignActivityType()
    {
        var node = Node("Elsa.WriteLine", nameof(Timer.Interval), "PT5M");

        Assert.Null(_timerTrigger.Describe(node));
        Assert.Null(_timerSchedule.Describe(node));
        Assert.Null(_cronTrigger.Describe(node));
        Assert.Null(_cronSchedule.Describe(node));
    }

    [Fact]
    public void Providers_Throw_WhenLiteralMissing()
    {
        var timerNode = Node(Timer.ActivityType, nameof(Timer.Interval), literal: null);
        var cronNode = Node(Cron.ActivityType, nameof(Cron.Expression), literal: null);

        Assert.Throws<ArgumentException>(() => _timerTrigger.Describe(timerNode));
        Assert.Throws<ArgumentException>(() => _timerSchedule.Describe(timerNode));
        Assert.Throws<ArgumentException>(() => _cronTrigger.Describe(cronNode));
        Assert.Throws<ArgumentException>(() => _cronSchedule.Describe(cronNode));
    }

    [Fact]
    public void TimerHash_IsDeterministic_TrimInsensitive_AndPrefixed()
    {
        Assert.Equal(TimerStimulus.Hash("PT5M"), TimerStimulus.Hash("  PT5M  "));
        Assert.NotEqual(TimerStimulus.Hash("PT5M"), TimerStimulus.Hash("PT6M"));
        Assert.StartsWith("sha256:", TimerStimulus.Hash("PT5M"));
    }

    [Fact]
    public void CronHash_CollapsesInternalWhitespace()
    {
        Assert.Equal(CronStimulus.Hash("0 * * * *"), CronStimulus.Hash("0   *  * * *"));
        Assert.NotEqual(CronStimulus.Hash("0 * * * *"), CronStimulus.Hash("5 * * * *"));
        Assert.StartsWith("sha256:", CronStimulus.Hash("0 * * * *"));
    }

    private static ExecutableNode Node(string activityType, string inputName, string? literal)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        if (literal is not null)
        {
            using var value = JsonDocument.Parse(JsonSerializer.Serialize(literal));
            bindings[inputName] = new RuntimeInputBinding(inputName, RuntimeInputBindingSource.Literal, literalValue: value.RootElement.Clone());
        }

        return new ExecutableNode(
            executableNodeId: "node-1",
            authoredActivityId: "authored-node-1",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: bindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }
}
