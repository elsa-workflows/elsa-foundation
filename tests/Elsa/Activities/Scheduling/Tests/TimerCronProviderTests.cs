using System.Text.Json;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
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
    public void TriggerProviderIds_AreExplicitAndStable()
    {
        Assert.Equal("Elsa.Timer", ((IActivityTriggerStimulusProvider)_timerTrigger).ProviderId);
        Assert.Equal("Elsa.Cron", ((IActivityTriggerStimulusProvider)_cronTrigger).ProviderId);
    }

    [Fact]
    public void Timer_TriggerAndSchedule_ShareStimulusIdentity()
    {
        var node = Node(Timer.ActivityType, nameof(Timer.Interval), "PT5M");

        var trigger = Assert.Single(_timerTrigger.Describe(node).Descriptors);
        var schedule = _timerSchedule.Describe(node);

        Assert.NotNull(schedule);
        Assert.Equal("Timer", trigger.StimulusType);
        Assert.Equal(TimerStimulus.Hash("PT5M"), trigger.StimulusHash);
        Assert.Equal(trigger.StimulusHash, schedule!.StimulusHash);
        Assert.Equal(RecurringScheduleKind.Interval, schedule.Kind);
        Assert.Equal("PT5M", schedule.Expression);
    }

    [Fact]
    public void Cron_TriggerAndSchedule_ShareStimulusIdentity()
    {
        var node = Node(Cron.ActivityType, nameof(Cron.Expression), "0 * * * *");

        var trigger = Assert.Single(_cronTrigger.Describe(node).Descriptors);
        var schedule = _cronSchedule.Describe(node);

        Assert.Equal("Cron", trigger.StimulusType);
        Assert.Equal(CronStimulus.Hash("0 * * * *"), trigger.StimulusHash);
        Assert.Equal(trigger.StimulusHash, schedule!.StimulusHash);
        Assert.Equal(RecurringScheduleKind.Cron, schedule.Kind);
        Assert.Equal("0 * * * *", schedule.Expression);
    }

    [Fact]
    public void Providers_ReturnEmpty_ForForeignActivityType()
    {
        var node = Node("Elsa.WriteLine", nameof(Timer.Interval), "PT5M");

        Assert.False(_timerTrigger.Describe(node).IsRecognized);
        Assert.Null(_timerSchedule.Describe(node));
        Assert.False(_cronTrigger.Describe(node).IsRecognized);
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

    [Theory]
    [InlineData(Timer.ActivityType, nameof(Timer.Interval), "   ")]
    [InlineData(Cron.ActivityType, nameof(Cron.Expression), "   ")]
    public void TriggerAndScheduleProviders_Throw_WhenLiteralIsBlank(string activityType, string inputName, string literal)
    {
        var node = Node(activityType, inputName, literal);

        if (activityType == Timer.ActivityType)
        {
            Assert.Throws<ArgumentException>(() => _timerTrigger.Describe(node));
            Assert.Throws<ArgumentException>(() => _timerSchedule.Describe(node));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => _cronTrigger.Describe(node));
            Assert.Throws<ArgumentException>(() => _cronSchedule.Describe(node));
        }
    }

    [Theory]
    [InlineData(Timer.ActivityType, nameof(Timer.Interval))]
    [InlineData(Cron.ActivityType, nameof(Cron.Expression))]
    public void TriggerAndScheduleProviders_Throw_WhenInputIsNonLiteral(string activityType, string inputName)
    {
        var node = Node(activityType, inputName, literal: null, inputBinding: ExpressionBinding(inputName));

        if (activityType == Timer.ActivityType)
        {
            Assert.Throws<ArgumentException>(() => _timerTrigger.Describe(node));
            Assert.Throws<ArgumentException>(() => _timerSchedule.Describe(node));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => _cronTrigger.Describe(node));
            Assert.Throws<ArgumentException>(() => _cronSchedule.Describe(node));
        }
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

    private static ExecutableNode Node(string activityType, string inputName, string? literal, RuntimeInputBinding? inputBinding = null)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        if (inputBinding is not null)
            bindings[inputName] = inputBinding;
        else if (literal is not null)
        {
            using var value = JsonDocument.Parse(JsonSerializer.Serialize(literal));
            bindings[inputName] = LiteralBinding(inputName, value.RootElement);
        }

        return new ExecutableNode(
            executableNodeId: "node-1",
            authoredActivityId: "authored-node-1",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: bindings,
            metadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding ExpressionBinding(string name) =>
        new(
            name,
            new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "input.value"));

    private static RuntimeInputBinding LiteralBinding(string name, JsonElement value)
    {
        var type = new ValueTypeDescriptor("String");
        return new RuntimeInputBinding(
            name,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, value, ValueProtectionPolicy.InstanceInline));
    }
}
