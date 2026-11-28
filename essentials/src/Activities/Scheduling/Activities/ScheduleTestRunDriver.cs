using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;

namespace Elsa.Activities.Scheduling.Activities;

internal abstract class ScheduleTestRunDriver(
    IRecurringScheduleCalculator calculator,
    TimeProvider timeProvider,
    string driverId,
    RecurringScheduleKind kind,
    string inputName) : ITestRunTriggerDriver
{
    public string DriverId => driverId;

    public bool Supports(TestRunTriggerContext context) =>
        context.ProviderId == DriverId && context.Node.ActivityType == DriverId;

    public IReadOnlyCollection<TestRunTriggerMode> Describe(TestRunTriggerContext context)
    {
        var expression = SchedulingNodeInputs.ReadLiteralString(context.Node, inputName)
            ?? throw new ArgumentException("A test schedule requires its authored literal.");
        var calculationExpression = PrepareExpression(expression);
        var next = timeProvider.GetUtcNow();
        var occurrences = new List<DateTimeOffset>();
        for (var index = 0; index < 3; index++)
        {
            if (calculator.ComputeNext(kind, calculationExpression, next) is not { } occurrence)
                break;
            occurrences.Add(occurrence);
            next = occurrence;
        }
        return [new("fire-now", "Fire once now", "Start one test execution now; the authored schedule stays unchanged.", true,
            configurationSchema: JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false }),
            configuration: JsonSerializer.SerializeToElement(new
            {
                kind = kind == RecurringScheduleKind.Interval ? "timer" : "cron", expression, timezone = "UTC", nextOccurrences = occurrences
            }))];
    }

    public TestRunTriggerValidation Validate(TestRunTriggerContext context, string modeId, JsonElement? data)
    {
        if (!Supports(context) || modeId != "fire-now")
            return Refuse("unsupported-schedule-mode", "Only Fire once now is supported by this scheduling driver.");
        if (data is { } value && (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any()))
            return Refuse("invalid-schedule-configuration", "Fire once now does not accept schedule overrides.");
        try { _ = Describe(context); }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            return Refuse("invalid-test-schedule", "The authored schedule cannot be calculated. Check its interval or Cron expression.");
        }
        return TestRunTriggerValidation.Success();
    }

    public ValueTask<TestRunTriggerPreparation> PrepareAsync(TestRunTriggerPrepareContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Validate(context.Trigger, context.ModeId, context.Data).IsValid)
            throw new ArgumentException("Invalid scheduling Test Drive configuration.");
        // The existing exact start dispatcher owns this one occurrence. No recurring schedule is created.
        return ValueTask.FromResult(TestRunTriggerPreparation.Immediate());
    }

    private static TestRunTriggerValidation Refuse(string code, string message) =>
        TestRunTriggerValidation.Failure([new(code, message)]);

    protected virtual string PrepareExpression(string expression) => expression;
}

internal sealed class TimerTestRunDriver(IRecurringScheduleCalculator calculator, TimeProvider timeProvider,
    KernelTimerTestRunIntervalPolicy? kernelIntervals = null)
    : ScheduleTestRunDriver(calculator, timeProvider, Timer.ActivityType, RecurringScheduleKind.Interval, nameof(Timer.Interval))
{
    protected override string PrepareExpression(string expression) => kernelIntervals?.Prepare(expression) ?? expression;
}

internal sealed class CronTestRunDriver(IRecurringScheduleCalculator calculator, TimeProvider timeProvider)
    : ScheduleTestRunDriver(calculator, timeProvider, Cron.ActivityType, RecurringScheduleKind.Cron, nameof(Cron.Expression));

internal sealed class KernelTimerTestRunIntervalPolicy
{
    public string Prepare(string expression)
    {
        // The Native Timer trims its authored literal and requires at least one second.
        var normalized = expression.Trim();
        if (RecurringScheduleInterval.ParsePositive(normalized) < TimeSpan.FromSeconds(1))
            throw new FormatException("An Actor Foundation Timer interval must be at least one second.");
        return normalized;
    }
}
