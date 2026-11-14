using System.Globalization;
using System.Text.Json;
using CShells.Features;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Scheduling.StandaloneTriggers;

/// <summary>Resource-owned Timer/Cron intake through the ordinary recurring schedule store and pump.</summary>
[ShellFeature(name: "StandaloneScheduleTriggers", DisplayName = "Standalone schedule triggers",
    Description = "Run independent Timer and Cron resources through the shared recurring schedule pump.",
    DependsOn = new object[] { "WorkflowsRuntimeRecurringTriggers" })]
public class StandaloneScheduleTriggersFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddStandaloneTriggerIngress<TimerScheduleSource>(new("timer", 1, ScheduleSourceContext.TimerFamily, ScheduleSourceContext.Schema));
        services.AddStandaloneTriggerIngress<CronScheduleSource>(new("cron", 1, ScheduleSourceContext.CronFamily, ScheduleSourceContext.Schema));
        services.TryAddScoped<ScheduleSourceContext>();
        services.TryAddScoped<IStandaloneTriggerSourceContext<TimerScheduleSource>>(provider => provider.GetRequiredService<ScheduleSourceContext>());
        services.TryAddScoped<IStandaloneTriggerSourceContext<CronScheduleSource>>(provider => provider.GetRequiredService<ScheduleSourceContext>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStandaloneSchedulePlanner, ScheduleResourcePlanner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStandaloneScheduleSource, ScheduleResourceSource>());
    }
}

internal sealed class TimerScheduleSource;
internal sealed class CronScheduleSource;

internal sealed class ScheduleResourcePlanner(IRecurringScheduleCalculator calculator) : IStandaloneSchedulePlanner
{
    public bool Supports(string typeKey) => typeKey is "timer" or "cron";
    public StandaloneSchedulePlan Plan(TriggerScope scope, TriggerAggregate? previous, TriggerStoreWrite write,
        RecurringTriggerSchedule? current, DateTimeOffset now)
    {
        try { return PlanCore(scope, previous, write, current, now); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or JsonException or
            FormatException or OverflowException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new TriggerOperationException("validation", "The resource recurrence or timezone is invalid or unavailable on this host.",
                new Dictionary<string, string> { ["scope"] = scope.Persistence.Value, ["triggerId"] = write.Trigger.Id }, exception);
        }
    }
    private StandaloneSchedulePlan PlanCore(TriggerScope scope, TriggerAggregate? previous, TriggerStoreWrite write,
        RecurringTriggerSchedule? current, DateTimeOffset now)
    {
        var trigger = write.Trigger;
        if (!Supports(trigger.TypeKey) || trigger.TenantId != scope.TenantId || previous is not null && previous.Trigger.Id != trigger.Id)
            throw new TriggerOperationException("validation", "The schedule planner does not own this exact resource mutation.");
        if (trigger.DesiredState == TriggerDesiredState.Deleted) return new(StandaloneScheduleAction.Remove);
        if (trigger.DesiredState != TriggerDesiredState.Enabled) return new(StandaloneScheduleAction.Deactivate);
        if (previous?.Trigger.DesiredState == TriggerDesiredState.Enabled && previous.Trigger.ConfigurationRevision == trigger.ConfigurationRevision && current is { IsActive: true })
            return new(StandaloneScheduleAction.Preserve);
        var kind = trigger.TypeKey == "timer" ? RecurringScheduleKind.Interval : RecurringScheduleKind.Cron;
        var expression = trigger.Configuration.GetProperty(kind == RecurringScheduleKind.Interval ? "interval" : "expression").GetString()
            ?? throw new TriggerOperationException("validation", "The resource recurrence is missing.");
        var timezone = kind == RecurringScheduleKind.Interval || !trigger.Configuration.TryGetProperty("timezone", out var zone) ? "UTC" : zone.GetString()
            ?? throw new TriggerOperationException("validation", "The Cron timezone is missing.");
        var next = calculator.ComputeNext(kind, expression, now, timezone)
            ?? throw new TriggerOperationException("validation", "This recurrence has no future firing.");
        var owner = new RecurringTriggerOwner(trigger.Id, trigger.Revision, trigger.ConfigurationRevision, trigger.TypeKey,
            kind == RecurringScheduleKind.Interval ? ScheduleSourceContext.TimerFamily : ScheduleSourceContext.CronFamily, scope.Persistence, scope.TenantId, timezone);
        return new(StandaloneScheduleAction.Replace, RecurringTriggerSchedule.ForResource(owner, kind, expression, next, now));
    }
}

