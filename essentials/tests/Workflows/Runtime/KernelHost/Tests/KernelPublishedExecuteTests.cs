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
using Elsa.Workflows.Runtime.Api.Requests;
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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class KernelPublishedExecuteTests
{
    private const string FoundationDatabase = "KERNEL_EXECUTE_FOUNDATION_POSTGRES";
    private const string RuntimeDatabase = "KERNEL_EXECUTE_RUNTIME_POSTGRES";
    private static readonly PersistenceScope Scope = new(PersistenceScope.DefaultValue);

    [Fact]
    public Task Sqlite_standard_execute_admits_published_native_and_flowchart() => RunAsync(false);

    [SkippableFact]
    public Task PostgreSql_standard_execute_admits_published_native_and_flowchart()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true);
    }

    [Fact]
    public Task Sqlite_private_ephemeral_execute_reports_volatile_completion_and_committed_promotion() => RunAsync(false, inline: true);

    [SkippableFact]
    public Task PostgreSql_private_ephemeral_execute_reports_volatile_completion_and_committed_promotion()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true, inline: true);
    }

    private static async Task RunAsync(bool postgres, bool inline = false)
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
            await RunAsync(postgres ? "PostgreSql" : "Sqlite", directory?.FullName, foundation, runtime, inline, deadline.Token);
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunAsync(string provider, string? directory, string? foundation, string runtime, bool inline, CancellationToken token)
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

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddElsaEndpoints();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication("KernelExecuteFixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("KernelExecuteFixture", _ => { });
        builder.Services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
            { "KernelExecuteFixture" });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IWorkflowExecutableStartPolicy, FixturePolicy>();
        builder.Services.AddSingleton<IPermissionResourceHandler, FixtureRunPolicy>();
        builder.Services.AddSingleton<IWellKnownTypeRegistry, FixtureTypes>();
        builder.Services.AddSingleton(new ShellSettings(new ShellId("default")));
        var expressions = new FixtureExpressions();
        builder.Services.AddSingleton<IPortableExpressionEvaluator>(expressions);
        var scripts = new BlockingScript();
        if (inline) builder.Services.AddSingleton<IJavaScriptScriptEvaluator>(scripts);
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
        var kernelHosting = new WorkflowsRuntimeKernelHostingFeature
        {
            Provider = provider,
            SqlitePath = directory is null ? null : Path.Combine(directory, "foundation.db"),
            PostgresConnectionString = foundation
        };
        if (inline)
            kernelHosting.ConfigureServices(builder.Services, KernelShellStorage.CreateInlineStartsQualification(provider,
                kernelHosting.SqlitePath, foundation));
        else kernelHosting.ConfigureServices(builder.Services);
        new WorkflowsRuntimeKernelFeature().PostConfigureServices(builder.Services);
        api.PostConfigureServices(builder.Services);
        var kernelApi = new WorkflowsRuntimeKernelApiFeature();
        kernelApi.PostConfigureServices(builder.Services);

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
            if (inline)
            {
                await VerifyPrivateInlineAsync();
                return;
            }
            using (var excluded = await ExecuteAsync(pure.Identity.ArtifactId, new { }))
            {
                Assert.Equal(HttpStatusCode.Conflict, excluded.StatusCode);
                var receipt = Assert.IsType<WorkflowExecutionStartDispatchView>(await excluded.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>(token));
                Assert.Equal("EphemeralPublishedStartUnavailable", receipt.Reason);
                Assert.Null(receipt.PersistenceOutcome);
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

            async Task VerifyPrivateInlineAsync()
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

                var rateLimited = new KernelPublishedRunDispatcher(scopes, app.Services.GetRequiredService<KernelServing>(), Scope,
                    shell.StartPublishedAsync, (_, _, _, _) => ValueTask.FromResult(new InlineStartOutcome(InlineStartDisposition.Unavailable)
                    {
                        RefusalReason = Contract.RefusalReason.RateLimited, RetryAfter = TimeSpan.FromMilliseconds(1250)
                    }));
                var rateReceipt = await new WorkflowExecutionStartService(rateLimited, stores.GetRequiredService<IWorkflowExecutableStore>())
                    .ExecuteAsync(new ExecuteWorkflow(pure.Identity.ArtifactId,
                        SourceReferenceId: WorkflowActivationReferenceIdentity.Create("publish-" + pure.Identity.DefinitionId)), token);
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

                async Task<WorkflowExecutionStartDispatchView> InlineAsync(WorkflowExecutable executable, HttpStatusCode status, string disposition)
                {
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    while (true)
                    {
                        using var response = await ExecuteAsync(executable.Identity.ArtifactId, new
                        {
                            sourceReferenceId = WorkflowActivationReferenceIdentity.Create("publish-" + executable.Identity.DefinitionId)
                        });
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
                while ((await reads.TailAsync(bound, actor, cancellationToken: token)).JournalHeadRevision is not > 0)
                    await Task.Delay(40, token);
                var waitRoute = "/fixture/workflows/" + path;
                using var first = await http.PostAsync(waitRoute, null, token);
                var route = $"/runtime/workflows/instances/{actor.Id}/recovery";

                using var forbidden = await RecoveryAsync(HttpMethod.Get, manage: false);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                using var hidden = await RecoveryAsync(HttpMethod.Get, tenant: "another-tenant");
                Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
                using var parked = await RecoveryAsync(HttpMethod.Get);
                Assert.Equal(HttpStatusCode.OK, parked.StatusCode);
                var status = await parked.Content.ReadFromJsonAsync<JsonElement>(token);
                Assert.True(status.GetProperty("status").GetString() == "Available",
                    $"Recovery status {status}; workflow HTTP {(int)first.StatusCode}: {await first.Content.ReadAsStringAsync(token)}");
                var selected = Assert.IsType<string>(status.GetProperty("token").GetString());
                Assert.NotEmpty(selected);
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
                while (await reads.GetAsync(bound, actor, token) is not { Park: null }) await Task.Delay(40, token);
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
                var ended = Assert.IsType<Contract.ActorView>(await reads.GetAsync(bound, actor, token));
                Assert.Equal(Contract.ActorStatus.Finished, ended.Header.Status);
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

                async Task<HttpResponseMessage> RecoveryAsync(HttpMethod method, bool manage = true, string? tenant = null, string? selection = null,
                    string subject = "accepted")
                {
                    using var request = new HttpRequestMessage(method, route);
                    if (method == HttpMethod.Post) request.Content = JsonContent.Create(new { token = selection });
                    request.Headers.Add("X-Fixture-Subject", subject);
                    if (manage) request.Headers.Add("X-Fixture-Manage", "yes");
                    if (tenant is not null) request.Headers.Add("X-Fixture-Tenant", tenant);
                    return await http.SendAsync(request, token);
                }
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
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<JsonElement?> EvaluateAsync(JavaScriptScriptEvaluationRequest request)
        {
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

    internal sealed class FixturePolicy : IWorkflowExecutableStartPolicy
    {
        public ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(WorkflowExecutableStartPolicyContext context, CancellationToken cancellationToken)
        {
            Assert.Equal(WorkflowRunKind.PublishedRun, context.RunKind);
            Assert.Equal(WorkflowExecutableStartAuthorityKind.LiveReference, context.AuthorityKind);
            Assert.Equal(Scope.Value, context.Partition?.Value);
            var subject = context.Authority.Metadata["elsa.identity.subject-id"];
            return ValueTask.FromResult(subject == "deny" ? WorkflowExecutableStartDecision.Deny("denied", "Denied by fixture") :
                WorkflowExecutableStartDecision.Allow());
        }
    }

    private sealed class PinBoundary
    {
        public Func<WorkflowExecutableSourceReference, CancellationToken, ValueTask>? BeforeAdmission { get; set; }
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
