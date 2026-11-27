using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CShells;
using Elsa.ActorFoundation.Hosting;
using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Kernel.Foundation.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed partial class KernelPublishedExecuteTests
{
    private const string FoundationDatabase = "KERNEL_EXECUTE_FOUNDATION_POSTGRES";
    private const string RuntimeDatabase = "KERNEL_EXECUTE_RUNTIME_POSTGRES";
    private const string RateLimitedSubject = "rate-limited-inline";
    private static readonly PersistenceScope Scope = new(PersistenceScope.DefaultValue);

    [Fact]
    public Task Sqlite_standard_execute_admits_published_native_and_flowchart() => RunAsync(false);

    [Fact]
    public Task Sqlite_direct_test_runs_use_an_isolated_prepared_root() => RunAsync(false, directTests: true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Sqlite_ephemeral_test_runs_preserve_completion_policy(bool recordCompletion) =>
        RunAsync(false, directTests: true, recordEphemeralTestCompletion: recordCompletion);

    [SkippableFact]
    public Task PostgreSql_standard_execute_admits_published_native_and_flowchart()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true);
    }

    [SkippableFact]
    public Task PostgreSql_direct_test_runs_use_an_isolated_prepared_root()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true, directTests: true);
    }

    [SkippableFact]
    public Task PostgreSql_ephemeral_test_runs_record_completion_when_configured() => PostgreSqlEphemeralTestAsync(recordCompletion: true);

    [SkippableFact]
    public Task PostgreSql_ephemeral_test_runs_report_write_free_completion_and_promotion() => PostgreSqlEphemeralTestAsync(recordCompletion: false);

    private static Task PostgreSqlEphemeralTestAsync(bool recordCompletion)
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true, directTests: true, recordEphemeralTestCompletion: recordCompletion);
    }

    [Fact]
    public Task Sqlite_shipped_ephemeral_execute_reports_volatile_completion_and_committed_promotion() => RunAsync(false, inline: true);

    [SkippableFact]
    public Task PostgreSql_shipped_ephemeral_execute_reports_volatile_completion_and_committed_promotion()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true, inline: true);
    }

    private static async Task RunAsync(bool postgres, bool directTests = false, bool inline = false, bool? recordEphemeralTestCompletion = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-published-execute-");
        try
        {
            var foundation = postgres ? Environment.GetEnvironmentVariable(FoundationDatabase) : null;
            var runtime = postgres ? Environment.GetEnvironmentVariable(RuntimeDatabase)! : new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false
            }.ToString();
            if (postgres) Assert.NotEqual(new Npgsql.NpgsqlConnectionStringBuilder(foundation).Database,
                new Npgsql.NpgsqlConnectionStringBuilder(runtime).Database);
            await RunAsync(postgres ? "PostgreSql" : "Sqlite", directory?.FullName, foundation, runtime, directTests, inline,
                recordEphemeralTestCompletion, deadline.Token);
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunAsync(string provider, string? directory, string? foundation, string runtime, bool directTests, bool inline,
        bool? recordEphemeralTestCompletion, CancellationToken token)
    {
        var canonical = ExecutableOf("execute-native", "canonical-publication", Sequence("sequence",
            SetOutput("input", "message", Request("input", "message")), SetOutput("variable", "seed", VariableRead("input", "seed-key"))),
            Variable("seed-key", "seed", "authored-variable"));
        var contract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion,
            [new("message", new TypeReference("String"), isRequired: false, JsonSerializer.SerializeToElement("authored-input"))]);
        canonical = new(canonical.Identity, canonical.RootActivity, canonical.ResumeTargets, canonical.CreatedAt,
            canonical.CompatibilityMetadata, contract, canonical.Dependencies, canonical.RuntimeRequirements, canonical.StorageDriverRequirements,
            canonical.IncidentStrategy, workflowVariables: canonical.WorkflowVariables);
        var alias = PublishedAs(canonical, "native-blue", "2");
        var sibling = PublishedAs(canonical, "native-sibling", "3");
        var flowchart = ExecutableOf("execute-flowchart", "flowchart", Node("flow", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new { startNodeId = "output", connections = Array.Empty<object>(), variables = Array.Empty<object>() }),
            slots: [Slot("Flowchart.Activities", SetOutput("output", "message", Request("input", "message")))]));

        var waiting = ExecutableOf("execute-waiting", "waiting", Sequence("sequence",
            Node("delay", Types.Delay, [Literal("Duration", "00:00:45", "TimeSpan")])));
        const string nativeRoute = "recoverable-native";
        const string flowchartRoute = "recoverable-flowchart";
        var recoverable = ExecutableOf("execute-recoverable", "recoverable", Sequence("sequence", RecoveryNodes(nativeRoute)));
        var recoverableFlowchart = ExecutableOf("execute-recoverable-flow", "recoverable-flow", Node("flow", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "wait",
                connections = new[]
                {
                    new { source = new { nodeId = "wait", port = "Done" }, target = new { nodeId = "output" } },
                    new { source = new { nodeId = "output", port = "Done" }, target = new { nodeId = "reply" } }
                }
            }), slots: [Slot("Flowchart.Activities", RecoveryNodes(flowchartRoute))]));
        var pure = Ephemeral(ExecutableOf("inline-native", "inline-native", Sequence("sequence", SetOutput("inline-output", "message", Literal("value", "no durable run")))));
        var slow = Ephemeral(ExecutableOf("inline-flowchart", "inline-flowchart", Node("flow", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "delay",
                connections = new[] { new { source = new { nodeId = "delay", port = "Done" }, target = new { nodeId = "output" } } },
                variables = Array.Empty<object>()
            }), slots: [Slot("Flowchart.Activities", Node("delay", Types.Delay, [Literal("Duration", "00:00:06", "TimeSpan")]),
                SetOutput("output", "message", Literal("value", "flowchart finished")))])));
        var retractedInline = Ephemeral(ExecutableOf("inline-retracted", "inline-retracted", Sequence("sequence",
            Node("delay", Types.Delay, [Literal("Duration", "00:00:06", "TimeSpan")]))));
        var initialFault = Ephemeral(ExecutableOf("inline-initial-fault", "inline-initial-fault", Sequence("sequence",
            SetOutput("first-output", "message", Expression("value", "JavaScript", "'recovered'", "String")))));
        var uncertain = Ephemeral(ExecutableOf("inline-uncertain", "inline-uncertain", Sequence("sequence",
            Node("blocked-script", Types.RunJavaScript, [Literal("Script", "return 1;", "String")]))));
        var refusedInlineTest = Ephemeral(ExecutableOf("inline-test-refused", "inline-test-refused", Sequence("sequence",
            Node("child", Types.Dispatch, [Literal("WorkflowDefinitionId", canonical.Identity.DefinitionId, "String"),
                Literal("WaitForCompletion", true, "Boolean")]))));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddElsaEndpoints();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication("KernelExecuteFixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("KernelExecuteFixture", _ => { });
        builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
            { "KernelExecuteFixture" });
        builder.Services.ReplaceCurrentSubjectPermissionAuthorizer<FixtureReadAuthorizer>();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IWorkflowExecutableStartPolicy>(new FixturePolicy(directTests ? WorkflowRunKind.TestRun : WorkflowRunKind.PublishedRun));
        builder.Services.AddSingleton<IPermissionResourceHandler, FixtureRunPolicy>();
        builder.Services.AddSingleton<IWellKnownTypeRegistry, FixtureTypes>();
        builder.Services.AddSingleton(new ShellSettings(new ShellId("default")));
        var expressions = new FixtureExpressions();
        builder.Services.AddSingleton<IPortableExpressionEvaluator>(expressions);
        var scripts = new BlockingScript();
        if (inline || recordEphemeralTestCompletion is false) builder.Services.AddSingleton<IJavaScriptScriptEvaluator>(scripts);
        builder.Services.AddSingleton(new WorkflowHttpEndpointSettings("/fixture/workflows", TimeSpan.FromSeconds(3), 4096,
            (context, _, _) => { context.Response.StatusCode = 504; return ValueTask.CompletedTask; }));
        builder.Services.AddWorkflowRuntime();
        builder.Services.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = provider, ConnectionString = runtime,
            RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32)
        });
        var pinBoundary = new PinBoundary();
        var pinDescriptor = builder.Services.Last(item => item.ServiceType == typeof(IRuntimeKernelExecutablePinStore));
        builder.Services.Remove(pinDescriptor);
        builder.Services.AddScoped<IRuntimeKernelExecutablePinStore>(services => new FixturePins(
            (IRuntimeKernelExecutablePinStore)ActivatorUtilities.CreateInstance(services, pinDescriptor.ImplementationType!), pinBoundary));
        var api = new WorkflowsRuntimeApiFeature();
        api.ConfigureServices(builder.Services);
        if (recordEphemeralTestCompletion is { } record)
            builder.Services.AddPersistenceModeOptions(new PersistenceModeOptions { RecordCompletion = record });
        var kernelHosting = new WorkflowsRuntimeKernelHostingFeature
        {
            Provider = provider,
            SqlitePath = directory is null ? null : Path.Combine(directory, "foundation.db"),
            PostgresConnectionString = foundation
        };
        if (recordEphemeralTestCompletion.HasValue)
            kernelHosting.ConfigureServices(builder.Services,
                KernelShellStorage.CreateEphemeralTestRunQualification(provider, kernelHosting.SqlitePath, foundation),
                protectChildExecutables: true);
        else if (directTests)
            kernelHosting.ConfigureServices(builder.Services,
                KernelShellStorage.CreateDirectTestRunQualification(provider, kernelHosting.SqlitePath, foundation),
                protectChildExecutables: true);
        else kernelHosting.ConfigureServices(builder.Services);
        new WorkflowsRuntimeKernelFeature().PostConfigureServices(builder.Services);
        api.PostConfigureServices(builder.Services);
        var kernelApi = new WorkflowsRuntimeKernelApiFeature();
        kernelApi.PostConfigureServices(builder.Services);
        if (inline)
        {
            var startDescriptor = builder.Services.Last(item => item.ServiceType == typeof(IWorkflowExecutionStartService));
            builder.Services.Remove(startDescriptor);
            builder.Services.AddScoped<IWorkflowExecutionStartService>(services =>
            {
                var context = services.GetRequiredService<IHttpContextAccessor>();
                if (context.HttpContext?.User.FindFirstValue(IdentityClaimTypes.UserId) != RateLimitedSubject)
                    return (IWorkflowExecutionStartService)startDescriptor.ImplementationFactory!(services);
                var runtimeShell = services.GetRequiredService<KernelShellRuntime>();
                var dispatch = new KernelPublishedRunDispatcher(services.GetRequiredService<IServiceScopeFactory>(),
                    services.GetRequiredService<KernelServing>(), Scope, runtimeShell.StartPublishedAsync,
                    (_, _, _, _) => ValueTask.FromResult(new InlineStartOutcome(InlineStartDisposition.Unavailable)
                    {
                        RefusalReason = Contract.RefusalReason.RateLimited, RetryAfter = TimeSpan.FromMilliseconds(1250)
                    }));
                return new WorkflowExecutionStartService(dispatch, services.GetRequiredService<IWorkflowExecutableStore>(), context,
                    services.GetRequiredService<NormalizedPrincipalValidator>());
            });
        }

        var scopeAdmission = new FixtureTestScopeAdmission();
        if (recordEphemeralTestCompletion is false)
        {
            var admissionDescriptor = builder.Services.Single(item => item.ServiceType == typeof(IWorkflowTestScopeAdmissionStore));
            builder.Services.Remove(admissionDescriptor);
            builder.Services.AddScoped<IWorkflowTestScopeAdmissionStore>(services => new ObservedTestScopeAdmission(
                (IWorkflowTestScopeAdmissionStore)admissionDescriptor.ImplementationFactory!(services), scopeAdmission));
        }
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
        kernelApi.MapEndpoints(app, app.Environment);
        kernelHosting.MapEndpoints(app, app.Environment);
        var scopes = app.Services.GetRequiredService<IServiceScopeFactory>();
        await using var publication = await scopes.CreateAsyncScopeAsync(Scope);
        var stores = publication.ServiceProvider;
        await stores.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
        var materials = stores.GetRequiredService<IWorkflowExecutableStore>();
        await materials.SaveAsync(canonical, token);
        await materials.SaveAsync(flowchart, token);
        await materials.SaveAsync(waiting, token);
        await materials.SaveAsync(recoverable, token);
        await materials.SaveAsync(recoverableFlowchart, token);
        await PublishAsync(alias, "blue");
        await PublishAsync(sibling, "default");
        await PublishAsync(flowchart, "default");
        await PublishAsync(waiting, "default");
        await PublishAsync(recoverable, "default");
        await PublishAsync(recoverableFlowchart, "default");
        await materials.SaveAsync(pure, token);
        await PublishAsync(pure, "default");
        if (recordEphemeralTestCompletion.HasValue)
        {
            await materials.SaveAsync(slow, token);
            await materials.SaveAsync(refusedInlineTest, token);
            if (recordEphemeralTestCompletion is false) await materials.SaveAsync(uncertain, token);
        }
        if (inline)
        {
            foreach (var candidate in new[] { slow, retractedInline, initialFault, uncertain })
            {
                await materials.SaveAsync(candidate, token);
                await PublishAsync(candidate, "default");
            }
        }
        var shell = app.Services.GetRequiredService<KernelShellRuntime>();
        await shell.StartAsync(token);
        try
        {
            await app.StartAsync(token);
            var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
            using var http = new HttpClient { BaseAddress = new(address) };
            var isolatedActors = new List<Contract.ActorRef>();
            if (recordEphemeralTestCompletion is { } recordCompletion)
            {
                await VerifyEphemeralMappingRefusalAsync(refusedInlineTest);
                await VerifyEphemeralTestAsync(pure, recordCompletion);
                if (!recordCompletion)
                {
                    await VerifyEphemeralCancellationBeforeOfferAsync(uncertain);
                    await VerifyEphemeralCancellationAsync(uncertain);
                    await VerifyEphemeralTestAsync(slow, expectActor: true);
                    await VerifyInterruptedStopRejoinsSettlementsAsync(pure);
                }
                return;
            }
            if (directTests)
            {
                await VerifyDirectTestAsync(canonical, "workflow-draft-snapshot", "draft:witness", closeBeforePin: false, interruptAfterPin: true);
                await VerifyDirectTestAsync(flowchart, "workflow-definition-version", "saved-witness", closeBeforePin: false);
                await VerifyDirectTestAsync(waiting, "workflow-definition-version", "stop-witness", closeBeforePin: false, stop: true);
                await VerifyDirectTestAsync(waiting, "workflow-definition-version", "expire-witness", closeBeforePin: false, expire: true);
                await VerifyDirectTestAsync(waiting, "workflow-definition-version", "closed-witness", closeBeforePin: true);
                await VerifyExpiryAsync();
                return;
            }
            if (inline)
            {
                await VerifyInlineAsync();
                return;
            }
            var target = WorkflowActivationReferenceIdentity.Create("publish-native-blue");
            using var ambiguous = await ExecuteAsync(canonical.Identity.ArtifactId, new { });
            Assert.Equal(HttpStatusCode.Conflict, ambiguous.StatusCode);
            using var wrongTenant = await ExecuteAsync(alias.Identity.ArtifactId, new { sourceReferenceId = target }, tenant: "another-tenant");
            Assert.Equal(HttpStatusCode.NotFound, wrongTenant.StatusCode);
            using var invalid = await ExecuteAsync(alias.Identity.ArtifactId, new { sourceReferenceId = target, inputs = new { unexpected = 1 } });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var denied = await ExecuteAsync(alias.Identity.ArtifactId, new { sourceReferenceId = target }, subject: "deny");
            Assert.True(denied.StatusCode == HttpStatusCode.InternalServerError,
                $"Expected Execute policy failure, got {denied.StatusCode}: {await denied.Content.ReadAsStringAsync(token)}");
            pinBoundary.BeforeAdmission = async (source, cancellationToken) =>
            {
                if (source.DefinitionId != sibling.Identity.DefinitionId) return;
                await using var retracting = await scopes.CreateAsyncScopeAsync(Scope);
                var coordinator = retracting.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>();
                Assert.True((await coordinator.DeactivateAsync(new(sibling, "default", WorkflowActivationSource.Publishing, 1), cancellationToken)).Succeeded);
                pinBoundary.Retracted = true;
            };
            using var stale = await ExecuteAsync(sibling.Identity.ArtifactId,
                new { sourceReferenceId = WorkflowActivationReferenceIdentity.Create("publish-native-sibling") }, subject: "retract");
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.True(pinBoundary.Retracted);
            var bound = EmbeddedTenant.Bind(Scope.Value);
            var reads = await shell.ReadAsync(token);
            Assert.Empty((await reads.ListAsync(bound, new(), cancellationToken: token)).Items);
            await using var pinScope = await scopes.CreateAsyncScopeAsync(Scope);
            var pins = pinScope.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
            Assert.Empty(await pins.ListPinnedExecutableArtifactIdsAsync(token));

            using var native = await ExecuteAsync(alias.Identity.ArtifactId, new { sourceReferenceId = target });
            var completed = await VerifyAsync(native, alias, canonical.Identity, "blue", "authored-input", "authored-variable");
            using var flow = await ExecuteAsync(flowchart.Identity.ArtifactId, new { inputs = new { message = "flowchart-input" } }, tenant: "default");
            await VerifyAsync(flow, flowchart, flowchart.Identity, "default", "flowchart-input");
            Assert.Equal(new[] { canonical.Identity.ArtifactId, flowchart.Identity.ArtifactId }.Order(StringComparer.Ordinal),
                (await pins.ListPinnedExecutableArtifactIdsAsync(token)).Order(StringComparer.Ordinal));

            using var forbiddenDelete = await DeleteAsync(completed, manage: false);
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);
            using var foreignDelete = await DeleteAsync(completed, manage: true, tenant: "another-tenant");
            Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);
            using var aliasDelete = await DeleteAsync(completed, manage: true, tenantAlias: "another-tenant");
            Assert.Equal(HttpStatusCode.NotFound, aliasDelete.StatusCode);
            using var mismatchedDelete = await DeleteAsync(completed, manage: true, tenant: "default", tenantAlias: "another-tenant");
            Assert.Equal(HttpStatusCode.NotFound, mismatchedDelete.StatusCode);
            Assert.NotNull(await reads.GetAsync(bound, new(completed), token));
            if (directory is not null)
            {
                var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "foundation.db"), Pooling = false };
                await using var locked = new SqliteConnection(connection.ToString());
                await locked.OpenAsync(token);
                await using var write = locked.BeginTransaction(deferred: false);
                using var busy = await DeleteAsync(completed, manage: true);
                Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
                await write.RollbackAsync(token);
                Assert.NotNull(await reads.GetAsync(bound, new(completed), token));
            }
            await VerifyDeletedAsync(completed);

            var delayed = await StartWaitingAsync();
            await VerifyDeletedAsync(delayed);

            var reauthorized = await StartWaitingAsync();
            using var deniedPurge = await DeleteAsync(reauthorized, manage: true, subject: "deny-terminal-purge");
            Assert.Equal(HttpStatusCode.NotFound, deniedPurge.StatusCode);
            Assert.Equal(Contract.ActorStatus.Cancelled, (await reads.GetAsync(bound, new(reauthorized), token))?.Header.Status);
            await VerifyDeletedAsync(reauthorized);
            var nativeParkSelection = await VerifyRecoveryAsync(recoverable, nativeRoute);
            await VerifyRecoveryAsync(recoverableFlowchart, flowchartRoute, nativeParkSelection);
            await VerifyReducedHistoryProjectionsAsync();

            async Task VerifyReducedHistoryProjectionsAsync()
            {
                var serving = app.Services.GetRequiredService<KernelServing>();
                var slot = WorkflowActivationSlotIdentity.Create(recoverable.Identity.DefinitionId, "default");
                var workflow = (serving.Find(slot) ?? throw new InvalidOperationException("The recovery workflow is not served.")).Workflow;
                var ingress = await shell.IngressAsync(token);
                foreach (var history in new[] { Contract.History.State, Contract.History.Minimal })
                {
                    expressions.IsAvailable = false;
                    var start = Kernel.WorkflowActor.WorkflowActor.Start(Kernel.Native.NativeExecutionModel.Start(workflow) with { History = history });
                    var admission = await ingress.SendAsync(bound, new Contract.Target.Start(start), Guid.NewGuid().ToString("N"),
                        Contract.Kinds.Start, cancellationToken: token);
                    Assert.Equal(Contract.AdmissionOutcome.Accepted, admission.Outcome);
                    var actor = Assert.IsType<Contract.ActorRef>(admission.Actor);
                    var projected = new ProjectedRun(reads, bound, actor, recoverable.Identity.DefinitionId, history);
                    await projected.ReadAsync(Contract.ActorStatus.Running, parked: false, token);

                    using var fault = await http.PostAsync("/fixture/workflows/" + nativeRoute, null, token);
                    var park = await projected.ReadAsync(Contract.ActorStatus.Faulted, parked: true, token);
                    expressions.IsAvailable = true;
                    using var recovery = await RequestRecoveryAsync(actor, HttpMethod.Post, selection: park.Selection);
                    Assert.Equal(HttpStatusCode.Accepted, recovery.StatusCode);
                    await projected.ReadAsync(Contract.ActorStatus.Running, parked: false, token);

                    using var completed = await http.PostAsync("/fixture/workflows/" + nativeRoute, null, token);
                    Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
                    await projected.ReadAsync(Contract.ActorStatus.Finished, parked: false, token);
                }
            }

            async Task VerifyInlineAsync()
            {
                var scope = EmbeddedTenant.Bind(Scope.Value);
                var read = await shell.ReadAsync(token);
                var pinRead = stores.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                var initial = await DurableRowCountAsync();
                var pinTail = await pinRead.ReadOpenTailAsync(Scope, token);

                using var wrongTenant = await ExecuteAsync(pure.Identity.ArtifactId, new { }, tenant: "different-tenant");
                Assert.Equal(HttpStatusCode.NotFound, wrongTenant.StatusCode);
                var volatileResult = await InlineAsync(pure, HttpStatusCode.OK, "Volatile");
                Assert.Equal("Finished", volatileResult.ActorStatus);
                Assert.Equal(nameof(WorkflowExecutionCommandDispatchStatus.Accepted), volatileResult.CommandDispatchStatus);
                Assert.Null(volatileResult.WorkflowExecutionId);
                Assert.Null(volatileResult.AgentId);
                Assert.Null(await read.GetAsync(scope, new(volatileResult.EnvelopeId), token));
                using (var response = await ReadRunAsync(volatileResult.EnvelopeId))
                    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal(initial, await DurableRowCountAsync());
                Assert.Equal(pinTail, await pinRead.ReadOpenTailAsync(Scope, token));

                var rateReceipt = await InlineAsync(pure, HttpStatusCode.TooManyRequests, "Unavailable", RateLimitedSubject);
                Assert.True(rateReceipt.Shed);
                Assert.Equal("RateLimited", rateReceipt.Reason);
                Assert.Equal(2, rateReceipt.RetryAfterSeconds);
                Assert.Null(rateReceipt.WorkflowExecutionId);

                pinBoundary.BeforeAdmission = async (source, ct) =>
                {
                    if (source.DefinitionId != retractedInline.Identity.DefinitionId) return;
                    await using var retract = await scopes.CreateAsyncScopeAsync(Scope);
                    var coordinator = retract.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>();
                    Assert.True((await coordinator.DeactivateAsync(new(retractedInline, "default", WorkflowActivationSource.Publishing, 1), ct)).Succeeded);
                    pinBoundary.Retracted = true;
                };
                var refused = await InlineAsync(retractedInline, HttpStatusCode.Conflict, "Refused");
                Assert.True(pinBoundary.Retracted);
                Assert.Null(refused.WorkflowExecutionId);
                Assert.Equal(initial, await DurableRowCountAsync());
                Assert.Empty(await pinRead.ListPinnedExecutableArtifactIdsAsync(token));

                var promoted = await InlineAsync(slow, HttpStatusCode.OK, "Committed");
                Assert.Equal("Running", promoted.ActorStatus);
                var actor = new Contract.ActorRef(Assert.IsType<string>(promoted.WorkflowExecutionId));
                var row = Assert.IsType<Contract.ActorView>(await read.GetAsync(scope, actor, token));
                Assert.Equal(promoted.SourceReferenceId, Kernel.WorkflowActor.WorkflowActor.DescribeRun(row.Header)?.PublishedStart?.SourceReferenceId);
                using (var response = await ReadRunAsync(actor.Id))
                {
                    Assert.True(response.StatusCode == HttpStatusCode.OK, $"Committed run read returned {response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");
                    var details = Assert.IsType<WorkflowInstanceDetailsView>(await response.Content.ReadFromJsonAsync<WorkflowInstanceDetailsView>(token));
                    Assert.Equal(nameof(PersistenceMode.Ephemeral), details.PersistenceMode);
                    Assert.Equal("boundary-level", details.InspectionGranularity);
                }
                Assert.Contains(slow.Identity.ArtifactId, await pinRead.ListPinnedExecutableArtifactIdsAsync(token));
                while ((row = (await read.GetAsync(scope, actor, token))!).Header.Status == Contract.ActorStatus.Running)
                    await Task.Delay(25, token);
                Assert.Equal(Contract.ActorStatus.Finished, row.Header.Status);
                var data = await Assert.IsAssignableFrom<Contract.IActorTerminalOutputReads>(read).ReadTerminalOutputAsync(scope, actor,
                    row.Header.EndedAt!.Value, includeValues: true, cancellationToken: token);
                using var output = JsonDocument.Parse(Assert.IsType<byte[]>(data?.Values));
                Assert.Equal("flowchart finished", output.RootElement.GetProperty("message").GetString());

                var parked = await InlineAsync(initialFault, HttpStatusCode.OK, "Committed");
                Assert.Equal("Faulted", parked.ActorStatus);
                Assert.Equal(nameof(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted), parked.CommandDispatchStatus);
                var parkedActor = new Contract.ActorRef(Assert.IsType<string>(parked.WorkflowExecutionId));
                Assert.True(await read.GetAsync(scope, parkedActor, token) is { Header: { Status: Contract.ActorStatus.Faulted, EndedAt: null } });
                Assert.Contains(initialFault.Identity.ArtifactId, await pinRead.ListPinnedExecutableArtifactIdsAsync(token));

                try
                {
                    var pending = InlineAsync(uncertain, HttpStatusCode.GatewayTimeout, "Unknown");
                    await scripts.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    var waitingSince = System.Diagnostics.Stopwatch.GetTimestamp();
                    Assert.False(pending.IsCompleted);
                    var unknown = await pending;
                    Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(waitingSince) >= TimeSpan.FromSeconds(8));
                    Assert.False(unknown.Shed);
                    Assert.Null(unknown.WorkflowExecutionId);
                    Assert.Null(unknown.RetryAfterSeconds);
                }
                finally { scripts.Released.TrySetResult(); }

                await shell.StopAsync(token);
                var unavailable = await InlineAsync(pure, HttpStatusCode.TooManyRequests, "Unavailable");
                Assert.True(unavailable.Shed);
                Assert.Null(unavailable.WorkflowExecutionId);

                async Task<HttpResponseMessage> ReadRunAsync(string run)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"/runtime/workflows/instances/{run}");
                    request.Headers.Add("X-Fixture-Subject", "accepted");
                    request.Headers.Add("X-Fixture-Tenant", Scope.Value);
                    request.Headers.Add("X-Fixture-Read", "yes");
                    request.Headers.Add("X-Fixture-Inspect", "yes");
                    return await http.SendAsync(request, token);
                }

                async Task<WorkflowExecutionStartDispatchView> InlineAsync(WorkflowExecutable executable, HttpStatusCode status,
                    string disposition, string subject = "accepted")
                {
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    while (true)
                    {
                        using var response = await ExecuteAsync(executable.Identity.ArtifactId, new
                        {
                            sourceReferenceId = WorkflowActivationReferenceIdentity.Create("publish-" + executable.Identity.DefinitionId)
                        }, subject: subject);
                        var receipt = Assert.IsType<WorkflowExecutionStartDispatchView>(await response.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>(token));
                        // A newly started owner can briefly have no partitions. Only definitive pre-queue responses can be retried.
                        if (status != HttpStatusCode.TooManyRequests && response.StatusCode == HttpStatusCode.TooManyRequests &&
                            receipt.PersistenceOutcome == "Unavailable" && System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(8))
                        {
                            await Task.Delay(100, token);
                            continue;
                        }
                        Assert.True(response.StatusCode == status, $"Inline Execute returned {response.StatusCode}: {await response.Content.ReadAsStringAsync(token)}");
                        Assert.Equal(disposition, receipt.PersistenceOutcome);
                        Assert.Equal(executable.Identity.ArtifactId, receipt.ArtifactId);
                        if (status == HttpStatusCode.GatewayTimeout) Assert.Null(response.Headers.RetryAfter);
                        if (status == HttpStatusCode.TooManyRequests)
                            Assert.Equal(TimeSpan.FromSeconds(receipt.RetryAfterSeconds!.Value), response.Headers.RetryAfter?.Delta);
                        return receipt;
                    }
                }
            }

            async Task<long> DurableRowCountAsync()
            {
                await using System.Data.Common.DbConnection db = directory is null
                    ? new Npgsql.NpgsqlConnection(foundation)
                    : new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "foundation.db"), Pooling = false }.ToString());
                await db.OpenAsync(token);
                await using var command = db.CreateCommand();
                command.CommandText = """
                    SELECT (SELECT count(*) FROM "Instances") + (SELECT count(*) FROM "FoundationRootBirths")
                       + (SELECT count(*) FROM "FoundationRootReceipts") + (SELECT count(*) FROM "FoundationPreparedRootStarts")
                       + (SELECT count(*) FROM "Journal")
                    """;
                return Convert.ToInt64(await command.ExecuteScalarAsync(token));
            }

            async Task<string> VerifyRecoveryAsync(WorkflowExecutable executable, string path, string? otherSelection = null)
            {
                expressions.IsAvailable = false;
                using var start = await ExecuteAsync(executable.Identity.ArtifactId, new { });
                Assert.Equal(HttpStatusCode.OK, start.StatusCode);
                var receipt = Assert.IsType<WorkflowExecutionStartDispatchView>(await start.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>(token));
                var actor = new Contract.ActorRef(Assert.IsType<string>(receipt.WorkflowExecutionId));
                var projected = new ProjectedRun(reads, bound, actor, executable.Identity.DefinitionId, Contract.History.Full);
                await projected.ReadAsync(Contract.ActorStatus.Running, parked: false, token);
                var waitRoute = "/fixture/workflows/" + path;
                using var first = await http.PostAsync(waitRoute, null, token);
                var park = await projected.ReadAsync(Contract.ActorStatus.Faulted, parked: true, token);
                using var forbidden = await RecoveryAsync(HttpMethod.Get, manage: false);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                using var hidden = await RecoveryAsync(HttpMethod.Get, tenant: "another-tenant");
                Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
                using var parked = await RecoveryAsync(HttpMethod.Get);
                Assert.Equal(HttpStatusCode.OK, parked.StatusCode);
                var status = await parked.Content.ReadFromJsonAsync<JsonElement>(token);
                Assert.True(status.GetProperty("status").GetString() == "Available",
                    $"Recovery status {status}; workflow HTTP {(int)first.StatusCode}: {await first.Content.ReadAsStringAsync(token)}");
                Assert.NotEmpty(status.GetProperty("token").GetString()!);
                var selected = Assert.IsType<string>(park.Selection);
                if (otherSelection is null)
                    await VerifyTenantReadIsolationAsync(http, reads, bound, actor, executable.Identity.DefinitionId, token);
                using var missingGrant = await InspectParkAsync(inspect: false);
                Assert.Equal(HttpStatusCode.NotFound, missingGrant.StatusCode);
                using var foreignEvidence = await InspectParkAsync(tenant: "another-tenant");
                Assert.Equal(HttpStatusCode.NotFound, foreignEvidence.StatusCode);
                using var metadataOnly = await InspectParkAsync();
                Assert.Equal(HttpStatusCode.OK, metadataOnly.StatusCode);
                Assert.Equal("no-store", metadataOnly.Headers.CacheControl?.ToString());
                var evidence = await metadataOnly.Content.ReadFromJsonAsync<JsonElement>(token);
                Assert.Equal(status.GetProperty("parkedAt").GetDateTimeOffset(), evidence.GetProperty("parkedAt").GetDateTimeOffset());
                Assert.Equal("The workflow stopped before its turn committed.", evidence.GetProperty("faultSummary").GetString());
                Assert.False(evidence.TryGetProperty("token", out _));
                foreach (var delivery in evidence.GetProperty("deliveries").EnumerateArray())
                    Assert.False(delivery.TryGetProperty("payload", out _));
                using var sensitiveEvidence = await InspectParkAsync(values: true);
                Assert.Equal(HttpStatusCode.OK, sensitiveEvidence.StatusCode);
                var sensitive = await sensitiveEvidence.Content.ReadFromJsonAsync<JsonElement>(token);
                Assert.Contains("fixture evaluator is unavailable", sensitive.GetProperty("faultSummary").GetString(), StringComparison.OrdinalIgnoreCase);
                using var malformedCursor = await InspectParkAsync(cursor: "invalid");
                Assert.Equal(HttpStatusCode.BadRequest, malformedCursor.StatusCode);
                using var denied = await RecoveryAsync(HttpMethod.Post, selection: selected, subject: "deny-park-recover");
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                using var stale = await RecoveryAsync(HttpMethod.Post, selection: otherSelection ?? "stale");
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                Assert.NotNull((await reads.GetAsync(bound, actor, token))?.Park);

                if (directory is not null && otherSelection is null)
                {
                    var pathToFoundation = Path.Combine(directory, "foundation.db");
                    var ingress = await Kernel.Foundation.Sqlite.SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(pathToFoundation,
                        Kernel.WorkflowActor.WorkflowActor.ActorType,
                        (scope, target, kind) => scope.Tenant == bound.Tenant && target == actor && kind == Contract.Kinds.Recover,
                        cancellationToken: token);
                    await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                        { DataSource = pathToFoundation, Pooling = false }.ToString());
                    await connection.OpenAsync(token);
                    await using var competingWriter = connection.BeginTransaction(deferred: false);
                    var refused = await ingress.SendAsync(bound, new Contract.Target.Ref(actor), "fixture-writer-held", Contract.Kinds.Recover,
                        JsonSerializer.Serialize(new Contract.OperatorCommand(selected, ExpectedTarget: actor)), cancellationToken: token);
                    Assert.Equal(Contract.AdmissionOutcome.Refused, refused.Outcome);
                    Assert.Equal(Contract.RefusalReason.NodeBusy, refused.Reason);
                    Assert.Equal(TimeSpan.FromSeconds(1), refused.RetryAfter);
                }

                using var accepted = await RecoveryAsync(HttpMethod.Post, selection: selected);
                Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
                await projected.ReadAsync(Contract.ActorStatus.Running, parked: false, token);
                using var noPark = await RecoveryAsync(HttpMethod.Get);
                Assert.Equal("NotParked", (await noPark.Content.ReadFromJsonAsync<JsonElement>(token)).GetProperty("status").GetString());
                using var noEvidence = await InspectParkAsync();
                Assert.Equal(HttpStatusCode.NotFound, noEvidence.StatusCode);
                using var retiredSelection = await RecoveryAsync(HttpMethod.Post, selection: selected);
                Assert.Equal(HttpStatusCode.Conflict, retiredSelection.StatusCode);

                using var repark = await http.PostAsync(waitRoute, null, token);
                using var parkedAgain = await RecoveryAsync(HttpMethod.Get);
                Assert.Equal(HttpStatusCode.OK, parkedAgain.StatusCode);
                var newPark = await parkedAgain.Content.ReadFromJsonAsync<JsonElement>(token);
                Assert.Equal("Available", newPark.GetProperty("status").GetString());
                var freshSelection = Assert.IsType<string>(newPark.GetProperty("token").GetString());
                using var previousPark = await RecoveryAsync(HttpMethod.Post, selection: selected);
                Assert.Equal(HttpStatusCode.Conflict, previousPark.StatusCode);
                Assert.NotNull((await reads.GetAsync(bound, actor, token))?.Park);
                expressions.IsAvailable = true;
                using var acceptedAgain = await RecoveryAsync(HttpMethod.Post, selection: freshSelection);
                Assert.Equal(HttpStatusCode.Accepted, acceptedAgain.StatusCode);
                while (await reads.GetAsync(bound, actor, token) is not { Park: null }) await Task.Delay(40, token);

                using var second = await http.PostAsync(waitRoute, null, token);
                Assert.Equal(HttpStatusCode.OK, second.StatusCode);
                Assert.Equal("recovered", await second.Content.ReadAsStringAsync(token));
                var ended = await projected.ReadAsync(Contract.ActorStatus.Finished, parked: false, token);
                var output = await Assert.IsAssignableFrom<Contract.IActorTerminalOutputReads>(reads)
                    .ReadTerminalOutputAsync(bound, actor, Assert.IsType<long>(ended.Header.EndedAt), includeValues: true, cancellationToken: token);
                using var values = JsonDocument.Parse(Assert.IsType<byte[]>(output?.Values));
                Assert.Equal("recovered", values.RootElement.GetProperty("result").GetString());
                return selected;

                async Task<HttpResponseMessage> InspectParkAsync(bool inspect = true, bool values = false, string? tenant = null,
                    string? cursor = null)
                {
                    var url = $"/runtime/workflows/instances/{actor.Id}/park";
                    if (cursor is not null) url += "?cursor=" + Uri.EscapeDataString(cursor);
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("X-Fixture-Subject", "park-reader");
                    request.Headers.Add("X-Fixture-Read", "yes");
                    if (inspect) request.Headers.Add("X-Fixture-Inspect", "yes");
                    if (values) request.Headers.Add("X-Fixture-Inspect-Values", "yes");
                    request.Headers.Add("X-Fixture-Tenant", tenant ?? Scope.Value);
                    return await http.SendAsync(request, token);
                }

                Task<HttpResponseMessage> RecoveryAsync(HttpMethod method, bool manage = true, string? tenant = null, string? selection = null,
                    string subject = "accepted") => RequestRecoveryAsync(actor, method, manage, tenant, selection, subject);
            }

            async Task<HttpResponseMessage> RequestRecoveryAsync(Contract.ActorRef actor, HttpMethod method, bool manage = true,
                string? tenant = null, string? selection = null, string subject = "accepted")
            {
                using var request = new HttpRequestMessage(method, $"/runtime/workflows/instances/{actor.Id}/recovery");
                if (method == HttpMethod.Post) request.Content = JsonContent.Create(new { token = selection });
                request.Headers.Add("X-Fixture-Subject", subject);
                if (manage) request.Headers.Add("X-Fixture-Manage", "yes");
                if (tenant is not null) request.Headers.Add("X-Fixture-Tenant", tenant);
                return await http.SendAsync(request, token);
            }

            async Task<string> StartWaitingAsync()
            {
                using var delayed = await ExecuteAsync(waiting.Identity.ArtifactId, new { });
                Assert.Equal(HttpStatusCode.OK, delayed.StatusCode);
                var receipt = Assert.IsType<WorkflowExecutionStartDispatchView>(await delayed.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>(token));
                Contract.ActorView? actor;
                do
                {
                    await Task.Delay(50, token);
                    actor = await reads.GetAsync(bound, new(Assert.IsType<string>(receipt.WorkflowExecutionId)), token);
                } while (actor is null);
                Assert.Equal(Contract.ActorStatus.Running, actor.Header.Status);
                return receipt.WorkflowExecutionId!;
            }

            async Task VerifyDeletedAsync(string run)
            {
                using var response = await DeleteAsync(run, manage: true);
                Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(token));
                Assert.Equal(new WorkflowInstanceDeletionView(run), await response.Content.ReadFromJsonAsync<WorkflowInstanceDeletionView>(token));
                Assert.Null(await reads.GetAsync(bound, new(run), token));
            }

            async Task<HttpResponseMessage> DeleteAsync(string run, bool manage, string? tenant = null, string subject = "accepted",
                string? tenantAlias = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"/runtime/workflows/instances/{run}");
                request.Headers.Add("X-Fixture-Subject", subject);
                if (manage) request.Headers.Add("X-Fixture-Manage", "yes");
                if (tenantAlias is not null) request.Headers.Add("X-Fixture-Tenant-Alias", tenantAlias);
                if (tenant is not null) request.Headers.Add("X-Fixture-Tenant", tenant);
                return await http.SendAsync(request, token);
            }

            async Task<HttpResponseMessage> ExecuteAsync(string artifact, object body, string? tenant = null, string subject = "accepted")
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/runtime/workflows/executables/{artifact}/execute")
                    { Content = JsonContent.Create(body) };
                request.Headers.Add("X-Fixture-Subject", subject);
                if (tenant is not null) request.Headers.Add("X-Fixture-Tenant", tenant);
                return await http.SendAsync(request, token);
            }

            async Task<string> VerifyAsync(HttpResponseMessage response, WorkflowExecutable source, WorkflowExecutableIdentity material, string slot,
                string message, string? seed = null)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var receipt = Assert.IsType<WorkflowExecutionStartDispatchView>(await response.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>(token));
                Assert.Equal(nameof(WorkflowExecutionCommandDispatchStatus.Accepted), receipt.CommandDispatchStatus);
                Assert.Equal(material.ArtifactId, receipt.ArtifactId);
                Assert.Equal(material.ArtifactHash, receipt.ArtifactHash);
                Assert.Equal(source.Identity.ArtifactVersion, receipt.ArtifactVersion);
                Assert.Equal("publish-" + source.Identity.DefinitionId, receipt.PublicationId);
                Assert.Equal(WorkflowActivationSlotIdentity.Create(source.Identity.DefinitionId, slot), receipt.SlotId);
                var actor = new Contract.ActorRef(Assert.IsType<string>(receipt.WorkflowExecutionId));
                Contract.ActorView? view;
                do
                {
                    await Task.Delay(50, token);
                    view = await reads.GetAsync(bound, actor, token);
                } while (view is null || view.Header.Status == Contract.ActorStatus.Running);
                Assert.Equal(Contract.ActorStatus.Finished, view.Header.Status);
                var selection = Assert.IsType<KernelExecutableSelection>(KernelExecutableSelection.Read(view.Header.Type, view.Header.TypeData));
                Assert.Equal(source.Identity.DefinitionId, selection.Definition);
                Assert.Equal(material, selection.Executable);
                var run = Kernel.WorkflowActor.WorkflowActor.DescribeRun(view.Header);
                Assert.Equal("accepted", run?.PublishedStart?.SubjectId);
                Assert.Equal(receipt.SourceReferenceId, run?.PublishedStart?.SourceReferenceId);
                var output = await Assert.IsAssignableFrom<Contract.IActorTerminalOutputReads>(reads).ReadTerminalOutputAsync(bound, actor,
                    view.Header.EndedAt!.Value, includeValues: true, cancellationToken: token);
                using var document = JsonDocument.Parse(output!.Values!);
                Assert.Equal(message, document.RootElement.GetProperty("message").GetString());
                if (seed is not null) Assert.Equal(seed, document.RootElement.GetProperty("seed").GetString());
                return actor.Id;
            }

            async Task VerifyEphemeralMappingRefusalAsync(WorkflowExecutable executable)
            {
                var before = await DurableRowCountAsync();
                string scopeId;
                await using (var authoring = await scopes.CreateAsyncScopeAsync(Scope))
                {
                    var (test, source, receipt) = await SubmitEphemeralTestAsync(authoring.ServiceProvider, executable);
                    scopeId = test.ScopeId;
                    Assert.Equal(WorkflowExecutionCommandDispatchStatus.Rejected, receipt.CommandDispatch.Status);
                    Assert.Contains(KernelHostCapabilities.DispatchUnavailable, receipt.CommandDispatch.Reason);
                    Assert.Equal(source.SourceReferenceId, receipt.PinnedSource?.SourceReferenceId);

                    // Publishing closes the scope for a rejected dispatch. Do not carry its presentation into the new scope.
                    var store = authoring.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>();
                    var now = DateTimeOffset.UtcNow;
                    await store.CloseAsync(new(scopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, now), token);
                    await store.CompleteAsync(scopeId, now, token);
                }

                await using var recovered = await scopes.CreateAsyncScopeAsync(Scope);
                var services = recovered.ServiceProvider;
                var durable = Assert.IsType<WorkflowTestScopeRecord>(await services.GetRequiredService<IWorkflowTestScopeStore>()
                    .FindAsync(scopeId, token));
                var persistedSource = Assert.IsType<WorkflowExecutableSourceReference>(await services
                    .GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(scopeId, token));
                Assert.Equal(WorkflowTestScopeState.Closed, durable.State);
                Assert.Null(durable.IsolatedActor);
                Assert.Equal(WorkflowTestRunInlineState.Refused, durable.InlineStart);
                Assert.Equal(WorkflowDirectTestRunObservation.Refused, await services.GetRequiredService<IWorkflowDirectTestRunDispatcher>()
                    .ObserveAsync(durable, persistedSource, token));
                Assert.Equal(before, await DurableRowCountAsync());
            }

            async Task VerifyEphemeralTestAsync(WorkflowExecutable executable, bool expectActor)
            {
                var isolated = app.Services.GetRequiredService<KernelTestRuns>().Reads;
                var production = app.Services.GetRequiredService<KernelRuns>();
                var before = await DurableRowCountAsync();
                await using var authoring = await scopes.CreateAsyncScopeAsync(Scope);
                var author = authoring.ServiceProvider;
                var scopeStore = author.GetRequiredService<IWorkflowTestScopeStore>();
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var (test, source, receipt) = await SubmitEphemeralTestAsync(author, executable);
                while (receipt.CommandDispatch.Metadata.GetValueOrDefault("elsa.kernel.start.persistence-outcome") == "Unavailable" &&
                    System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(8))
                {
                    // An owner may still be acquiring its partitions. A definitive shed response did not enter the queue.
                    await Task.Delay(100, token);
                    (test, source, receipt) = await SubmitEphemeralTestAsync(author, executable);
                }
                Assert.True(receipt.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted, receipt.CommandDispatch.Reason);
                Assert.Equal(source.SourceReferenceId, receipt.PinnedSource?.SourceReferenceId);
                var durable = Assert.IsType<WorkflowTestScopeRecord>(await scopeStore.FindAsync(test.ScopeId, token));
                if (!expectActor)
                {
                    Assert.True(receipt.TestRunCompletedWithoutRecord);
                    Assert.Null(durable.IsolatedActor);
                    Assert.Equal(WorkflowTestRunInlineState.CompletedWithoutRecord, durable.InlineStart);
                    Assert.Equal(before, await DurableRowCountAsync());
                    await using var recovered = await scopes.CreateAsyncScopeAsync(Scope);
                    var recoveredScope = await recovered.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>().FindAsync(test.ScopeId, token);
                    Assert.Equal(WorkflowDirectTestRunObservation.CompletedWithoutRecord,
                        await recovered.ServiceProvider.GetRequiredService<IWorkflowDirectTestRunDispatcher>()
                            .ObserveAsync(recoveredScope!, source, token));
                    return;
                }

                Assert.False(receipt.TestRunCompletedWithoutRecord);
                var actor = new Contract.ActorRef(receipt.WorkflowExecutionId);
                Assert.Equal(actor.Id, durable.IsolatedActor?.ActorReference);
                Assert.True((await production.GetAsync(actor, token)).Missing);
                Assert.Null(await author.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(actor.Id, token));
                var read = (await isolated.GetAsync(actor, token)).Run;
                Assert.NotNull(read);
                Assert.Equal(test.ScopeId, read.Value.Run.TestStart?.ScopeId);
                Assert.True(read.Value.Run.Ephemeral);
            }

            async Task VerifyEphemeralCancellationBeforeOfferAsync(WorkflowExecutable executable)
            {
                var before = await DurableRowCountAsync();
                WorkflowTestScope test;
                await using (var authoring = await scopes.CreateAsyncScopeAsync(Scope))
                {
                    var services = authoring.ServiceProvider;
                    (test, _, var request) = await PrepareEphemeralTestAsync(services, executable);
                    using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
                    scopeAdmission.Began = scope =>
                    {
                        Assert.Equal(test.ScopeId, scope.ScopeId);
                        caller.Cancel();
                    };
                    try
                    {
                        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<IWorkflowDirectTestRunDispatcher>()
                            .DispatchAsync(request, WorkflowExecutableReferenceScope.TestRun, cancellationToken: caller.Token).AsTask());
                        Assert.True(caller.IsCancellationRequested);
                    }
                    finally { scopeAdmission.Began = null; }
                }

                await using var recovered = await scopes.CreateAsyncScopeAsync(Scope);
                var recovery = recovered.ServiceProvider;
                var store = recovery.GetRequiredService<IWorkflowTestScopeStore>();
                var now = DateTimeOffset.UtcNow;
                await store.CloseAsync(new(test.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, now), token);
                await store.CompleteAsync(test.ScopeId, now, token);
                var durable = Assert.IsType<WorkflowTestScopeRecord>(await store.FindAsync(test.ScopeId, token));
                var source = Assert.IsType<WorkflowExecutableSourceReference>(await recovery
                    .GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(test.ScopeId, token));
                Assert.Equal(WorkflowTestScopeState.Closed, durable.State);
                Assert.Equal(WorkflowTestRunInlineState.Unavailable, durable.InlineStart);
                Assert.Null(durable.IsolatedActor);
                Assert.Equal(WorkflowDirectTestRunObservation.Unavailable, await recovery.GetRequiredService<IWorkflowDirectTestRunDispatcher>()
                    .ObserveAsync(durable, source, token));
                Assert.Equal(0, scripts.Calls);
                Assert.Equal(before, await DurableRowCountAsync());
            }

            async Task VerifyEphemeralCancellationAsync(WorkflowExecutable executable)
            {
                var before = await DurableRowCountAsync();
                WorkflowExecutionStartDispatchRequest request;
                WorkflowTestScope test;
                try
                {
                    await using (var authoring = await scopes.CreateAsyncScopeAsync(Scope))
                    {
                        var services = authoring.ServiceProvider;
                        (test, _, request) = await PrepareEphemeralTestAsync(services, executable);
                        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
                        var start = services.GetRequiredService<IWorkflowDirectTestRunDispatcher>()
                            .DispatchAsync(request, WorkflowExecutableReferenceScope.TestRun, cancellationToken: caller.Token).AsTask();
                        await scripts.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                        Assert.False(start.IsCompleted);
                        caller.Cancel();
                        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
                    }

                    await using var cleanup = await scopes.CreateAsyncScopeAsync(Scope);
                    var durable = cleanup.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>();
                    var duplicate = await cleanup.ServiceProvider.GetRequiredService<IWorkflowDirectTestRunDispatcher>()
                        .DispatchAsync(request, WorkflowExecutableReferenceScope.TestRun, cancellationToken: token);
                    Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, duplicate.CommandDispatch.Status);
                    Assert.Equal("InlineStartOutcomeUnknown", duplicate.CommandDispatch.Reason);
                    Assert.Equal(WorkflowTestRunInlineState.Pending, (await durable.FindAsync(test.ScopeId, token))?.InlineStart);

                    // The Publishing error path may close a cancelled request's scope before the owner finishes it.
                    var now = DateTimeOffset.UtcNow;
                    await durable.CloseAsync(new(test.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, now), token);
                    await durable.CompleteAsync(test.ScopeId, now, token);
                }
                finally { scripts.Released.TrySetResult(); }

                await using var recovered = await scopes.CreateAsyncScopeAsync(Scope);
                var recoveredServices = recovered.ServiceProvider;
                var scopeStore = recoveredServices.GetRequiredService<IWorkflowTestScopeStore>();
                using var settle = CancellationTokenSource.CreateLinkedTokenSource(token);
                settle.CancelAfter(TimeSpan.FromSeconds(8));
                WorkflowTestScopeRecord restored;
                do
                {
                    restored = Assert.IsType<WorkflowTestScopeRecord>(await scopeStore.FindAsync(test.ScopeId, settle.Token));
                    if (restored.InlineStart == WorkflowTestRunInlineState.CompletedWithoutRecord) break;
                    await Task.Delay(40, settle.Token);
                } while (true);
                Assert.Equal(WorkflowTestScopeState.Closed, restored.State);
                Assert.Null(restored.IsolatedActor);
                var source = Assert.IsType<WorkflowExecutableSourceReference>(await recoveredServices
                    .GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(test.ScopeId, token));
                Assert.Equal(WorkflowDirectTestRunObservation.CompletedWithoutRecord, await recoveredServices
                    .GetRequiredService<IWorkflowDirectTestRunDispatcher>().ObserveAsync(restored, source, token));
                Assert.Equal(1, scripts.Calls);
                Assert.Equal(before, await DurableRowCountAsync());
            }

            async Task VerifyInterruptedStopRejoinsSettlementsAsync(WorkflowExecutable executable)
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<WorkflowExecutionStartDispatchResult> started;
                Task rejoined;
                await using var authoring = await scopes.CreateAsyncScopeAsync(Scope);
                var services = authoring.ServiceProvider;
                var (test, _, request) = await PrepareEphemeralTestAsync(services, executable);
                scopeAdmission.Settling = (scope, outcome, budget) =>
                {
                    Assert.Equal(test.ScopeId, scope.ScopeId);
                    Assert.Equal(WorkflowTestRunInlineState.CompletedWithoutRecord, outcome);
                    entered.TrySetResult();
                    return release.Task.WaitAsync(budget);
                };
                try
                {
                    started = services.GetRequiredService<IWorkflowDirectTestRunDispatcher>()
                        .DispatchAsync(request, WorkflowExecutableReferenceScope.TestRun, cancellationToken: token).AsTask();
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    using var interrupted = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shell.StopAsync(interrupted.Token));
                    rejoined = shell.StopAsync(token);
                    await Assert.ThrowsAsync<TimeoutException>(() => rejoined.WaitAsync(TimeSpan.FromMilliseconds(100), token));
                }
                finally
                {
                    scopeAdmission.Settling = null;
                    release.TrySetResult();
                }

                await rejoined;
                Assert.True((await started).TestRunCompletedWithoutRecord);
                await using var recovered = await scopes.CreateAsyncScopeAsync(Scope);
                var durable = await recovered.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>().FindAsync(test.ScopeId, token);
                Assert.Equal(WorkflowTestRunInlineState.CompletedWithoutRecord, durable?.InlineStart);
                Assert.Null(durable?.IsolatedActor);
            }

            async Task<(WorkflowTestScope Test, WorkflowExecutableSourceReference Source, WorkflowExecutionStartDispatchResult Receipt)>
                SubmitEphemeralTestAsync(IServiceProvider author, WorkflowExecutable executable)
            {
                var (test, source, start) = await PrepareEphemeralTestAsync(author, executable);
                var dispatcher = author.GetRequiredService<IWorkflowDirectTestRunDispatcher>();
                return (test, source, await dispatcher.DispatchAsync(start, WorkflowExecutableReferenceScope.TestRun, cancellationToken: token));
            }

            async Task<(WorkflowTestScope Test, WorkflowExecutableSourceReference Source, WorkflowExecutionStartDispatchRequest Request)>
                PrepareEphemeralTestAsync(IServiceProvider author, WorkflowExecutable executable)
            {
                var now = DateTimeOffset.UtcNow;
                var selected = new WorkflowTestScope(Guid.NewGuid().ToString("N"), now.AddMinutes(30), Scope.Value, new(Scope.Value));
                await author.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(selected, now, token);
                var id = executable.Identity;
                var reference = new WorkflowExecutableSourceReference(selected.ScopeId, id.ArtifactId, "workflow-draft-snapshot", "draft:witness",
                    id.ArtifactVersion, id.DefinitionId, id.DefinitionVersionId, id.ArtifactVersion, now, null,
                    WorkflowExecutableReferenceScope.TestRun, ExpiresAt: selected.ExpiresAt, TenantId: selected.TenantId);
                await author.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference, token);
                var start = new WorkflowExecutionStartDispatchRequest(id.ArtifactId, "workflow-designer-test-run",
                    workflowExecutionId: null, idempotencyKey: null, metadata: null, variables: null, inputs: null,
                    stimulusInput: null, triggerNodeId: null, runKind: WorkflowRunKind.TestRun,
                    sourceSelection: new(sourceReferenceId: reference.SourceReferenceId),
                    provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                    parentWorkflowExecutionId: null, correlationId: null, tenantId: Scope.Value, partition: selected.Partition,
                    authority: WorkflowExecutionAuthoritySnapshot.CreateRoot("workflow-designer-test-run",
                        new Dictionary<string, string> { ["elsa.identity.subject-id"] = "accepted", ["elsa.identity.subject-kind"] = "persisted-user" }),
                    startAuthority: null, dispatchNestingDepth: 0, testScope: selected);
                return (selected, reference, start);
            }

            async Task VerifyDirectTestAsync(WorkflowExecutable executable, string sourceKind, string version, bool closeBeforePin,
                bool stop = false, bool expire = false, bool interruptAfterPin = false)
            {
                var privateRuns = app.Services.GetRequiredService<KernelTestRuns>().Reads;
                Assert.Throws<InvalidOperationException>(() => EmbeddedTenant.Bind(privateRuns.Tenant.Tenant));
                var real = app.Services.GetRequiredService<KernelRuns>();
                Assert.NotEqual(real.Tenant.Tenant, privateRuns.Tenant.Tenant);

                await using var authoring = await scopes.CreateAsyncScopeAsync(Scope);
                var author = authoring.ServiceProvider;
                var now = DateTimeOffset.UtcNow;
                var test = new WorkflowTestScope(Guid.NewGuid().ToString("N"), now.AddMinutes(30), Scope.Value, new(Scope.Value));
                var scopeStore = author.GetRequiredService<IWorkflowTestScopeStore>();
                await scopeStore.CreateAsync(test, now, token);
                var id = executable.Identity;
                var source = new WorkflowExecutableSourceReference(test.ScopeId, id.ArtifactId, sourceKind, version, id.ArtifactVersion,
                    id.DefinitionId, version, id.ArtifactVersion, now, null, WorkflowExecutableReferenceScope.TestRun,
                    ExpiresAt: test.ExpiresAt, TenantId: test.TenantId);
                await author.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(source, token);
                pinBoundary.BeforeTestAdmission = closeBeforePin ? async (_, cancellation) =>
                {
                    await using var competing = await scopes.CreateAsyncScopeAsync(Scope);
                    await competing.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>().CloseAsync(
                        new(test.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, DateTimeOffset.UtcNow), cancellation);
                } : null;

                var start = new WorkflowExecutionStartDispatchRequest(id.ArtifactId, "workflow-designer-test-run",
                    workflowExecutionId: null, idempotencyKey: null, metadata: null, variables: null,
                    inputs: new Dictionary<string, object?> { ["message"] = "direct-authoring-result" },
                    stimulusInput: null, triggerNodeId: null, runKind: WorkflowRunKind.TestRun,
                    sourceSelection: new(sourceReferenceId: source.SourceReferenceId),
                    provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                    parentWorkflowExecutionId: null, correlationId: null, tenantId: Scope.Value, partition: test.Partition,
                    authority: WorkflowExecutionAuthoritySnapshot.CreateRoot("workflow-designer-test-run",
                        new Dictionary<string, string> { ["elsa.identity.subject-id"] = "accepted", ["elsa.identity.subject-kind"] = "persisted-user" }),
                    startAuthority: null, dispatchNestingDepth: 0, testScope: test);
                var dispatcher = author.GetRequiredService<IWorkflowDirectTestRunDispatcher>();
                var interrupted = new TaskCompletionSource<RuntimeKernelExecutablePin>();
                if (interruptAfterPin)
                    pinBoundary.AfterTestAdmission = async (pin, cancellation) =>
                    {
                        interrupted.SetResult(pin);
                        var oldActor = new Contract.ActorRef(pin.ActorReference);
                        Assert.Equal(RootStartProof.Unresolved, await shell.ConfirmPreparedTestAsync(oldActor, pin.Issuance, cancellation));
                        await using var competing = await scopes.CreateAsyncScopeAsync(Scope);
                        var duplicate = await competing.ServiceProvider.GetRequiredService<IWorkflowDirectTestRunDispatcher>()
                            .DispatchAsync(start, WorkflowExecutableReferenceScope.TestRun, cancellationToken: cancellation);
                        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Rejected, duplicate.CommandDispatch.Status);
                        Assert.True((await privateRuns.GetAsync(oldActor, cancellation)).Missing);
                        return false;
                    };
                var result = await dispatcher.DispatchAsync(start, WorkflowExecutableReferenceScope.TestRun, cancellationToken: token);
                if (interruptAfterPin)
                {
                    pinBoundary.AfterTestAdmission = null;
                    Assert.Equal(WorkflowExecutionCommandDispatchStatus.Rejected, result.CommandDispatch.Status);
                    var abandoned = await interrupted.Task.WaitAsync(token);
                    var oldActor = new Contract.ActorRef(abandoned.ActorReference);
                    Assert.Equal(RootStartProof.PreparationDisarmed, await shell.ConfirmPreparedTestAsync(oldActor, abandoned.Issuance, token));
                    Assert.Equal(new WorkflowTestRunActorBinding(abandoned.ActorReference, abandoned.Issuance),
                        (await scopeStore.FindAsync(test.ScopeId, token))!.IsolatedActor);

                    result = await dispatcher.DispatchAsync(start, WorkflowExecutableReferenceScope.TestRun, cancellationToken: token);
                    Assert.NotEqual(oldActor.Id, result.WorkflowExecutionId);
                    Assert.Equal(result.WorkflowExecutionId, (await scopeStore.FindAsync(test.ScopeId, token))!.IsolatedActor?.ActorReference);
                    var replay = await dispatcher.DispatchAsync(start, WorkflowExecutableReferenceScope.TestRun, cancellationToken: token);
                    Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, replay.CommandDispatch.Status);
                    Assert.Equal(result.WorkflowExecutionId, replay.WorkflowExecutionId);
                    Assert.True((await privateRuns.GetAsync(oldActor, token)).Missing);
                }
                if (closeBeforePin)
                {
                    Assert.Equal(WorkflowExecutionCommandDispatchStatus.Rejected, result.CommandDispatch.Status);
                    return;
                }
                Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
                Assert.Equal(source.SourceReferenceId, result.PinnedSource?.SourceReferenceId);
                var actor = new Contract.ActorRef(result.WorkflowExecutionId);
                isolatedActors.Add(actor);
                if (stop || expire)
                {
                    while ((await privateRuns.TailAsync(actor, cancellationToken: token)).JournalHeadRevision is not > 0)
                        await Task.Delay(40, token);
                    Assert.Equal(Contract.ActorStatus.Running, (await privateRuns.GetAsync(actor, token)).Run?.View.Header.Status);
                    if (expire) return;
                    await Assert.ThrowsAsync<Core.Exceptions.TestScopeAdmissionException>(() =>
                        dispatcher.RequestStopAsync(test, actor.Id, token).AsTask());
                    await scopeStore.CloseAsync(new(test.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, DateTimeOffset.UtcNow), token);
                    await dispatcher.RequestStopAsync(test, actor.Id, token);
                    Assert.True(await dispatcher.IsStoppedAsync(test, actor.Id, token));
                    Assert.Equal(Contract.ActorStatus.Cancelled, (await privateRuns.GetAsync(actor, token)).Run?.View.Header.Status);
                    Assert.True((await real.GetAsync(actor, token)).Missing);
                    Assert.Null(await author.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(actor.Id, token));
                    return;
                }
                Contract.ActorView? view;
                do
                {
                    await Task.Delay(50, token);
                    view = (await privateRuns.GetAsync(actor, token)).Run?.View;
                } while (view is null || view.Header.Status == Contract.ActorStatus.Running);
                Assert.Equal(Contract.ActorStatus.Finished, view.Header.Status);
                Assert.True((await real.GetAsync(actor, token)).Missing);
                Assert.Null(await author.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(actor.Id, token));
                using var inspect = new HttpRequestMessage(HttpMethod.Get, $"/runtime/workflows/instances/{actor.Id}");
                inspect.Headers.Add("X-Fixture-Subject", "accepted");
                inspect.Headers.Add("X-Fixture-Tenant", Scope.Value);
                inspect.Headers.Add("X-Fixture-Inspect", "yes");
                inspect.Headers.Add("X-Fixture-Read", "yes");
                inspect.Headers.Add("X-Fixture-Inspect-Values", "yes");
                using var response = await http.SendAsync(inspect, token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var details = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                Assert.Equal(nameof(WorkflowRunKind.TestRun), details.RootElement.GetProperty("instance").GetProperty("runKind").GetString());
                Assert.Equal("direct-authoring-result", details.RootElement.GetProperty("outputs").GetProperty("message").GetProperty("value").GetString());
                using var foreign = new HttpRequestMessage(HttpMethod.Get, $"/runtime/workflows/instances/{actor.Id}");
                foreign.Headers.Add("X-Fixture-Subject", "accepted");
                foreign.Headers.Add("X-Fixture-Tenant", "another-tenant");
                foreign.Headers.Add("X-Fixture-Inspect", "yes");
                foreign.Headers.Add("X-Fixture-Read", "yes");
                using var hidden = await http.SendAsync(foreign, token);
                Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
                using var production = new HttpRequestMessage(HttpMethod.Get, "/runtime/workflows/instances/page");
                production.Headers.Add("X-Fixture-Subject", "accepted");
                production.Headers.Add("X-Fixture-Tenant", Scope.Value);
                production.Headers.Add("X-Fixture-Inspect", "yes");
                production.Headers.Add("X-Fixture-Read", "yes");
                using var listed = await http.SendAsync(production, token);
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                using var list = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(token));
                Assert.Empty(list.RootElement.GetProperty("items").EnumerateArray());
            }

            async Task VerifyExpiryAsync()
            {
                var privateRuns = app.Services.GetRequiredService<KernelTestRuns>().Reads;
                var storage = app.Services.GetRequiredService<KernelShellStorage>();
                var logger = app.Services.GetRequiredService<ILogger<KernelDirectTestRunRetirement>>();
                var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddMinutes(31));
                var cleanup = new KernelDirectTestRunRetirement(privateRuns.Tenant, await shell.ReadAsync(token),
                    storage.OpenOperatorsAsync, clock, logger);
                while (!(await Task.WhenAll(isolatedActors.Select(async actor =>
                    (await privateRuns.GetAsync(actor, token)).Missing))).All(missing => missing))
                {
                    await cleanup.SweepPageAsync(null, token);
                    await Task.Delay(80, token);
                }

                var pins = stores.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                while ((await pins.ListPinnedExecutableArtifactIdsAsync(token)).Count > 0)
                    await Task.Delay(80, token);
            }
        }
        finally { await shell.StopAsync(CancellationToken.None); }

        async Task PublishAsync(WorkflowExecutable executable, string slot)
        {
            var id = executable.Identity;
            var now = DateTimeOffset.UtcNow;
            var source = new WorkflowExecutableSourceReference("candidate", id.ArtifactId, "definition", id.DefinitionId, id.ArtifactVersion,
                id.DefinitionId, id.DefinitionVersionId, id.ArtifactVersion, now, now, WorkflowExecutableReferenceScope.Published);
            var coordinator = stores.GetRequiredService<IWorkflowActivationCoordinator>();
            var result = await coordinator.ActivateAsync(new(executable, source, slot, "publish-" + id.DefinitionId,
                WorkflowActivationSource.Publishing, 0), token);
            Assert.True(result.Succeeded, result.Diagnostic);
        }
    }

    private static ExecutableNode[] RecoveryNodes(string path) =>
    [
        Node("wait", Types.HttpEndpoint,
            [Literal("Path", path, "String"), Literal("SupportedMethods", new[] { "POST" }), Literal("CanStartWorkflow", false, "Boolean")]),
        SetOutput("output", "result", Expression("value", "JavaScript", "'recovered'", "String")),
        Node("reply", Types.WriteHttpResponse, [Literal("Body", "recovered", "String")])
    ];

    private sealed class FixtureExpressions : IPortableExpressionEvaluator
    {
        public volatile bool IsAvailable;

        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => IsAvailable
            ? ValueTask.FromResult(JsonSerializer.SerializeToElement("recovered"))
            : throw new Kernel.Native.NativeExpressionsUnavailableException("The fixture evaluator is unavailable.");
    }

    private sealed class BlockingScript : IJavaScriptScriptEvaluator
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<JsonElement?> EvaluateAsync(JavaScriptScriptEvaluationRequest request)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            await Released.Task.WaitAsync(request.CancellationToken);
            return JsonSerializer.SerializeToElement(1);
        }
    }

    private static WorkflowExecutable Ephemeral(WorkflowExecutable executable) => new(executable.Identity, executable.RootActivity,
        executable.ResumeTargets, executable.CreatedAt, executable.CompatibilityMetadata, executable.InputContract, executable.Dependencies,
        executable.RuntimeRequirements, executable.StorageDriverRequirements, executable.IncidentStrategy,
        persistenceMode: PersistenceMode.Ephemeral, workflowVariables: executable.WorkflowVariables);

    private static WorkflowExecutable PublishedAs(WorkflowExecutable canonical, string definition, string version) =>
        new(canonical.Identity with { DefinitionId = definition, DefinitionVersionId = definition + "-v" + version, ArtifactVersion = version },
            canonical.RootActivity, canonical.ResumeTargets, canonical.CreatedAt, canonical.CompatibilityMetadata,
            canonical.InputContract, canonical.Dependencies, canonical.RuntimeRequirements, canonical.StorageDriverRequirements, canonical.IncidentStrategy,
            workflowVariables: canonical.WorkflowVariables);

    internal sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new(IdentityClaimTypes.UserId, Request.Headers["X-Fixture-Subject"].ToString()),
                new(IdentityClaimTypes.Permission, "workflows/runtime:execute"),
                new(IdentityClaimTypes.Normalized, "v1")
            };
            if (Request.Headers.ContainsKey("X-Fixture-Manage")) claims.Add(new(IdentityClaimTypes.Permission, "workflows/runtime:manage"));
            if (Request.Headers.ContainsKey("X-Fixture-Read"))
                claims.Add(new(IdentityClaimTypes.Permission, WorkflowRuntimePermissions.WorkflowRuntimeRead));
            if (Request.Headers.ContainsKey("X-Fixture-Inspect"))
                claims.Add(new(IdentityClaimTypes.Permission, HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission));
            if (Request.Headers.ContainsKey("X-Fixture-Inspect-Values"))
                claims.Add(new(IdentityClaimTypes.Permission, HttpContextActivityExecutionInspectionAuthorizationContext.SensitiveValuesPermission));
            if (Request.Headers.TryGetValue("X-Fixture-Tenant", out var tenant)) claims.Add(new(IdentityClaimTypes.TenantId, tenant.ToString()));
            if (Request.Headers.TryGetValue("X-Fixture-Tenant-Alias", out var alias)) claims.Add(new("tenant_id", alias.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class FixtureRunPolicy : IPermissionResourceHandler
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            var deny = (context.Resource, context.Principal.FindFirstValue(IdentityClaimTypes.UserId)) is
                (WorkflowExecutionState { Status: WorkflowExecutionStatus.Cancelled }, "deny-terminal-purge") or
                (WorkflowExecutionState { Status: WorkflowExecutionStatus.Faulted }, "deny-park-recover");
            return ValueTask.FromResult<PermissionEvaluationResult?>(deny ? PermissionEvaluationResult.Denied() : null);
        }
    }

    internal sealed class FixturePolicy(WorkflowRunKind expected = WorkflowRunKind.PublishedRun) : IWorkflowExecutableStartPolicy
    {
        public ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(WorkflowExecutableStartPolicyContext context, CancellationToken cancellationToken)
        {
            Assert.Equal(expected, context.RunKind);
            Assert.Equal(WorkflowExecutableStartAuthorityKind.LiveReference, context.AuthorityKind);
            Assert.Equal(Scope.Value, context.Partition?.Value);
            var subject = context.Authority.Metadata["elsa.identity.subject-id"];
            return ValueTask.FromResult(subject == "deny" ? WorkflowExecutableStartDecision.Deny("denied", "Denied by fixture") :
                WorkflowExecutableStartDecision.Allow());
        }
    }

    private sealed class FixtureTestScopeAdmission
    {
        public Action<WorkflowTestScope>? Began { get; set; }
        public Func<WorkflowTestScope, WorkflowTestRunInlineState, CancellationToken, Task>? Settling { get; set; }
    }

    private sealed class ObservedTestScopeAdmission(IWorkflowTestScopeAdmissionStore physical, FixtureTestScopeAdmission fixture)
        : IWorkflowTestScopeAdmissionStore
    {
        public ValueTask AssertOpenAsync(WorkflowTestScope scope, DateTimeOffset observedAt, CancellationToken cancellationToken) =>
            physical.AssertOpenAsync(scope, observedAt, cancellationToken);

        public ValueTask AdmitIsolatedActorAsync(WorkflowTestScope scope, string actorReference, Func<DateTimeOffset> readClock,
            CancellationToken cancellationToken) => physical.AdmitIsolatedActorAsync(scope, actorReference, readClock, cancellationToken);

        public async ValueTask<bool> BeginInlineStartAsync(WorkflowTestScope scope, Func<DateTimeOffset> readClock,
            CancellationToken cancellationToken)
        {
            var began = await physical.BeginInlineStartAsync(scope, readClock, cancellationToken);
            if (began) fixture.Began?.Invoke(scope);
            return began;
        }

        public async ValueTask<bool> SettleInlineStartAsync(WorkflowTestScope scope, WorkflowTestRunInlineState outcome,
            CancellationToken cancellationToken)
        {
            if (fixture.Settling is { } settling) await settling(scope, outcome, cancellationToken);
            return await physical.SettleInlineStartAsync(scope, outcome, cancellationToken);
        }
    }

    private sealed class PinBoundary
    {
        public Func<WorkflowExecutableSourceReference, CancellationToken, ValueTask>? BeforeAdmission { get; set; }
        public Func<WorkflowExecutableSourceReference, CancellationToken, ValueTask>? BeforeTestAdmission { get; set; }
        public Func<RuntimeKernelExecutablePin, CancellationToken, ValueTask<bool>>? AfterTestAdmission { get; set; }
        public bool Retracted { get; set; }
    }

    private sealed class FixturePins(IRuntimeKernelExecutablePinStore inner, PinBoundary boundary) : IRuntimeKernelExecutablePinStore
    {
        public async ValueTask<bool> TryPinPublishedAsync(RuntimeKernelExecutablePin pin, WorkflowActivationSlot slot,
            WorkflowExecutableSourceReference source, CancellationToken token)
        {
            if (boundary.BeforeAdmission is { } before) await before(source, token);
            return await inner.TryPinPublishedAsync(pin, slot, source, token);
        }

        public ValueTask<bool> TryPinAsync(IReadOnlyCollection<RuntimeKernelExecutablePin> pins, CancellationToken token) => inner.TryPinAsync(pins, token);
        public async ValueTask<bool> TryPinTestRunAsync(RuntimeKernelExecutablePin pin, WorkflowTestScope scope,
            WorkflowExecutableSourceReference source, WorkflowTestRunActorBinding? observedBinding, CancellationToken token)
        {
            if (boundary.BeforeTestAdmission is { } before) await before(source, token);
            if (!await inner.TryPinTestRunAsync(pin, scope, source, observedBinding, token)) return false;
            return boundary.AfterTestAdmission is not { } after || await after(pin, token);
        }
        public ValueTask<string?> ReadOpenTailAsync(PersistenceScope scope, CancellationToken token) => inner.ReadOpenTailAsync(scope, token);
        public ValueTask<RuntimeKernelExecutablePinPage> ListOpenAsync(PersistenceScope scope, string? after, string through, int limit,
            CancellationToken token) => inner.ListOpenAsync(scope, after, through, limit, token);
        public ValueTask<bool> TryReleaseAsync(RuntimeKernelExecutablePin pin,
            Func<RuntimeKernelExecutablePin, CancellationToken, ValueTask<RuntimeKernelExecutablePinProof>> confirmAsync,
            CancellationToken token) => inner.TryReleaseAsync(pin, confirmAsync, token);
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken token) =>
            inner.ListPinnedExecutableArtifactIdsAsync(token);
    }

    internal sealed class FixtureTypes : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = "String"; return type == typeof(string); }
        public bool TryGetType(string alias, out Type type) { type = typeof(string); return alias == "String"; }
        public IEnumerable<Type> ListTypes() => [typeof(string)];
        public string GetAliasOrDefault(Type type) => type == typeof(string) ? "String" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => alias == "String" ? typeof(string) : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => TryGetType(alias, out type);
    }
}
