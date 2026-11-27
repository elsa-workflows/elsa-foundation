using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        Contract.ActorRef actor;

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
                using var body = new MemoryStream();
                var context = new DefaultHttpContext { RequestServices = services, RequestAborted = token };
                context.Request.Path = Route;
                context.Request.Method = "POST";
                context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget = Route;
                context.Response.Body = body;
                await handler.HandleAsync(context);
                Assert.True(context.Response.StatusCode == 202,
                    $"The held endpoint returned {context.Response.StatusCode}: {System.Text.Encoding.UTF8.GetString(body.ToArray())}");
                actor = new(Assert.Single(context.Response.Headers[KernelHttpEdge.WorkflowInstanceIdHeader])!);
                var reads = await shell.ReadAsync(token);
                Assert.Equal(Contract.ActorStatus.Running, (await reads.GetAsync(tenant, actor, token))?.Header.Status);
                var pins = stores.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                var tail = await pins.ReadOpenTailAsync(Persistence, token);
                Assert.NotNull(tail);
                var pin = Assert.Single((await pins.ListOpenAsync(Persistence, null, tail, 16, token)).Items);
                Assert.Equal((actor.Id, identity), (pin.ActorReference, pin.Executable));
                Assert.True(PendingRootStart.OwnsIssuance(pin.Issuance));

                var deactivated = await coordinator.DeactivateAsync(new(executable, KernelServing.DefaultSlotName,
                    WorkflowActivationSource.Publishing, published.Slot.Revision), token);
                Assert.True(deactivated.Succeeded, deactivated.Diagnostic);
                Assert.True(await stores.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().RetireAsync(
                    WorkflowActivationReferenceIdentity.Create(published.Slot.ActiveActivationId!), DateTimeOffset.UtcNow, "retired the source", token));
                await stores.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync(token);
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
                await stores.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync(token);
                Assert.Null(await stores.GetRequiredService<IWorkflowExecutableStore>().FindAsync(identity.ArtifactId, token));
            }
            finally { await StopAsync(shell); }
        }

        ServiceProvider OpenShell()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
            services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.ArtifactCreationGracePeriod = TimeSpan.Zero);
            services.AddWorkflowRuntime();
            services.AddRuntimeEntityFrameworkCore(new()
            {
                Provider = provider, ConnectionString = runtime,
                RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32)
            });
            new WorkflowsRuntimeKernelHostingFeature().ConfigureServices(services, KernelShellStorage.CreatePreparedStartsQualification(
                provider, foundationPath, foundation));
            new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
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
