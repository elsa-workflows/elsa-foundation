using System.Text.Json;
using Elsa.Activities.Scheduling.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Scheduling.Tests;

public sealed class StandaloneScheduleSourcesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("tenant-a"), "tenant-a");
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static StandaloneTrigger Trigger(long revision = 1, long configuration = 1, TriggerDesiredState state = TriggerDesiredState.Enabled,
        string type = "timer", JsonElement? settings = null) => new("resource", "Schedule", type, 1, settings ?? Json("{\"interval\":\"PT5M\"}"),
            revision, configuration, 1, state, new(TriggerManagementKind.Studio), Now, Now, "creator", tenantId: Scope.TenantId);
    private static TriggerStoreWrite Write(StandaloneTrigger trigger) => new("request", "fingerprint", "operator", trigger.Revision - 1, trigger, []);

    private static ServiceProvider Services(IStandaloneTriggerIngressFactory? factory = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRecurringScheduleCalculator, RecurringScheduleCalculator>();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
        services.AddSingleton<TimeProvider>(new Clock());
        services.AddSingleton<IRuntimeIngressDeliveryStore, UnsupportedDelivery>();
        if (factory is not null) services.AddScoped<IStandaloneTriggerIngressFactory>(provider =>
        {
            ((ProbeFactory)factory).Services = provider;
            return factory;
        });
        new StandaloneScheduleTriggersFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Enable_and_resume_arm_future_generations_while_association_only_changes_preserve_the_cursor()
    {
        using var services = Services(); using var scope = services.CreateScope();
        var planner = Assert.Single(scope.ServiceProvider.GetServices<IStandaloneSchedulePlanner>());
        var first = planner.Plan(Scope, null, Write(Trigger()), null, Now).Replacement!;
        Assert.Equal(Now.AddMinutes(5), first.NextOccurrence);
        Assert.Null(first.ArtifactId); Assert.Null(first.ExecutableNodeId); Assert.Null(first.StimulusType); Assert.Null(first.StimulusHash);
        Assert.Equal(Scope.Persistence, first.Resource!.Scope);
        var overdue = first with { NextOccurrence = Now.AddDays(-3) };
        var edited = Trigger(revision: 2);
        Assert.Equal(StandaloneScheduleAction.Preserve, planner.Plan(Scope, new(Trigger(), []), Write(edited), overdue, Now).Action);
        var paused = Trigger(revision: 3, state: TriggerDesiredState.Paused);
        Assert.Equal(StandaloneScheduleAction.Deactivate, planner.Plan(Scope, new(edited, []), Write(paused), overdue, Now).Action);
        var resumed = planner.Plan(Scope, new(paused, []), Write(Trigger(revision: 4)), overdue with { IsActive = false }, Now).Replacement!;
        Assert.Equal(4, resumed.Resource!.Generation);
        Assert.Equal(Now.AddMinutes(5), resumed.NextOccurrence);
        Assert.NotEqual(overdue.BuildOccurrenceKey(), resumed.BuildOccurrenceKey());
    }

    [Fact]
    public void Configuration_and_timezone_changes_create_one_future_generation_without_taking_over_the_old_overdue_cursor()
    {
        using var services = Services(); using var scope = services.CreateScope();
        var planner = Assert.Single(scope.ServiceProvider.GetServices<IStandaloneSchedulePlanner>());
        var initial = Trigger(type: "cron", settings: Json("{\"expression\":\"0 9 * * *\",\"timezone\":\"UTC\"}"));
        var old = planner.Plan(Scope, null, Write(initial), null, Now).Replacement! with { NextOccurrence = Now.AddDays(-7) };
        var changed = Trigger(revision: 2, configuration: 2, type: "cron", settings: Json("{\"expression\":\"0 9 * * *\",\"timezone\":\"America/New_York\"}"));
        var replacement = planner.Plan(Scope, new(initial, []), Write(changed), old, Now).Replacement!;
        Assert.Equal(2, replacement.Resource!.Generation);
        Assert.Equal("America/New_York", replacement.Resource.TimeZoneId);
        Assert.True(replacement.NextOccurrence > Now);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 14, 0, 0, TimeSpan.Zero), replacement.NextOccurrence);
        Assert.Equal(old.ScheduleId, replacement.ScheduleId);
    }

    [Theory]
    [InlineData("timer", "PT5M")]
    [InlineData("cron", "0 9 * * *")]
    public async Task The_actual_source_module_submits_the_pump_claim_in_trusted_context_without_binding_tenant_from_signal(string type, string expression)
    {
        var factory = new ProbeFactory();
        using var services = Services(factory); using var scope = services.CreateScope();
        var planner = Assert.Single(scope.ServiceProvider.GetServices<IStandaloneSchedulePlanner>());
        var settings = type == "timer" ? Json("{\"interval\":\"PT5M\"}") : Json("{\"expression\":\"0 9 * * *\",\"timezone\":\"UTC\"}");
        var schedule = planner.Plan(Scope, null, Write(Trigger(type: type, settings: settings)), null, Now).Replacement!;
        var claim = new RecurringTriggerOccurrenceClaim(schedule, "pump", 1, 1, Now, Now.AddMinutes(1), 0);

        await Assert.ThrowsAsync<ReachedPublicPort>(() => scope.ServiceProvider.GetRequiredService<IStandaloneScheduleSource>()
            .AdmitAsync(Scope.Persistence, claim).AsTask());

        Assert.Equal(type, factory.Descriptor!.TypeKey);
        Assert.Same(claim, factory.Context!.ScheduleClaim);
        Assert.Equal(Scope, factory.Context.Scope);
        Assert.Equal("runtime.recurring-trigger", factory.Context.Actor.SystemIdentity);
        Assert.Equal(schedule.BuildOccurrenceKey(), factory.Signal!.SourceKey);
        Assert.Equal(schedule.NextOccurrence, factory.Signal.Metadata.GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal("UTC", factory.Signal.Metadata.GetProperty("timezone").GetString());
        Assert.Equal(expression, schedule.Expression);
    }

    [Fact]
    public void Resource_schedule_identity_distinguishes_scope_and_absent_tenant_without_changing_legacy_keys()
    {
        Assert.NotEqual(RecurringTriggerSchedule.BuildResourceId(new("one"), "resource", null),
            RecurringTriggerSchedule.BuildResourceId(new("two"), "resource", null));
        Assert.NotEqual(RecurringTriggerSchedule.BuildResourceId(new("one"), "resource", null),
            RecurringTriggerSchedule.BuildResourceId(new("one"), "resource", "one"));
        var legacy = new RecurringTriggerSchedule("artifact:node", "artifact", "node", "Timer", "hash", RecurringScheduleKind.Interval, "PT5M", Now, Now);
        Assert.Equal("recurring:artifact:node:" + Now.UtcTicks, legacy.BuildOccurrenceKey());
        Assert.False(JsonSerializer.SerializeToElement(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)).TryGetProperty("resource", out _));
    }

    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(Scope.Persistence); }
    private sealed class ReachedPublicPort : Exception;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class UnsupportedDelivery : IRuntimeIngressDeliveryStore;
    private sealed class ProbeFactory : IStandaloneTriggerIngressFactory
    {
        internal IServiceProvider Services = null!;
        public StandaloneTriggerSourceDescriptor? Descriptor; public StandaloneTriggerInvocationContext? Context; public StandaloneTriggerSignal? Signal;
        public IStandaloneTriggerIngress<TSource> Create<TSource>() where TSource : class => new Probe<TSource>(this,
            Services.GetRequiredKeyedService<StandaloneTriggerSourceDescriptor>(typeof(TSource)), Services.GetRequiredService<IStandaloneTriggerSourceContext<TSource>>());
        private sealed class Probe<T>(ProbeFactory owner, StandaloneTriggerSourceDescriptor descriptor, IStandaloneTriggerSourceContext<T> context) : IStandaloneTriggerIngress<T> where T : class
        {
            public async ValueTask<StandaloneTriggerIngressResult> AdmitAsync(StandaloneTriggerSignal signal, CancellationToken cancellationToken = default)
            { owner.Descriptor = descriptor; owner.Context = await context.GetAsync(cancellationToken); owner.Signal = signal; throw new ReachedPublicPort(); }
        }
    }
}
