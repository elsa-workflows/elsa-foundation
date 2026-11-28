using System.Data.Common;
using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class KernelShellHeldExecutableTests
{
    private const string Route = "/workflows/http/held-pinned";
    private const string HeldDatabase = "KERNEL_HELD_FOUNDATION_POSTGRES";
    private const string RuntimeDatabase = "KERNEL_HELD_RUNTIME_POSTGRES";
    private static readonly PersistenceScope Persistence = new(PersistenceScope.DefaultValue);

    [Fact]
    public Task Sqlite_held_HTTP_pins_its_executable_through_cold_resume_and_actor_retirement() => RunAsync(postgres: false);

    [SkippableFact]
    public Task PostgreSql_held_HTTP_pins_its_executable_through_cold_resume_and_actor_retirement()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HeldDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)),
            "Requires dedicated Foundation and Runtime PostgreSQL databases.");
        return RunAsync(postgres: true);
    }

    private static async Task RunAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-held-executable");
        try
        {
            var provider = postgres ? "PostgreSql" : "Sqlite";
            var foundationPath = directory is null ? null : Path.Combine(directory.FullName, "foundation.db");
            var foundation = postgres ? Environment.GetEnvironmentVariable(HeldDatabase)! : null;
            var runtime = postgres ? Environment.GetEnvironmentVariable(RuntimeDatabase)!
                : new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false }.ToString();
            if (postgres) Assert.NotEqual(new NpgsqlConnectionStringBuilder(foundation).Database, new NpgsqlConnectionStringBuilder(runtime).Database);
            await RunAsync(provider, foundationPath, foundation, runtime, deadline.Token);
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunAsync(string provider, string? foundationPath, string? foundation, string runtime, CancellationToken token)
    {
        const string definition = "held-pinned";
        const string eventName = "complete-held-pinned";
        var tenant = EmbeddedTenant.Bind(Persistence.Value);
        var executable = ExecutableOf("held-pinned-v1", definition, Sequence("sequence",
            Node("start", Types.HttpEndpoint, [Literal("Path", definition), Literal("SupportedMethods", new[] { "POST" }),
                Literal("CanStartWorkflow", true, "Boolean")]),
            Node("respond", Types.WriteHttpResponse, [Literal("StatusCode", 202, "Int32"), Literal("Body", "waiting", "String")]),
            Node("wait", Types.Event, [Literal("EventName", eventName), Literal("CanStartWorkflow", false, "Boolean")]),
            WriteLine("finish", Literal("text", "finished"))));
        var identity = executable.Identity;
        var unavailable = new PinAvailability();
        var sourceReads = new SourceReadFailures();
        Contract.ActorRef actor;
        WorkflowPublishedStart selected;

        await using (var services = OpenShell())
        {
            await using var runtimeScope = await services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
            var stores = runtimeScope.ServiceProvider;
            await stores.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
            var materials = stores.GetRequiredService<IWorkflowExecutableStore>();
            await materials.SaveAsync(executable, token);
            var source = new WorkflowExecutableSourceReference("candidate", identity.ArtifactId, "definition", definition,
                identity.ArtifactVersion, definition, identity.DefinitionVersionId, identity.ArtifactVersion, DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow, WorkflowExecutableReferenceScope.Published);
            var coordinator = stores.GetRequiredService<IWorkflowActivationCoordinator>();
            var published = await coordinator.ActivateAsync(new(executable, source, KernelServing.DefaultSlotName,
                "held-material-publication", WorkflowActivationSource.Publishing, 0), token);
            Assert.True(published.Succeeded, published.Diagnostic);
            var shell = services.GetRequiredService<KernelShellRuntime>();
            await new StartKernelShell(shell).InitializeAsync(token);
            try
            {
                var handler = new KernelHttpRequestHandler(services.GetRequiredService<KernelServing>(), shell.IngressAsync,
                    services.GetRequiredService<KernelShellStorage>().ReadClockAsync, tenant, "/workflows/http", TimeSpan.FromSeconds(10),
                    maxRequestBodyBytes: 64 * 1024, enforceRequestTimeout: true);
                var reads = await shell.ReadAsync(token);
                sourceReads.FailNextRead();
                var context = await RequestAsync(handler, Route);
                Assert.Equal(202, context.StatusCode);
                Assert.Equal(1, sourceReads.Failures);
                actor = new(Assert.Single(context.Headers[KernelHttpEdge.WorkflowInstanceIdHeader])!);
                var birth = Assert.IsType<Contract.ActorView>(await reads.GetAsync(tenant, actor, token));
                Assert.Equal(Contract.ActorStatus.Running, birth.Header.Status);
                var publishedSourceId = WorkflowActivationReferenceIdentity.Create(published.Slot.ActiveActivationId!);
                var references = stores.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
                var publishedSource = Assert.IsType<WorkflowExecutableSourceReference>(await references.FindAsync(publishedSourceId, token));
                selected = Assert.IsType<WorkflowPublishedStart>(WorkflowActor.DescribeRun(birth.Header)?.PublishedStart);
                Assert.Equal((publishedSourceId, published.Slot.ActiveActivationId, published.Slot.SlotId,
                    published.Slot.SlotName, published.Slot.Revision),
                    (selected.SourceReferenceId, selected.ActivationId, selected.SlotId, selected.SlotName, selected.SlotRevision));
                Assert.Equal(KernelSourceStamp.Digest(publishedSource), selected.SourceDigest);
                var pins = stores.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                var tail = await pins.ReadOpenTailAsync(Persistence, token);
                Assert.NotNull(tail);
                var pin = Assert.Single((await pins.ListOpenAsync(Persistence, null, tail, 16, token)).Items);
                Assert.Equal((actor.Id, identity), (pin.ActorReference, pin.Executable));
                Assert.True(PendingRootStart.OwnsIssuance(pin.Issuance));

                unavailable.FailWrites = true;
                var shortRequest = new KernelHttpRequestHandler(services.GetRequiredService<KernelServing>(), shell.IngressAsync,
                    services.GetRequiredService<KernelShellStorage>().ReadClockAsync, tenant, "/workflows/http", TimeSpan.FromSeconds(2),
                    maxRequestBodyBytes: 64 * 1024, enforceRequestTimeout: true);
                Assert.Equal(408, (await RequestAsync(shortRequest, Route)).StatusCode);
                Assert.True(unavailable.Attempts > 0);
                Assert.Equal(actor, Assert.Single((await reads.ListAsync(tenant, new(), cancellationToken: token)).Items).Header.Ref);
                unavailable.FailWrites = false;
                var earlierFailures = sourceReads.Failures;
                sourceReads.Unavailable = true;
                try
                {
                    var request = RequestAsync(shortRequest, Route);
                    await sourceReads.Blocked.WaitAsync(token);
                    var timedOut = await request.WaitAsync(TimeSpan.FromSeconds(5), token);
                    Assert.Equal(408, timedOut.StatusCode);
                    Assert.False(timedOut.Headers.ContainsKey(KernelHttpEdge.WorkflowInstanceIdHeader));
                    Assert.True(sourceReads.Failures > earlierFailures + 1);
                    Assert.Equal(actor, Assert.Single((await reads.ListAsync(tenant, new(), cancellationToken: token)).Items).Header.Ref);
                }
                finally
                {
                    sourceReads.Unavailable = false;
                    sourceReads.ReleaseBlockedRead();
                }
                Assert.True(await sourceReads.BlockedReadFinished.WaitAsync(token));

                unavailable.FailWrites = true;
                var probe = Wf.Workflow("held-probe", Wf.Sequence("main", Wf.HttpEndpoint("start", "held-probe"), Wf.Respond("out", "ok")));
                await services.GetRequiredService<KernelServing>().ServeHostWorkflowAsync(probe, token);
                var hosted = await RequestAsync(handler, "/workflows/http/held-probe");
                Assert.Equal(200, hosted.StatusCode);
                unavailable.FailWrites = false;

                var before = (await reads.ListAsync(tenant, new(), cancellationToken: token)).Items.Count;
                var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                unavailable.BeforePublishedPin = async cancellation =>
                {
                    reached.TrySetResult();
                    await release.Task.WaitAsync(cancellation);
                };
                var stale = RequestAsync(handler, Route);
                try
                {
                    await reached.Task.WaitAsync(token);
                    var deactivated = await coordinator.DeactivateAsync(new(executable, KernelServing.DefaultSlotName,
                        WorkflowActivationSource.Publishing, published.Slot.Revision), token);
                    Assert.True(deactivated.Succeeded, deactivated.Diagnostic);
                }
                finally { release.TrySetResult(); }
                var refused = await stale;
                Assert.Equal(404, refused.StatusCode);
                Assert.False(refused.Headers.ContainsKey(KernelHttpEdge.WorkflowInstanceIdHeader));
                Assert.Equal(before, (await reads.ListAsync(tenant, new(), cancellationToken: token)).Items.Count);
                Assert.True(await references.RetireAsync(publishedSourceId, DateTimeOffset.UtcNow, "retired the source", token));
                Assert.Contains(identity.ArtifactId, await materials.ListMaterialRetentionRootIdsAsync(token));
                Assert.NotNull(await materials.FindAsync(identity.ArtifactId, token));
            }
            finally { await StopAsync(shell); }
        }

        await using (var services = OpenShell())
        {
            var shell = services.GetRequiredService<KernelShellRuntime>();
            await new StartKernelShell(shell).InitializeAsync(token);
            try
            {
                var serving = services.GetRequiredService<KernelServing>();
                Assert.Empty(serving.Slots);
                serving.Registry.Add(new Workflow(identity.ArtifactId, Wf.WriteLine("wrong", "wrong executable"), definition: definition)
                    { ExecutableSelection = "different selection" });
                var store = await services.GetRequiredService<KernelShellStorage>().OpenAsync(token);
                var resumed = await store.Ingress.SendAsync(tenant, new Contract.Target.Ref(actor), "finish-held-pinned", NativeKinds.Resume,
                    JsonSerializer.Serialize(new ResumeStimulus(eventName)), cancellationToken: token);
                Assert.Equal(Contract.AdmissionOutcome.Accepted, resumed.Outcome);
                var terminal = await WaitAsync(() => store.Reads.GetAsync(tenant, actor, token),
                    item => item?.Header.Status == Contract.ActorStatus.Finished, token);
                Assert.Equal(selected, WorkflowActor.DescribeRun(terminal!.Header)?.PublishedStart);
                Assert.Equal(JsonSerializer.Serialize(identity), serving.Registry.Get(identity.ArtifactId).ExecutableSelection);

                var policy = await store.RetentionPolicies.ReadRetentionPolicyAsync(tenant, token);
                var applied = await store.RetentionPolicies.TryApplyRetentionPolicyAsync(tenant, policy.Revision,
                    new(Finished: TimeSpan.Zero), long.MaxValue, cancellationToken: token);
                Assert.Equal(Contract.ActorRetentionPolicyChangeOutcome.Applied, applied.Outcome);
                Assert.Equal(Contract.AdmissionOutcome.Accepted, (await store.AdmitRetentionDelete(tenant, actor, terminal!.Selection!, token)).Outcome);
                await WaitAsync(() => store.Reads.GetAsync(tenant, actor, token), item => item is null, token);

                await using var runtimeScope = await services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var stores = runtimeScope.ServiceProvider;
                var pins = stores.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                await WaitAsync(() => pins.ReadOpenTailAsync(Persistence, token), cursor => cursor is null, token);
                Assert.Empty(await pins.ListPinnedExecutableArtifactIdsAsync(token));
                var materials = stores.GetRequiredService<IWorkflowExecutableStore>();
                Assert.DoesNotContain(identity.ArtifactId, await materials.ListMaterialRetentionRootIdsAsync(token));
            }
            finally { await StopAsync(shell); }
        }

        ServiceProvider OpenShell()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
            services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.ArtifactCreationGracePeriod = TimeSpan.Zero);
            KernelRuntimeFixture.Add(services, provider, runtime);
            if (provider == "PostgreSql")
                services.ConfigureDbContext<RuntimePostgreSqlDbContext>(options => options.AddInterceptors(sourceReads));
            else
                services.ConfigureDbContext<RuntimeSqliteDbContext>(options => options.AddInterceptors(sourceReads));
            var pinStore = services.Last(item => item.ServiceType == typeof(IRuntimeKernelExecutablePinStore));
            services.Remove(pinStore);
            services.AddScoped<IRuntimeKernelExecutablePinStore>(sp => new TestPins(
                (IRuntimeKernelExecutablePinStore)ActivatorUtilities.CreateInstance(sp, pinStore.ImplementationType!), unavailable));
            new WorkflowsRuntimeKernelHostingFeature { Provider = provider, SqlitePath = foundationPath, PostgresConnectionString = foundation }
                .ConfigureServices(services);
            new WorkflowsRuntimeKernelFeature().ConfigureServices(services);
            new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        async Task<Microsoft.AspNetCore.Http.HttpResponse> RequestAsync(KernelHttpRequestHandler handler, string path)
        {
            using var body = new MemoryStream();
            var context = new DefaultHttpContext { RequestAborted = token };
            context.Request.Path = path;
            context.Request.Method = "POST";
            context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget = path;
            context.Response.Body = body;
            await handler.HandleAsync(context);
            return context.Response;
        }
    }

    private sealed class SourceReadFailures : DbCommandInterceptor
    {
        private int failNext;
        private int failures;
        private int unavailableAttempts;
        private readonly TaskCompletionSource blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> blockedReadFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile bool Unavailable;
        internal int Failures => Volatile.Read(ref failures);
        internal Task Blocked => blocked.Task;
        internal Task<bool> BlockedReadFinished => blockedReadFinished.Task;

        internal void FailNextRead() => Interlocked.Exchange(ref failNext, 1);
        internal void ReleaseBlockedRead() => release.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(RuntimeArtifactEfModule.SourceReferenceTableName, StringComparison.Ordinal) &&
                (Unavailable || Interlocked.Exchange(ref failNext, 0) != 0))
            {
                Interlocked.Increment(ref failures);
                if (Unavailable && Interlocked.Increment(ref unavailableAttempts) == 3)
                {
                    // SQLite can enter a synchronous provider read that ignores cancellation until it returns.
                    blocked.TrySetResult();
                    await release.Task;
                    blockedReadFinished.TrySetResult(cancellationToken.IsCancellationRequested);
                }
                if (command is SqliteCommand) throw new SqliteException("Test source read is busy.", 5);
                throw new NpgsqlException("Test source read is transient.", new TimeoutException());
            }
            return result;
        }
    }

    private sealed class PinAvailability
    {
        internal volatile bool FailWrites;
        internal int Attempts;
        internal Func<CancellationToken, Task>? BeforePublishedPin;
    }

    private sealed class TestPins(IRuntimeKernelExecutablePinStore inner, PinAvailability availability) : IRuntimeKernelExecutablePinStore
    {
        public ValueTask<bool> TryPinAsync(IReadOnlyCollection<RuntimeKernelExecutablePin> pins, CancellationToken token) =>
            inner.TryPinAsync(pins, token);

        public async ValueTask<bool> TryPinPublishedAsync(RuntimeKernelExecutablePin pin, WorkflowActivationSlot slot,
            WorkflowExecutableSourceReference source, CancellationToken token)
        {
            if (availability.FailWrites)
            {
                Interlocked.Increment(ref availability.Attempts);
                throw new WorkflowExecutableRootWriteLeaseUnavailableException(pin.Executable.ArtifactId, "qualification");
            }
            if (Interlocked.Exchange(ref availability.BeforePublishedPin, null) is { } before)
                await before(token);
            return await inner.TryPinPublishedAsync(pin, slot, source, token);
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

    private static async Task<T> WaitAsync<T>(Func<ValueTask<T>> read, Func<T, bool> complete, CancellationToken token)
    {
        while (true)
        {
            var item = await read();
            if (complete(item)) return item;
            await Task.Delay(50, token);
        }
    }

    private static async Task StopAsync(KernelShellRuntime shell)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await new StopKernelShell(shell).TerminateAsync(stop.Token);
    }
}
