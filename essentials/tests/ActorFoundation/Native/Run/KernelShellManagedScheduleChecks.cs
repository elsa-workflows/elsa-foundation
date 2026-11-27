using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

/// <summary>Real shell composition from durable publication through schedule delivery and committed workflow history.</summary>
internal static class KernelShellManagedScheduleChecks
{
    private const string Trigger = "trigger";
    private const string After = "after-trigger";
    private static readonly TimeSpan TimerInterval = TimeSpan.FromSeconds(5);
    private static readonly PersistenceScope Persistence = new(PersistenceScope.DefaultValue);

    internal static async Task<int> RunAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        var token = deadline.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-managed-schedules-");
        try
        {
            var provider = postgres ? "PostgreSql" : "Sqlite";
            var foundation = postgres ? Connection("KERNEL_PREVIEW_POSTGRES") : null;
            var runtime = postgres ? Connection("KERNEL_SCHEDULE_RUNTIME_POSTGRES") : new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory!.FullName, "runtime.db"),
                Pooling = false
            }.ToString();
            if (foundation is not null)
                Require(new NpgsqlConnectionStringBuilder(foundation).Database != new NpgsqlConnectionStringBuilder(runtime).Database,
                    "PostgreSQL qualification requires separate dedicated Foundation and Runtime databases");
            var path = directory is null ? null : Path.Combine(directory.FullName, "foundation.db");
            await RunCoreAsync(provider, foundation, runtime, path, token);
            return 0;
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunCoreAsync(string provider, string? foundation, string runtime, string? path, CancellationToken token)
    {
        var timer = ExecutableOf("managed-timer-v1", "managed-timer", Sequence("sequence",
            Node(Trigger, Types.Timer, [Literal("Interval", "PT5S", "String")]), Completion(marker: "timer")));
        var cron = ExecutableOf("managed-cron-v1", "managed-cron", Node("flowchart", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = Trigger,
                connections = new[] { new { source = new { nodeId = Trigger, port = "Done" }, target = new { nodeId = After } } }
            }),
            slots: [Slot("Flowchart.Activities",
                Node(Trigger, "Elsa.Cron", [Literal("Expression", "*/2 * * * * *", "String")]), Completion(marker: "cron"))]));
        WorkflowActivationResult timerPublication, cronPublication;
        await using (var publisher = OpenShell())
        {
            await publisher.GetRequiredService<KernelShellStorage>().InstallAsync(token);
            var publishingScopes = publisher.GetRequiredService<IServiceScopeFactory>();
            await using (var scope = await publishingScopes.CreateAsyncScopeAsync(Persistence))
            {
                var database = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
                await database.Database.MigrateAsync(token);
            }
            timerPublication = await PublishAsync(publishingScopes, timer, tenant: null);
            cronPublication = await PublishAsync(publishingScopes, cron, tenant: PersistenceScope.DefaultValue);
        }

        await using (var host = OpenShell())
        {
            var serving = host.GetRequiredService<KernelServing>();
            Require(serving.Capabilities.Schedules && serving.Slots.Count == 0,
                "the cold qualification shell exposes managed schedules and has no in-memory publications");
            var shell = host.GetRequiredService<KernelShellRuntime>();
            await new StartKernelShell(shell).InitializeAsync(token);
            try
            {
                Require(serving.Slots.Select(slot => slot.ActivationId).ToHashSet(StringComparer.Ordinal)
                        .SetEquals([timerPublication.Slot.ActiveActivationId!, cronPublication.Slot.ActiveActivationId!]),
                    "the cold shell restored both exact durable publications before serving");
                await VerifyAsync(host, timer, timerPublication, tenant: null);
                await VerifyAsync(host, cron, cronPublication, tenant: PersistenceScope.DefaultValue);
            }
            finally { await StopAsync(shell); }
        }

        // Let one Timer become due with the Foundation owner down, then replace its Runtime publication before a new owner starts.
        await Task.Delay(TimerInterval + TimeSpan.FromMilliseconds(300), token);
        var replacement = ExecutableOf("managed-timer-v2", "managed-timer", Sequence("sequence",
            Node(Trigger, Types.Timer, [Literal("Interval", "PT5S", "String")]), Completion(marker: "republished-timer")));
        WorkflowActivationResult republished;
        await using (var publisher = OpenShell())
        {
            await publisher.GetRequiredService<KernelShellStorage>().InstallAsync(token);
            republished = await PublishAsync(publisher.GetRequiredService<IServiceScopeFactory>(), replacement,
                tenant: PersistenceScope.DefaultValue, expectedRevision: timerPublication.Slot.Revision);
        }
        await using (var restarted = OpenShell())
        {
            var shell = restarted.GetRequiredService<KernelShellRuntime>();
            await new StartKernelShell(shell).InitializeAsync(token);
            try
            {
                var carried = await VerifyAsync(restarted, replacement, republished, tenant: PersistenceScope.DefaultValue);
                await using var read = await restarted.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var controls = read.ServiceProvider.GetRequiredService<IRuntimeKernelScheduleControlReader>();
                var active = await controls.FindAsync(carried.Occurrence.Scope, carried.Occurrence.ScheduleKey, token);
                Require(active?.Owner?.Continuity is { } continuity && carried.Occurrence.Origin is { TenantId: null } &&
                    carried.Occurrence.Due > republished.Slot.UpdatedAt && carried.Occurrence.Due <= continuity.FoundationActivatedAt,
                    "the installed shell carried the old due Timer by the Foundation clock despite the Runtime process clock skew");
                Console.WriteLine($"ok {provider} republish carried old timer due {carried.Occurrence.Due:O} at Foundation cutover {active!.Owner!.Continuity!.FoundationActivatedAt:O}");
            }
            finally { await StopAsync(shell); }
        }

        async Task<RuntimeKernelScheduleMaterializedOccurrence> VerifyAsync(ServiceProvider host, WorkflowExecutable executable,
            WorkflowActivationResult publication, string? tenant)
        {
            var scopes = host.GetRequiredService<IServiceScopeFactory>();
            var shellRuns = host.GetRequiredService<KernelRuns>();
            var scope = new TriggerScope(Persistence, tenant);
            var key = $"schedule:{executable.Identity.DefinitionId}:{Trigger}";
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(TimeSpan.FromSeconds(18));
            try
            {
                while (true)
                {
                    await using var read = await scopes.CreateAsyncScopeAsync(Persistence);
                    var ledger = read.ServiceProvider.GetRequiredService<IRuntimeKernelScheduleOccurrenceRetirementStore>();
                    var deliveries = await ledger.ListMaterializedAsync(scope, null, 32, wait.Token);
                    foreach (var delivery in deliveries.Where(item => item.Occurrence.ScheduleKey == key))
                    {
                        var occurrence = delivery.Occurrence;
                        Require(occurrence.Scope == scope && occurrence.ActivationId == publication.Slot.ActiveActivationId &&
                            occurrence.PublicationRevision == publication.Slot.Revision && occurrence.Executable == executable.Identity &&
                            occurrence.TriggerNode == Trigger, $"the occurrence retains exact publication and trigger authority for {key}");
                        var actor = await shellRuns.GetAsync(new(delivery.ActorReference), wait.Token);
                        if (actor.Run is not { } run || run.View.Header.Status == Contract.ActorStatus.Running) continue;
                        Require(run.View.Header.Status == Contract.ActorStatus.Finished && run.Run.Workflow == executable.Identity.ArtifactId &&
                            run.Run.Definition == executable.Identity.DefinitionId,
                            $"the exact acknowledged birth finished on {executable.Identity.ArtifactId}");
                        var history = await shellRuns.ReadSequencedJournalAsync(new(delivery.ActorReference), 0, null, 64, wait.Token);
                        Require(history is not null && history.Items.Count(IsCompletion) == 1,
                            $"the authored continuation ran once for {executable.Identity.DefinitionId}");
                        Console.WriteLine($"ok {provider} {executable.Identity.DefinitionId} published activation {occurrence.ActivationId} " +
                            $"fired through the managed shell as {delivery.ActorReference}");
                        return delivery;
                    }
                    await Task.Delay(100, wait.Token);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException($"No finished shell run was acknowledged for {key} in occurrence scope {tenant ?? "(unqualified)"}.");
            }
        }

        static async Task StopAsync(KernelShellRuntime shell)
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await new StopKernelShell(shell).TerminateAsync(stop.Token);
        }

        ServiceProvider OpenShell()
        {
            var storage = KernelShellStorage.CreateManagedSchedulesQualification(provider, path, foundation);
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<TimeProvider>(new SkewedRuntimeClock());
            services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
            services.AddWorkflowRuntime();
            services.AddRuntimeEntityFrameworkCore(new()
            {
                Provider = provider,
                ConnectionString = runtime,
                RecoveryContinuationSigningKey = new string('r', 32),
                HierarchyCursorSigningKey = new string('h', 32)
            });
            new WorkflowsRuntimeKernelHostingFeature().ConfigureServices(services, storage);
            new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        async Task<WorkflowActivationResult> PublishAsync(IServiceScopeFactory publishingScopes, WorkflowExecutable executable, string? tenant,
            long expectedRevision = 0)
        {
            await using var scope = await publishingScopes.CreateAsyncScopeAsync(Persistence);
            var provider = scope.ServiceProvider;
            await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable, token);
            var identity = executable.Identity;
            var now = provider.GetRequiredService<TimeProvider>().GetUtcNow();
            var source = new WorkflowExecutableSourceReference("candidate", identity.ArtifactId, "definition", identity.DefinitionId,
                identity.ArtifactVersion, identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion, now, now,
                WorkflowExecutableReferenceScope.Published, TenantId: tenant);
            var result = await provider.GetRequiredService<IWorkflowActivationCoordinator>().ActivateAsync(new(executable, source,
                KernelServing.DefaultSlotName, $"publish-{identity.ArtifactId}", WorkflowActivationSource.Publishing, expectedRevision), token);
            Require(result.Succeeded, $"publication of {identity.ArtifactId} failed at {result.FailedStep}: {result.Diagnostic}");
            return result;
        }
    }

    private sealed class SkewedRuntimeClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow().AddMinutes(-2);
    }

    private static ExecutableNode Completion(string marker) => WriteLine(After, Literal("text", marker, "String"));

    private static bool IsCompletion(Contract.SequencedJournalEntry item)
    {
        if (item.Entry is null) return false;
        using var document = JsonDocument.Parse(item.Entry);
        var entry = document.RootElement;
        return entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("NativeStructure", out var version) && version.GetInt32() == 1 &&
            entry.GetProperty("Kind").GetInt32() == 3 && entry.GetProperty("Node").GetString() == After;
    }

    private static string Connection(string variable) => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Set {variable} to a dedicated empty PostgreSQL database.");

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
