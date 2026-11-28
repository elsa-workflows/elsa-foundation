using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Scheduling.StandaloneTriggers;
using Elsa.Events;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using Elsa.ActorFoundation.Hosting;
using Contract = Kernel.Foundation.Contracts;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed partial class KernelStandalonePreparedStartTests
{
    private const string EventId = "standalone-start-event";
    private const string TimerId = "standalone-start-timer";
    private const string EventFamily = "Elsa.Qualification.Event";
    private static readonly ValueTypeDescriptor EventSchema = new("Qualification.Event", schemaVersion: 1);
    private static TriggerScope DeliveryScope => new(Persistence, Persistence.Value);

    [Fact]
    public Task Sqlite_activated_Event_and_recurring_delivery_recover_orphaned_births() => RunActivationAsync(false);

    [SkippableFact]
    public Task PostgreSql_activated_Event_and_recurring_delivery_recover_orphaned_births()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)),
            "Requires dedicated Foundation and Runtime PostgreSQL databases.");
        return RunActivationAsync(true);
    }

    private static async Task RunActivationAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(70));
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-standalone-activation");
        try
        {
            var foundation = postgres ? Environment.GetEnvironmentVariable(FoundationDatabase)! : null;
            var runtime = postgres ? Environment.GetEnvironmentVariable(RuntimeDatabase)!
                : new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false }.ToString();
            if (postgres)
                Assert.NotEqual(new NpgsqlConnectionStringBuilder(foundation).Database, new NpgsqlConnectionStringBuilder(runtime).Database);
            await RunActivationAsync(postgres ? "PostgreSql" : "Sqlite",
                directory is null ? null : Path.Combine(directory.FullName, "foundation.db"), foundation, runtime, deadline.Token);
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunActivationAsync(string provider, string? path, string? foundation, string runtime, CancellationToken token)
    {
        var clock = new TestClock(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        var model = ExecutableOf("standalone-activated-v1", Definition, SetOutput("result", "value", Literal("value", "exact actor")));
        var executable = new WorkflowExecutable(model.Identity, model.RootActivity, model.ResumeTargets, model.CreatedAt,
            model.CompatibilityMetadata, new(WorkflowExecutableInputContract.CurrentVersion, []), model.Dependencies, model.IncidentStrategy);
        var nextModel = ExecutableOf("standalone-activated-v2", Definition, WriteLine("replacement", Literal("text", "replacement")));
        var replacement = new WorkflowExecutable(nextModel.Identity with { ArtifactVersion = "2", DefinitionVersionId = "version-2" },
            nextModel.RootActivity, nextModel.ResumeTargets, nextModel.CreatedAt, nextModel.CompatibilityMetadata, nextModel.IncidentStrategy);
        var eventSignal = new StandaloneTriggerSignal(EventId, "event-once", EventSchema, JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }));
        var eventSourceId = RuntimeStandaloneIngressSource.BuildOutboxItemId(Persistence, Persistence.Value, EventId, EventFamily, eventSignal.SourceKey);
        RecurringTriggerOccurrenceClaim orphan;
        string timerSourceId;
        Dictionary<string, string> actorBySource;
        var recoverableProof = new HidePositiveProof();

        await using (var host = OpenHost(lostObservation: true))
        {
            await EnablePreparedRuntimeSchemaAsync(host, token);
            await using var operation = await host.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
            var services = operation.ServiceProvider;
            var publication = await PublishAsync(services, executable, 0, clock.GetUtcNow(), token);
            await WriteTriggerAsync(services, EventId, "event", new { name = EventId });
            await WriteTriggerAsync(services, TimerId, "timer", new { interval = "PT1H" });
            var shell = host.Services.GetRequiredService<KernelShellRuntime>();
            await shell.StartAsync(token);
            try
            {
                var eventAccepted = await services.GetRequiredService<IStandaloneTriggerIngress<FixtureEvent>>().AdmitAsync(eventSignal, token);
                Assert.False(eventAccepted.Replayed);
                var scheduleStore = services.GetRequiredService<IRecurringTriggerScheduleStore>();
                var now = clock.GetUtcNow();
                var timer = RecurringTriggerSchedule.ForResource(new(TimerId, 1, 1, "timer", "Elsa.StandaloneTimer",
                    Persistence, Persistence.Value), RecurringScheduleKind.Interval, "PT1H", now, now);
                await scheduleStore.SaveAsync(timer, token);
                orphan = Assert.Single(await scheduleStore.ClaimDueAsync(new("first-pump", now, TimeSpan.FromMinutes(1), 5), token));
                var accepted = await Assert.Single(services.GetServices<IStandaloneScheduleSource>()).AdmitAsync(Persistence, orphan, token);
                Assert.False(accepted.Replayed);
                timerSourceId = accepted.Item.OutboxItemId;
                Assert.Equal(timer.BuildOccurrenceKey(), accepted.Item.StandaloneIngress?.SourceKey);

                var published = await PublishAsync(services, replacement, publication.Revision, now, token);
                Assert.NotEqual(publication.ActiveActivationId, published.ActiveActivationId);
                var runtimeDb = services.GetRequiredService<RuntimeDbContext>();
                actorBySource = new(StringComparer.Ordinal);
                for (var index = 0; index < 2; index++)
                {
                    var firstProcessing = await ProcessAsync(services, token, limit: 1);
                    Assert.Equal(1, firstProcessing.FailedCount);
                    var id = Assert.Single(firstProcessing.Items).OutboxItemId;
                    Assert.Equal(index + 1, recoverableProof.Births.Count);
                    actorBySource.Add(id, recoverableProof.Births[index]);
                    var pending = Assert.IsType<RuntimePostCommitOutboxItem>(await services.GetRequiredService<IRuntimeIngressDeliveryStore>()
                        .FindDeliveryAsync(DeliveryScope, id, token));
                    Assert.Null(Progress(pending).Receipt);
                    Assert.Equal(RuntimeRecipientPhase.DispatchCommitted, Progress(pending).Phase);
                    var planned = Assert.Single(pending.SubscriptionDelivery!.Recipients).StartIdentity.WorkflowExecutionId;
                    Assert.False(await runtimeDb.WorkflowExecutionStates.AnyAsync(row => row.WorkflowExecutionId == planned, token));
                }
                Assert.Equal(2, recoverableProof.Births.Distinct().Count());
                var reads = await shell.ReadAsync(token);
                var owner = EmbeddedTenant.Bind(Persistence.Value);
                foreach (var id in recoverableProof.Births)
                    await WaitForAsync(reads, owner, new(id), state => state.Header.Status == Contract.ActorStatus.Finished, token);
            }
            finally { await StopAsync(shell); }
        }

        clock.Advance(TimeSpan.FromMinutes(2));
        await using (var host = OpenHost(lostObservation: false))
        {
            await EnablePreparedRuntimeSchemaAsync(host, token);
            var shell = host.Services.GetRequiredService<KernelShellRuntime>();
            await shell.StartAsync(token);
            try
            {
                await using var operation = await host.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var services = operation.ServiceProvider;
                Assert.True((await services.GetRequiredService<IStandaloneTriggerIngress<FixtureEvent>>().AdmitAsync(eventSignal, token)).Replayed);
                var scheduleStore = services.GetRequiredService<IRecurringTriggerScheduleStore>();
                var next = Assert.Single(await scheduleStore.ClaimDueAsync(new("cold-pump", clock.GetUtcNow(), TimeSpan.FromMinutes(1), 5), token));
                Assert.False(await scheduleStore.SettleClaimAsync(orphan, clock.GetUtcNow().AddHours(1), token));
                Assert.Equal(orphan.Schedule.BuildOccurrenceKey(), next.Schedule.BuildOccurrenceKey());
                Assert.True(next.FencingToken > orphan.FencingToken);
                var replay = await Assert.Single(services.GetServices<IStandaloneScheduleSource>()).AdmitAsync(Persistence, next, token);
                Assert.True(replay.Replayed);
                Assert.Equal(timerSourceId, replay.Item.OutboxItemId);
                Assert.True(await scheduleStore.SettleClaimAsync(next, clock.GetUtcNow().AddHours(1), token));

                Assert.Equal(2, (await ProcessAsync(services, token)).DeliveredCount);
                foreach (var id in new[] { eventSourceId, timerSourceId })
                {
                    var source = Assert.IsType<RuntimePostCommitOutboxItem>(await services.GetRequiredService<IRuntimeIngressDeliveryStore>()
                        .FindDeliveryAsync(DeliveryScope, id, token));
                    Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, source.Status);
                    var receipt = Assert.IsType<RuntimeTargetAcceptanceReceipt>(Progress(source).Receipt);
                    Assert.Equal(RuntimeTargetAcceptanceKind.PreparedActorBirth, receipt.Kind);
                    Assert.Equal(executable.Identity, receipt.Material);
                    var recipient = Assert.Single(source.SubscriptionDelivery!.Recipients);
                    var raw = await services.GetRequiredService<KernelRuns>().GetAsync(new(receipt.EvidenceId), token);
                    Assert.False(raw.Missing);
                    Assert.Equal((executable.Identity.ArtifactId, Definition), (raw.Run?.Run.Workflow, raw.Run?.Run.Definition));
                    var projection = services.GetRequiredService<IRuntimePreparedStandaloneRunReader>();
                    var actor = Assert.IsType<WorkflowExecutionState>(await projection.ReadAsync(DeliveryScope, recipient, receipt, token));
                    Assert.Equal(receipt.EvidenceId, actor.WorkflowExecutionId);
                    Assert.Equal(recipient.SourceProvenance, actor.PinnedSource);
                    Assert.Null(await projection.ReadAsync(new(new("other"), "other"), recipient, receipt, token));
                    Assert.Equal(actorBySource[id], actor.WorkflowExecutionId);
                }
                Assert.Equal(0, (await ProcessAsync(services, token)).AttemptedCount);
                await host.StartAsync(token);
                using var http = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
                await AssertDeliveryAsync(http, EventId, actorBySource[eventSourceId], denyRun: false, token);
                await AssertDeliveryAsync(http, TimerId, actorBySource[timerSourceId], denyRun: false, token);
                await AssertDeliveryAsync(http, EventId, actorBySource[eventSourceId], denyRun: true, token);
            }
            finally { await StopAsync(shell); }
        }

        WebApplication OpenHost(bool lostObservation)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            var services = builder.Services;
            services.AddAuthentication(DeliveryAuth).AddScheme<AuthenticationSchemeOptions, DeliveryAuthentication>(DeliveryAuth, _ => { });
            services.AddFoundationIdentityAbstractions(options =>
                options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { DeliveryAuth });
            services.AddSingleton<IPermissionResourceHandler, DenyFixtureRun>();
            services.AddAuthorization();
            services.AddSingleton<TimeProvider>(clock);
            new EventsFeature().ConfigureServices(services);
            new ActivitiesPrimitivesFeature().ConfigureServices(services);
            WorkflowPublicationDefaults.Add(services);
            new RuntimeArtifactsEntityFrameworkCoreFeature
            {
                Provider = provider, ConnectionString = runtime, RecoveryContinuationSigningKey = new string('r', 32)
            }.ConfigureServices(services);
            new RuntimePublicationEntityFrameworkCoreFeature().ConfigureServices(services);
            new RuntimePreparedStandaloneEntityFrameworkCoreFeature().ConfigureServices(services);
            new WorkflowsRuntimePreparedStandaloneRecurringTriggersFeature().ConfigureServices(services);
            new PreparedStandaloneScheduleTriggersFeature().ConfigureServices(services);
            services.AddStandaloneTriggerIngress<FixtureEvent>(new("event", 1, EventFamily, EventSchema));
            services.AddScoped<IStandaloneTriggerSourceContext<FixtureEvent>, EventContext>();
            var hosting = new WorkflowsRuntimeKernelHostingFeature
            {
                Provider = provider, SqlitePath = path, PostgresConnectionString = foundation, PreparedStandaloneStarts = true
            };
            hosting.ConfigureServices(services);
            hosting.PostConfigureServices(services);
            var kernel = new WorkflowsRuntimeKernelFeature();
            kernel.ConfigureServices(services);
            kernel.PostConfigureServices(services);
            var retainedApi = new WorkflowsRuntimeKernelRetainedApiFeature();
            retainedApi.ConfigureServices(services);
            retainedApi.PostConfigureServices(services);
            var api = new StandaloneTriggersApiFeature();
            api.ConfigureServices(services);
            api.PostConfigureServices(services);
            if (lostObservation)
            {
                var descriptor = services.Single(item => item.ServiceType == typeof(IRuntimePreparedStartAttemptProof));
                services.Remove(descriptor);
                services.AddScoped<IRuntimePreparedStartAttemptProof>(sp =>
                    recoverableProof.Wrap((IRuntimePreparedStartAttemptProof)descriptor.ImplementationFactory!(sp)));
            }
            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.Use((context, next) =>
            {
                context.RequestServices.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(Persistence));
                return next(context);
            });
            api.MapEndpoints(app, app.Environment);
            return app;
        }

        async Task WriteTriggerAsync<T>(IServiceProvider services, string id, string type, T configuration)
        {
            var now = clock.GetUtcNow();
            var origin = new TriggerManagementOrigin(TriggerManagementKind.Studio);
            var trigger = new StandaloneTrigger(id, id, type, 1, JsonSerializer.SerializeToElement(configuration), 1, 1, 1,
                TriggerDesiredState.Enabled, origin, now, now, "qualification", tenantId: Persistence.Value);
            var subscription = new TriggerSubscription(id + "-subscription", id, Definition, KernelServing.DefaultSlotName, "native",
                1, 1, true, [], origin, new(id + "-grant", id, id + "-subscription", Definition, KernelServing.DefaultSlotName,
                    "native", type, 1, TriggerGrantStatus.Granted, "qualification", now));
            await services.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(DeliveryScope,
                new(id + "-create", id + "-create", "qualification", null, trigger, [subscription]), token);
        }
    }

    private static ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(IServiceProvider services, CancellationToken token, int limit = 10) =>
        services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(
            new(limit, intentKind: RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind), token);

    private const string DeliveryAuth = "StandaloneDeliveryFixture";

    private static async Task AssertDeliveryAsync(HttpClient client, string trigger, string actor, bool denyRun, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/runtime/standalone-trigger-deliveries?triggerId=" + trigger);
        if (denyRun) request.Headers.Add("X-Fixture-Deny-Run", "true");
        using var response = await client.SendAsync(request, token);
        var json = await response.Content.ReadAsStringAsync(token);
        Assert.True(response.IsSuccessStatusCode, $"Delivery HTTP returned {response.StatusCode}: {json}");
        using var content = JsonDocument.Parse(json);
        var recipient = Assert.Single(Assert.Single(content.RootElement.GetProperty("items").EnumerateArray()).GetProperty("recipients").EnumerateArray());
        Assert.Equal("Started", recipient.GetProperty("dispatch").GetString());
        Assert.NotEqual(actor, recipient.GetProperty("plannedExecutionIdentity").GetString());
        if (denyRun)
            Assert.DoesNotContain(actor, json);
        else
        {
            var execution = recipient.GetProperty("executionReference");
            Assert.Equal(actor, execution.GetProperty("id").GetString());
            Assert.Equal("/workflows/instances/" + actor, execution.GetProperty("inspectionPath").GetString());
        }
    }

    private sealed class DeliveryAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var subject = Request.Headers.ContainsKey("X-Fixture-Deny-Run") ? "denied-run" : "reader";
            Claim[] claims =
            [
                new(IdentityClaimTypes.UserId, subject), new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.TenantId, Persistence.Value), new(IdentityClaimTypes.Permission, "triggers/deliveries:read"),
                new(IdentityClaimTypes.Permission, "workflows/runtime:read")
            ];
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class DenyFixtureRun : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<PermissionEvaluationResult?>(context.Resource is WorkflowExecutionState &&
                context.Principal.FindFirstValue(IdentityClaimTypes.UserId) == "denied-run" ? PermissionEvaluationResult.Denied() : null);
    }

    private sealed class FixtureEvent;

    private sealed class EventContext : IStandaloneTriggerSourceContext<FixtureEvent>
    {
        public bool SupportsResourceAuthorization => true;
        public ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<StandaloneTriggerInvocationContext>(new(DeliveryScope, new(Persistence.Value), new("fixture-event-source", "qualification")));

        public ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(trigger.Id == EventId && signal.TriggerId == EventId);
    }

    private sealed class HidePositiveProof
    {
        internal List<string> Births { get; } = [];
        internal IRuntimePreparedStartAttemptProof Wrap(IRuntimePreparedStartAttemptProof physical) => new HiddenProof(physical, this);

        private sealed class HiddenProof(IRuntimePreparedStartAttemptProof physical, HidePositiveProof observations) : IRuntimePreparedStartAttemptProof
        {
            public async ValueTask<RuntimePreparedStartProof> ObserveAsync(TriggerScope scope, string actorReference, string issuance,
                CancellationToken cancellationToken)
            {
                var proof = await physical.ObserveAsync(scope, actorReference, issuance, cancellationToken);
                if (proof != RuntimePreparedStartProof.Accepted) return proof;
                observations.Births.Add(actorReference);
                return RuntimePreparedStartProof.Unresolved;
            }
        }
    }
}