internal sealed class ScheduleSourceContext : IStandaloneTriggerSourceContext<TimerScheduleSource>, IStandaloneTriggerSourceContext<CronScheduleSource>
{
    public bool SupportsResourceAuthorization => true;
    internal const string TimerFamily = "Elsa.StandaloneTimer";
    internal const string CronFamily = "Elsa.StandaloneCron";
    internal static readonly ValueTypeDescriptor Schema = new("Elsa.ScheduleOccurrence", schemaVersion: 1);
    internal StandaloneTriggerInvocationContext? Current { get; set; }
    public ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Current ?? throw new TriggerOperationException("unavailable", "Schedule source context requires the actual recurring pump occurrence.",
            innerException: new InvalidOperationException("No recurring occurrence is bound to this source scope.")));
    }
    public ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claim = Current?.ScheduleClaim;
        var owner = claim?.Schedule.Resource;
        return ValueTask.FromResult(claim is not null && owner is not null &&
            // Renewal changes the physical deadline. The protected admission writer fences the actual current claim;
            // the immutable snapshot supplied by the pump is not an independent expiry authority.

            Current!.Scope.Persistence == owner.Scope && Current.Scope.TenantId == owner.TenantId && trigger.TenantId == owner.TenantId &&
            trigger.Id == owner.TriggerId && signal.TriggerId == trigger.Id && trigger.TypeKey == owner.SourceTypeKey &&
            signal.SourceKey == claim.Schedule.BuildOccurrenceKey() &&
            (owner.SourceTypeKey == "timer" && owner.SourceFamily == TimerFamily || owner.SourceTypeKey == "cron" && owner.SourceFamily == CronFamily));
    }
}

internal sealed class ScheduleResourceSource(ScheduleSourceContext context, IStandaloneTriggerIngress<TimerScheduleSource> timers,
    IStandaloneTriggerIngress<CronScheduleSource> cron, IPersistenceAccessContextAccessor access, IRuntimeIngressDeliveryStore delivery) : IStandaloneScheduleSource
{
    public bool Supports(string sourceTypeKey, string sourceFamily) => sourceTypeKey == "timer" && sourceFamily == ScheduleSourceContext.TimerFamily ||
        sourceTypeKey == "cron" && sourceFamily == ScheduleSourceContext.CronFamily;
    public async ValueTask<RuntimeIngressAdmissionReceipt> AdmitAsync(PersistenceScope scope, RecurringTriggerOccurrenceClaim claim,
        CancellationToken cancellationToken = default)
    {
        var schedule = claim.Schedule;
        var owner = schedule.Resource ?? throw Failure(scope, schedule.ScheduleId, new InvalidDataException("The schedule is not a resource-owned occurrence."));
        if (owner.Scope != scope || !schedule.IsActive || !Supports(owner.SourceTypeKey, owner.SourceFamily))
            throw Failure(scope, owner.TriggerId, new InvalidDataException("The schedule differs from its installed producer and trusted persistence scope."));
        try { access.Current.EnsureScope(scope); }
        catch (InvalidOperationException exception) { throw Failure(scope, owner.TriggerId, exception); }
        var previous = context.Current;
        context.Current = new(new(scope, owner.TenantId), new(scope.Value),
            new("runtime.recurring-trigger", "standalone-trigger:" + owner.TriggerId, new Dictionary<string, string>
            { ["generation"] = owner.Generation.ToString(CultureInfo.InvariantCulture) })) { ScheduleClaim = claim };
        try
        {
            var signal = new StandaloneTriggerSignal(owner.TriggerId, schedule.BuildOccurrenceKey(), ScheduleSourceContext.Schema,
                JsonSerializer.SerializeToElement(new { dueAt = schedule.NextOccurrence, generation = owner.Generation }),
                JsonSerializer.SerializeToElement(new { dueAt = schedule.NextOccurrence, timezone = owner.TimeZoneId, generation = owner.Generation }));
            var result = owner.SourceTypeKey == "timer" ? await timers.AdmitAsync(signal, cancellationToken) : await cron.AdmitAsync(signal, cancellationToken);
            var sourceId = RuntimeStandaloneIngressSource.BuildOutboxItemId(scope, owner.TenantId, owner.TriggerId, owner.SourceFamily, signal.SourceKey);
            var item = await delivery.FindDeliveryAsync(new(scope, owner.TenantId), sourceId, cancellationToken) ??
                throw Failure(scope, owner.TriggerId, new InvalidDataException("The actual accepted schedule source is unavailable."));
            if (item.StandaloneIngress?.AdmissionId != result.AdmissionId || item.StandaloneIngress.TriggerId != owner.TriggerId ||
                item.StandaloneIngress.SourceFamily != owner.SourceFamily || item.StandaloneIngress.TenantId != owner.TenantId)
                throw Failure(scope, owner.TriggerId, new InvalidDataException("The accepted schedule acknowledgement differs from its actual authorized occurrence source."));
            return new(item, result.Replayed);
        }
        finally { context.Current = previous; }
    }
    private static TriggerOperationException Failure(PersistenceScope scope, string resourceId, Exception cause) =>
        new("unavailable", "The recurring source does not match its actual resource-owned occurrence.",
            new Dictionary<string, string> { ["scope"] = scope.Value, ["triggerId"] = resourceId }, cause);

}
