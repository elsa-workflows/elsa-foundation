using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CShells;
using CShells.Features;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.StandaloneTriggers;
using Elsa.ActorFoundation.Hosting;
using Elsa.Events;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Http;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Http;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;
using Contract = Kernel.Foundation.Contracts;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed partial class KernelStandalonePreparedStartTests
{
    private const string HeldRoute = "standalone-held-exact";
    private const string FanOutRoute = "standalone-fan-out-exact";
    private const string HeldKey = "held-source-key";
    private const string Proceed = "standalone-http-proceed";
    private const string Finish = "standalone-http-finish";
    private const string ResponseText = "first exact committed reply";
    private const string HttpAuth = "StandaloneHttpQualification";

    [Fact]
    public Task Sqlite_standalone_http_replays_the_source_owned_response_without_legacy_runtime() => QualifyStandaloneHttpAsync(false);

    [SkippableFact]
    public Task PostgreSql_standalone_http_replays_the_source_owned_response_without_legacy_runtime()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)),
            "Requires dedicated Foundation and Runtime PostgreSQL databases.");
        return QualifyStandaloneHttpAsync(true);
    }

    private static async Task QualifyStandaloneHttpAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-standalone-http");
        try
        {
            var provider = postgres ? "PostgreSql" : "Sqlite";
            var path = directory is null ? null : Path.Combine(directory.FullName, "foundation.db");
            var foundation = postgres ? Environment.GetEnvironmentVariable(FoundationDatabase)! : null;
            var runtime = postgres ? Environment.GetEnvironmentVariable(RuntimeDatabase)!
                : new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false }.ToString();
            if (postgres)
                Assert.NotEqual(new NpgsqlConnectionStringBuilder(foundation).Database, new NpgsqlConnectionStringBuilder(runtime).Database);
            var owner = EmbeddedTenant.Bind(Persistence.Value);
            var expressions = new StandaloneHttpExpressions();
            var proofs = new DelayedFirstBirthAcknowledgement();
            var model = ExecutableOf("standalone-held-native", Definition, Sequence("sequence",
                Wait("observe", Proceed),
                SetOutput("first-checkpoint", "first", Expression("value", "JavaScript", "'first'", "String")),
                Node("reply", Types.WriteHttpResponse, [Literal("StatusCode", 201, "Int32"), Literal("Body", ResponseText, "String")]),
                Wait("still-running", Finish),
                SetOutput("last-checkpoint", "last", Expression("value", "JavaScript", "'last'", "String")),
                Node("later-response", Types.WriteHttpResponse, [Literal("StatusCode", 202, "Int32"), Literal("Body", "later", "String")])));
            var executable = new WorkflowExecutable(model.Identity, model.RootActivity, model.ResumeTargets, model.CreatedAt,
                model.CompatibilityMetadata, new(WorkflowExecutableInputContract.CurrentVersion, []), model.Dependencies, model.IncidentStrategy);
            var heldSource = RuntimeStandaloneIngressSource.BuildOutboxItemId(Persistence, Persistence.Value, HeldRoute, "Http", HeldKey);
            Contract.ActorRef actor;

            await using (var host = OpenHost(TimeSpan.FromMilliseconds(700)))
            {
                await using var operation = await host.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var services = operation.ServiceProvider;
                Assert.Null(services.GetService<IWorkflowExecutionStateStore>());
                Assert.Null(services.GetService<IWorkflowSchedulerWorkQueue>());
                Assert.Null(services.GetService<IRuntimeCheckpointCommitStore>());
                Assert.Null(services.GetService<IRuntimeIngressWaitTarget>());
                await services.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
                await EnableHttpReservationsAsync(host);
                var publication = await PublishAsync(services, executable, 0, DateTimeOffset.UtcNow, token);
                await WriteSourceAsync(services, HeldRoute, "WorkflowResponse", publication);
                await WriteSourceAsync(services, FanOutRoute, "AsyncFanOut", publication);
                var shell = host.Services.GetRequiredService<KernelShellRuntime>();
                await shell.StartAsync(token);
                try
                {
                    await ServeAsync(host);
                    using var client = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
                    using var denied = await RequestAsync(client, HeldRoute, HeldKey, authorized: false);
                    Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
                    var ingress = services.GetRequiredService<IRuntimeIngressDeliveryStore>();
                    Assert.Null(await ingress.FindDeliveryAsync(DeliveryScope, heldSource, token));
                    var bookmarks = new EfBookmarkStateStore(services.GetRequiredService<RuntimeDbContext>(),
                        services.GetRequiredService<IPersistenceAccessContextAccessor>());
                    var retainedWait = new BookmarkState("legacy-wait", "legacy-execution", "activity", "node", "resume",
                        HttpEndpointStimulus.StimulusType, HttpEndpointStimulus.Hash(HeldRoute, "POST"), null,
                        new Dictionary<string, string>(), DateTimeOffset.UtcNow, null);
                    await bookmarks.SaveAsync(retainedWait, token);
                    using var blocked = await RequestAsync(client, HeldRoute, HeldKey);
                    await ExpectStatusAsync(blocked, HttpStatusCode.ServiceUnavailable);
                    Assert.Null(await ingress.FindDeliveryAsync(DeliveryScope, heldSource, token));
                    Assert.True(await bookmarks.DeleteAsync(retainedWait.WorkflowExecutionId, retainedWait.BookmarkId, token));
                    using var timedOut = await RequestAsync(client, HeldRoute, HeldKey);
                    await ExpectStatusAsync(timedOut, HttpStatusCode.GatewayTimeout);
                    Assert.Equal(heldSource, (await timedOut.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token)).GetProperty("deliveryId").GetString());
                    while (proofs.Actor is null)
                    {
                        await ProcessAsync(services, token);
                        await Task.Delay(45, token);
                    }
                    actor = new(Assert.IsType<string>(proofs.Actor));
                    var reads = await shell.ReadAsync(token);
                    _ = await WaitForAsync(reads, owner, actor, state => state.Header.Status == Contract.ActorStatus.Running, token);
                    var source = await ingress.FindDeliveryAsync(DeliveryScope, heldSource, token);
                    Assert.Contains(source!.SubscriptionDelivery!.ControlFacts!.Transport, observation => observation.State == RuntimeIngressTransportState.TimedOut);
                    Assert.Null(Progress(source).Receipt);

                    using var accepted = await RequestAsync(client, FanOutRoute, "async-source-key");
                    await ExpectStatusAsync(accepted, HttpStatusCode.Accepted);
                    var asyncSource = (await accepted.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token)).GetProperty("deliveryId").GetString()!;
                    var asyncReceipt = await CompleteSourceAsync(services, asyncSource);
                    Assert.NotEqual(actor.Id, asyncReceipt.EvidenceId);

                    var unpublished = await services.GetRequiredService<IWorkflowActivationCoordinator>().DeactivateAsync(new(executable,
                        publication.SlotName, WorkflowActivationSource.Publishing, publication.Revision), token);
                    Assert.True(unpublished.Succeeded, unpublished.Diagnostic);
                }
                finally { await StopAsync(shell); }
            }

            await using (var host = OpenHost(TimeSpan.FromSeconds(3)))
            {
                await EnableHttpReservationsAsync(host);
                var shell = host.Services.GetRequiredService<KernelShellRuntime>();
                await shell.StartAsync(token);
                try
                {
                    Assert.Empty(host.Services.GetRequiredService<KernelServing>().Slots);
                    await ServeAsync(host);
                    using var client = new HttpClient { BaseAddress = new Uri(host.Urls.Single()) };
                    var opened = await host.Services.GetRequiredService<KernelShellStorage>().OpenAsync(token);
                    await ResumeAsync(opened.Ingress, actor, Proceed);
                    var firstPark = await WaitForAsync(opened.Reads, owner, actor, item => item.Park is not null, token);
                    using var unresolved = await RequestAsync(client, HeldRoute, HeldKey);
                    await ExpectStatusAsync(unresolved, HttpStatusCode.GatewayTimeout);
                    expressions.Available = true;
                    await RecoverAsync(host, firstPark, "recover-before-response");
                    await WaitForAsync(opened.Reads, owner, actor, item => item is { Park: null, Header.Status: Contract.ActorStatus.Running }, token);
                    await ResumeAsync(opened.Ingress, actor, Proceed, "repeat-after-recover");
                    using var answered = await RequestAsync(client, HeldRoute, HeldKey);
                    await ExpectAnswerAsync(answered, actor);
                    Assert.Equal(Contract.ActorStatus.Running, (await opened.Reads.GetAsync(owner, actor, token))?.Header.Status);
                    expressions.Available = false;
                    await ResumeAsync(opened.Ingress, actor, Finish);
                    var laterPark = await WaitForAsync(opened.Reads, owner, actor, item => item.Park is not null, token);
                    using var committedBeforePark = await RequestAsync(client, HeldRoute, HeldKey);
                    await ExpectAnswerAsync(committedBeforePark, actor);
                    expressions.Available = true;
                    await RecoverAsync(host, laterPark, "recover-after-response");
                    await WaitForAsync(opened.Reads, owner, actor, item => item is { Park: null, Header.Status: Contract.ActorStatus.Running }, token);
                    await ResumeAsync(opened.Ingress, actor, Finish, "repeat-after-recover");
                    var ended = await WaitForAsync(opened.Reads, owner, actor, item => item.Header.Status == Contract.ActorStatus.Finished, token);
                    var plan = await opened.RetentionPolicies.TryApplyRetentionPolicyAsync(owner, 0,
                        new(Finished: TimeSpan.Zero), long.MaxValue, cancellationToken: token);
                    Assert.Equal(Contract.ActorRetentionPolicyChangeOutcome.Applied, plan.Outcome);
                    Assert.Equal(Contract.AdmissionOutcome.Accepted,
                        (await opened.AdmitRetentionDelete(owner, actor, Assert.IsType<string>(ended.Selection), token)).Outcome);
                    while (await opened.Reads.GetAsync(owner, actor, token) is not null) await Task.Delay(30, token);
                    using var replay = await RequestAsync(client, HeldRoute, HeldKey);
                    await ExpectAnswerAsync(replay, actor);
                    using var unauthorizedReplay = await RequestAsync(client, HeldRoute, HeldKey, authorized: false);
                    Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedReplay.StatusCode);
                    Assert.False(unauthorizedReplay.Headers.Contains(KernelHttpEdge.WorkflowInstanceIdHeader));
                    await using var operation = await host.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                    var delivery = await operation.ServiceProvider.GetRequiredService<IRuntimeIngressDeliveryStore>()
                        .FindDeliveryAsync(DeliveryScope, heldSource, token);
                    Assert.Null(Progress(Assert.IsType<RuntimePostCommitOutboxItem>(delivery)).Receipt);
                    proofs.Released = true;
                    var accepted = await CompleteSourceAsync(operation.ServiceProvider, heldSource);
                    Assert.Equal(actor.Id, accepted.EvidenceId);
                    Assert.Equal(executable.Identity, accepted.Material);
                }
                finally { await StopAsync(shell); }
            }

            WebApplication OpenHost(TimeSpan timeout)
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                var services = builder.Services;
                services.AddAuthentication(HttpAuth).AddScheme<AuthenticationSchemeOptions, StandaloneHttpAuthentication>(HttpAuth, _ => { });
                services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string> { HttpAuth });
                services.AddSingleton<IWellKnownTypeRegistry, KernelPublishedExecuteTests.FixtureTypes>();
                services.AddSingleton<IPortableExpressionEvaluator>(expressions);
                new EventsFeature().ConfigureServices(services);
                services.AddWorkflowActivationDefaults();
                new RuntimeArtifactsEntityFrameworkCoreFeature
                {
                    Provider = provider, ConnectionString = runtime, RecoveryContinuationSigningKey = new string('r', 32)
                }.ConfigureServices(services);
                new RuntimePublicationEntityFrameworkCoreFeature().ConfigureServices(services);
                new RuntimePreparedStandaloneEntityFrameworkCoreFeature().ConfigureServices(services);
                new HttpFeature(new ShellFeatureContext(new ShellSettings { Id = new ShellId("qualification") }, [])).ConfigureServices(services);
                new StandaloneHttpTriggerAuthoringFeature().ConfigureServices(services);
                var runtimeHttp = new WorkflowsRuntimeStandaloneHttpFeature();
                runtimeHttp.ConfigureServices(services);
                var activities = new KernelActivitiesHttpFeature { SyncRequestTimeout = timeout };
                activities.ConfigureServices(services);
                var hosting = new WorkflowsRuntimeKernelHostingFeature
                {
                    Provider = provider, SqlitePath = path, PostgresConnectionString = foundation, PreparedStandaloneStarts = true,
                    PreparedStandaloneHttpFanOut = true, PreparedStandaloneHttpResponses = true
                };
                hosting.ConfigureServices(services, KernelShellStorage.CreateRetainedHttpResponseQualification(provider, path, foundation),
                    protectChildExecutables: true);
                var proof = services.Single(descriptor => descriptor.ServiceType == typeof(IRuntimePreparedStartAttemptProof));
                services.Replace(ServiceDescriptor.Scoped<IRuntimePreparedStartAttemptProof>(provider =>
                    proofs.Wrap((IRuntimePreparedStartAttemptProof)proof.ImplementationFactory!(provider))));
                var kernel = new WorkflowsRuntimeKernelFeature();
                kernel.ConfigureServices(services);
                kernel.PostConfigureServices(services);
                runtimeHttp.PostConfigureServices(services);
                activities.PostConfigureServices(services);
                var app = builder.Build();
                app.UseAuthentication();
                app.Use((context, next) =>
                {
                    context.RequestServices.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(Persistence));
                    return next(context);
                });
                activities.UseMiddleware(app, app.Environment);
                return app;
            }

            async Task EnableHttpReservationsAsync(WebApplication host)
            {
                await host.Services.GetRequiredService<EfModuleMigrator<RuntimeDbContext>>().InitializeAsync(token);
                await using var operation = await host.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var services = operation.ServiceProvider;
                var context = services.GetRequiredService<RuntimeDbContext>();
                var gate = Assert.IsType<EfSchemaModuleGate>(services.GetRequiredService<EfSchemaFinalizationGates>().FindModuleGate(context.GetType()));
                var store = new EfSchemaFinalizationStore(context);
                var member = new SchemaFinalizationMember("standalone-http-qualification", "isolated-host");
                string[] admittedFamilies = ["StandaloneTriggers", RuntimeTriggerBindingEfModule.SchemaFamily,
                    RuntimeArtifactEfModule.SchemaFamily, RuntimeOperationalStateEfModule.SchemaFamily, RuntimePostCommitOutboxEfModule.SchemaFamily,
                    "RuntimeSchedulerWork", RuntimeWorkflowExecutionEfModule.SchemaFamily, "RuntimeModelState"];
                foreach (var chain in gate.Families.Chains.Where(chain => admittedFamilies.Contains(chain.Family)))
                {
                    var record = Assert.IsType<SchemaFinalizationRecord>(await store.FindAsync(chain.Family, token));
                    if (record.FinalizedVersion == chain.CurrentVersion) continue;
                    var intent = await store.RecordIntentAsync(chain.Family, record.Revision, chain.CurrentVersion, chain.ReadableVersions, member, token);
                    Assert.True(intent.Applied);
                    var finalized = await store.CommitIntentAsync(chain.Family, intent.Record.Revision, chain.ReadableVersions, member, token);
                    Assert.True(finalized.Applied);
                }
                await gate.RefreshAsync(context, token);
            }

            async Task ServeAsync(WebApplication host)
            {
                await host.Services.GetRequiredService<IHttpEndpointRouteTableSynchronizer>().RefreshAsync(token);
                await host.StartAsync(token);
            }

            async Task WriteSourceAsync(IServiceProvider services, string route, string mode, WorkflowActivationSlot slot)
            {
                var now = DateTimeOffset.UtcNow;
                var origin = new TriggerManagementOrigin(TriggerManagementKind.Studio);
                var selector = new TriggerTargetObservation(Definition, slot.SlotName, slot.Revision, slot.ActiveActivationId,
                    RuntimeIngressAdmission.ComputeInputContractFingerprint(executable.InputContract!), "native", TriggerTargetState.Available);
                var trigger = new StandaloneTrigger(route, route, "http", 1, JsonSerializer.SerializeToElement(new
                {
                    method = "POST", path = route, sourceAccess = "Authenticated", http = new { mode }
                }), 1, 1, 1, TriggerDesiredState.Enabled, origin, now, now, "qualification", tenantId: Persistence.Value);
                var subscription = new TriggerSubscription(route + "-subscription", route, Definition, slot.SlotName, "native", 1, 1, true, [], origin,
                    new(route + "-grant", route, route + "-subscription", Definition, slot.SlotName, "native", "http", 1,
                        TriggerGrantStatus.Granted, "qualification", now));
                var committer = services.GetRequiredService<ITriggerManagementCommitter>();
                Assert.True(committer.CanActivateType("http", 1));
                var receipt = await committer.CommitAsync(DeliveryScope,
                    new(route + "-create", route + "-create", "qualification", null, trigger, [subscription]) { ObservedTargets = [selector] }, token);
                Assert.Equal(route, receipt.TriggerId);
            }

            async Task<RuntimeTargetAcceptanceReceipt> CompleteSourceAsync(IServiceProvider services, string sourceId)
            {
                var ingress = services.GetRequiredService<IRuntimeIngressDeliveryStore>();
                while (true)
                {
                    var source = await ingress.FindDeliveryAsync(DeliveryScope, sourceId, token);
                    if (source is not null && Progress(source).Receipt is { } receipt)
                    {
                        Assert.Equal(RuntimeTargetAcceptanceKind.PreparedActorBirth, receipt.Kind);
                        Assert.Empty(source.SubscriptionDelivery!.WaitDelivery!.Recipients);
                        return receipt;
                    }
                    await ProcessAsync(services, token);
                    await Task.Delay(45, token);
                }
            }

            async Task<HttpResponseMessage> RequestAsync(HttpClient client, string route, string key, bool authorized = true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/workflows/http/" + route) { Content = JsonContent.Create(new { }) };
                request.Headers.Host = "qualification.test";
                request.Headers.Add("Idempotency-Key", key);
                if (authorized) request.Headers.Add("X-Qualification-Subject", "reader");
                return await client.SendAsync(request, token);
            }

            async Task ResumeAsync(Contract.IIngress ingress, Contract.ActorRef reference, string name, string? attempt = null)
            {
                var admitted = await ingress.SendAsync(owner, new Contract.Target.Ref(reference), attempt is null ? name : name + "-" + attempt, NativeKinds.Resume,
                    JsonSerializer.Serialize(new ResumeStimulus(name)), cancellationToken: token);
                Assert.Equal(Contract.AdmissionOutcome.Accepted, admitted.Outcome);
            }

            async Task RecoverAsync(WebApplication host, Contract.ActorView park, string operation)
            {
                var target = park.Header.Ref;
                var operators = await host.Services.GetRequiredService<KernelShellStorage>().OpenOperatorsAsync(
                    (scope, actor, kind) => scope.Tenant == owner.Tenant && actor == target && kind == Contract.Kinds.Recover, token);
                var admitted = await operators.SendAsync(owner, new Contract.Target.Ref(target), operation, Contract.Kinds.Recover,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(Assert.IsType<string>(park.Selection), ExpectedTarget: target)),
                    cancellationToken: token);
                Assert.Equal(Contract.AdmissionOutcome.Accepted, admitted.Outcome);
            }

            async Task ExpectAnswerAsync(HttpResponseMessage result, Contract.ActorRef reference)
            {
                await ExpectStatusAsync(result, HttpStatusCode.Created);
                Assert.Equal(ResponseText, await result.Content.ReadAsStringAsync(token));
                Assert.Equal("text/plain", result.Content.Headers.ContentType?.MediaType);
                Assert.Equal(reference.Id, Assert.Single(result.Headers.GetValues(KernelHttpEdge.WorkflowInstanceIdHeader)));
            }

            async Task ExpectStatusAsync(HttpResponseMessage result, HttpStatusCode expected) =>
                Assert.True(result.StatusCode == expected, $"Expected {expected}, got {result.StatusCode}: {await result.Content.ReadAsStringAsync(token)}");
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static ExecutableNode Wait(string id, string eventName) =>
        Node(id, Types.Event, [Literal("EventName", eventName), Literal("CanStartWorkflow", false, "Boolean")]);

    private sealed class DelayedFirstBirthAcknowledgement
    {
        private string? _actor;
        public string? Actor => Volatile.Read(ref _actor);
        public volatile bool Released;

        public IRuntimePreparedStartAttemptProof Wrap(IRuntimePreparedStartAttemptProof physical) => new Observations(this, physical);

        private sealed class Observations(DelayedFirstBirthAcknowledgement owner, IRuntimePreparedStartAttemptProof physical)
            : IRuntimePreparedStartAttemptProof
        {
            public async ValueTask<RuntimePreparedStartProof> ObserveAsync(TriggerScope scope, string actorReference, string issuance,
                CancellationToken cancellationToken)
            {
                var result = await physical.ObserveAsync(scope, actorReference, issuance, cancellationToken);
                if (result != RuntimePreparedStartProof.Accepted || owner.Released) return result;
                var first = Interlocked.CompareExchange(ref owner._actor, actorReference, null) ?? actorReference;
                return first == actorReference ? RuntimePreparedStartProof.Unresolved : result;
            }
        }
    }

    private sealed class StandaloneHttpExpressions : IPortableExpressionEvaluator
    {
        public volatile bool Available;

        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => Available
            ? ValueTask.FromResult(JsonSerializer.SerializeToElement("recovered"))
            : throw new NativeExpressionsUnavailableException("The qualification evaluator is unavailable.");
    }

    private sealed class StandaloneHttpAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Qualification-Subject", out var subject)) return Task.FromResult(AuthenticateResult.NoResult());
            Claim[] claims = [new(IdentityClaimTypes.UserId, subject.ToString()), new(IdentityClaimTypes.Normalized, "v1"),
                new(IdentityClaimTypes.TenantId, Persistence.Value)];
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
