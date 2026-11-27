using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Native;
using KernelWorkflowActor = Kernel.WorkflowActor.WorkflowActor;
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

public sealed class KernelShellChildExecutableTests
{
    private static readonly PersistenceScope Scope = new(PersistenceScope.DefaultValue);

    [Fact]
    public Task Sqlite_shell_pins_the_canonical_material_of_a_child() => RunAsync(postgres: false);

    [SkippableFact]
    public Task PostgreSql_shell_pins_the_canonical_material_of_a_child()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KERNEL_CHILD_RUNTIME_POSTGRES")),
            "Requires dedicated Foundation and Runtime PostgreSQL databases.");
        return RunAsync(postgres: true);
    }

    private static async Task RunAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        var token = deadline.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-child-executable-");
        try
        {
            var provider = postgres ? "PostgreSql" : "Sqlite";
            var path = directory is null ? null : Path.Combine(directory.FullName, "foundation.db");
            var foundation = postgres ? Connection("KERNEL_PREVIEW_POSTGRES") : null;
            var runtime = postgres ? Connection("KERNEL_CHILD_RUNTIME_POSTGRES")
                : new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false }.ToString();
            if (foundation is not null)
                Check(new NpgsqlConnectionStringBuilder(foundation).Database != new NpgsqlConnectionStringBuilder(runtime).Database,
                    "Foundation and Runtime use independent databases");
            await RunAsync(provider, foundation, runtime, path, token);
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunAsync(string provider, string? foundation, string runtime, string? path, CancellationToken token)
    {
        var material = ExecutableOf("protected-child-v1", "original-source", Sequence("root",
            Node("wait", Types.Event, [Literal("EventName", "release-protected-child"), Literal("CanStartWorkflow", false, "Boolean")]),
            WriteLine("finished", Literal("text", "child continued from the stored executable"))));
        // A later publication can reuse the canonical behavior under a different logical source.
        var child = new WorkflowExecutable(material.Identity with
        {
            DefinitionId = "protected-child", DefinitionVersionId = "protected-child-v2", ArtifactVersion = "2"
        }, material.RootActivity, material.ResumeTargets, material.CreatedAt, material.CompatibilityMetadata, material.IncidentStrategy);
        var parent = ExecutableOf("dispatch-parent-v1", "dispatch-parent", Sequence("root",
            Node("http", Types.HttpEndpoint, [Literal("Path", "dispatch-parent"), Literal("SupportedMethods", new[] { "POST" }),
                Literal("CanStartWorkflow", true, "Boolean")]),
            Node("dispatch", Types.Dispatch, [Literal("WorkflowDefinitionId", child.Identity.DefinitionId),
                Literal("WaitForCompletion", true, "Boolean")])));
        var scope = EmbeddedTenant.Bind(Scope.Value);
        var definitions = new[] { child, parent };

        await using (var publisher = OpenShell())
        {
            await using var childScope = await publisher.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Scope);
            var services = childScope.ServiceProvider;
            await services.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
            await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(material, token);
            foreach (var executable in definitions) await PublishAsync(services, executable);
            var selected = publisher.GetRequiredService<KernelServing>().Slots.Single(slot => slot.DefinitionId == child.Identity.DefinitionId).Workflow;
            Check(selected.Definition == child.Identity.DefinitionId && selected.ExecutableSelection == JsonSerializer.Serialize(material.Identity),
                "the live publication serves the canonical stored material under its own logical definition");
        }

        Contract.ActorRef childRef;
        Contract.ActorRef parentRef;
        await using (var services = OpenShell())
        {
            var shell = services.GetRequiredService<KernelShellRuntime>();
            await new StartKernelShell(shell).InitializeAsync(token);
            try
            {
                var serving = services.GetRequiredService<KernelServing>();
                var selection = KernelHttpEdge.Match(serving, new("POST", "dispatch-parent"), "dispatch-parent");
                Check(selection is not null &&
                    KernelExecutableSelection.Read(selection.Spec.Type, selection.Spec.TypeData)?.Executable == parent.Identity,
                    "the normal HTTP route carries the exact full selected root executable");
                var plainStart = KernelWorkflowActor.Start(NativeExecutionModel.Start("host-plan"));
                Check(!plainStart.TypeData.Contains("ExecutableSelection", StringComparison.Ordinal) &&
                    KernelExecutableSelection.Read(plainStart.Type, plainStart.TypeData) is { Executable: null, Workflow: "host-plan" },
                    "a historical start without selection retains the same header shape");
                var ingress = await shell.IngressAsync(token);
                var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(selection!.Spec), "protected-parent", Contract.Kinds.Start,
                    cancellationToken: token);
                Check(admitted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the installed owner admitted the authored parent");
                parentRef = admitted.Actor!.Value;

                await using var runtimeScope = await services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Scope);
                var runtimeServices = runtimeScope.ServiceProvider;
                var pins = runtimeServices.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                var pin = await WaitAsync(async () =>
                {
                    var through = await pins.ReadOpenTailAsync(Scope, token);
                    return through is null ? null : (await pins.ListOpenAsync(Scope, null, through, 16, token)).Items.Single();
                }, item => item is not null, token);
                childRef = new(pin!.ActorReference);
                var reads = await shell.ReadAsync(token);
                var waiting = await WaitAsync(() => reads.GetAsync(scope, childRef, token),
                    item => item?.Header.Status == Contract.ActorStatus.Running, token);
                Check(pin.Executable == material.Identity && pin.Tenant == scope.Tenant &&
                    waiting!.Header.Labels.GetValueOrDefault("definition") == child.Identity.DefinitionId,
                    "the installed child retains the canonical material and the selected logical definition");

                var slot = await runtimeServices.GetRequiredService<IWorkflowActivationAuthority>().FindAsync(child.Identity.DefinitionId,
                    KernelServing.DefaultSlotName, token);
                Check(slot is not null && (await runtimeServices.GetRequiredService<IWorkflowActivationCoordinator>().DeactivateAsync(
                    new(child, KernelServing.DefaultSlotName, WorkflowActivationSource.Publishing, slot.Revision), token)).Succeeded,
                    "the child's definition was unpublished while its committed run waited");
                var sources = runtimeServices.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
                var sourceId = WorkflowActivationReferenceIdentity.Create(slot!.ActiveActivationId!);
                Check(await sources.RetireAsync(sourceId, TimeProvider.System.GetUtcNow(), "qualification retired the unpublished source", token),
                    "the unpublished activation source no longer roots its artifact");
                var collector = runtimeServices.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();
                await collector.SweepAsync(token);
                Check(await runtimeServices.GetRequiredService<IWorkflowExecutableStore>().FindAsync(child.Identity.ArtifactId, token) is not null,
                    "normal executable collection kept the unpublished child artifact");

                await RefuseReplacedIdentityAsync(services, serving, runtimeServices, pins);
            }
            finally { await StopAsync(shell); }
        }

        await using (var services = OpenShell())
        {
            var shell = services.GetRequiredService<KernelShellRuntime>();
            await new StartKernelShell(shell).InitializeAsync(token);
            try
            {
                var registry = services.GetRequiredService<KernelServing>().Registry;
                Check(!registry.Contains(child.Identity.ArtifactId),
                    "the cold installed owner does not serve the unpublished artifact");
                registry.Add(new Workflow(child.Identity.ArtifactId, Wf.WriteLine("unrelated", "replacement"),
                    definition: child.Identity.DefinitionId) { ExecutableSelection = "unrelated material" });
                var reads = await shell.ReadAsync(token);
                var ingress = await shell.IngressAsync(token);
                var release = await ingress.SendAsync(scope, new Contract.Target.Ref(childRef), "continue-protected-child",
                    NativeKinds.Resume, JsonSerializer.Serialize(new ResumeStimulus("release-protected-child")), cancellationToken: token);
                Check(release.Outcome == Contract.AdmissionOutcome.Accepted, "the exact committed child accepted its resume after restart");
                var terminal = await WaitAsync(() => reads.GetAsync(scope, childRef, token),
                    item => item?.Header.Status == Contract.ActorStatus.Finished, token);
                Check(registry.Get(child.Identity.ArtifactId).ExecutableSelection == JsonSerializer.Serialize(material.Identity),
                    "the cold owner replaced the stale same-ID plan with the exact retained executable before resuming");
                await WaitAsync(() => reads.GetAsync(scope, parentRef, token), item => item?.Header.Status == Contract.ActorStatus.Finished, token);
                var operators = await OpenOperatorsAsync();
                var retired = await operators.SendAsync(scope, new Contract.Target.Ref(childRef), "retire-protected-child", Contract.Kinds.Delete,
                    JsonSerializer.Serialize(new Contract.OperatorCommand(terminal!.Selection)), cancellationToken: token);
                Check(retired.Outcome == Contract.AdmissionOutcome.Accepted, "the selected Delete admitted the exact terminal child");
                await WaitAsync(() => reads.GetAsync(scope, childRef, token), item => item is null, token);

                await using var runtimeScope = await services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Scope);
                var stores = runtimeScope.ServiceProvider;
                var pins = stores.GetRequiredService<IRuntimeKernelExecutablePinStore>();
                await WaitAsync(() => pins.ReadOpenTailAsync(Scope, token), cursor => cursor is null, token);
                Check((await pins.ListPinnedExecutableArtifactIdsAsync(token)).Count == 0,
                    "installed housekeeping released the exact retired birth's pin");
                await stores.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SweepAsync(token);
                Check(await stores.GetRequiredService<IWorkflowExecutableStore>().FindAsync(child.Identity.ArtifactId, token) is null,
                    "ordinary executable collection removed the artifact after its final root was released");
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
            new WorkflowsRuntimeKernelHostingFeature().ConfigureServices(services, KernelShellStorage.Create(provider, path, foundation),
                protectChildExecutables: true);
            new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        async Task<Contract.IIngress> OpenOperatorsAsync()
        {
            if (foundation is null)
                return await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(path!, KernelWorkflowActor.ActorType, Authorize, cancellationToken: token);
            var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(foundation).Options;
            return await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(() => PostgresPreviewHosting.CreateShippedContext(options),
                KernelWorkflowActor.ActorType, Authorize, cancellationToken: token);

            bool Authorize(Contract.BoundScope selectedScope, Contract.ActorRef selected, string kind) =>
                ReferenceEquals(selectedScope, scope) && selected == childRef && kind == Contract.Kinds.Delete;
        }

        async Task PublishAsync(IServiceProvider services, WorkflowExecutable executable)
        {
            var identity = executable.Identity;
            await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable, token);
            var now = TimeProvider.System.GetUtcNow();
            var source = new WorkflowExecutableSourceReference("candidate", identity.ArtifactId, "definition", identity.DefinitionId,
                identity.ArtifactVersion, identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion, now, now,
                WorkflowExecutableReferenceScope.Published);
            var published = await services.GetRequiredService<IWorkflowActivationCoordinator>().ActivateAsync(new(executable, source,
                KernelServing.DefaultSlotName, "publish-" + identity.ArtifactId, WorkflowActivationSource.Publishing, 0), token);
            Check(published.Succeeded, $"publication of {identity.ArtifactId} succeeded");
        }

        async Task RefuseReplacedIdentityAsync(IServiceProvider shellServices, KernelServing serving, IServiceProvider runtimeServices,
            IRuntimeKernelExecutablePinStore pins)
        {
            var old = ExecutableOf("reused-artifact-id", "reused-definition", WriteLine("first-body", Literal("text", "old")));
            var newRoot = WriteLine("replacement-body", Literal("text", "replacement"));
            var replacement = new WorkflowExecutable(old.Identity with { ArtifactHash = "different-full-hash" }, newRoot,
                old.ResumeTargets, old.CreatedAt, old.CompatibilityMetadata, old.IncidentStrategy);
            var material = runtimeServices.GetRequiredService<IWorkflowExecutableStore>();
            await material.SaveAsync(old, token);
            var selectedStart = KernelWorkflowActor.Start(NativeExecutionModel.Start(NativeExecutableMapper.Map(old)));
            Check(await material.DeleteAsync(old.Identity.ArtifactId, token), "old unreferenced material was removed before protection");
            await material.SaveAsync(replacement, token);
            var protector = new KernelChildBirthProtection(shellServices.GetRequiredService<IServiceScopeFactory>(), Scope, scope, serving,
                shellServices.GetRequiredService<KernelShellStorage>(), TimeProvider.System,
                shellServices.GetRequiredService<ILogger<KernelChildBirthProtection>>());
            var decision = await protector.ProtectAsync([new(scope.Tenant, new(new string('A', 64)), selectedStart.Type,
                selectedStart.TypeData, "fixture-issuance")], token);
            Check(decision == ChildBirthProtectionOutcome.Refused &&
                !(await pins.ListPinnedExecutableArtifactIdsAsync(token)).Contains(replacement.Identity.ArtifactId),
                "an exact selection refused replacement material under a reused artifact ID");
        }
    }

    private static async Task<T> WaitAsync<T>(Func<ValueTask<T>> read, Func<T, bool> completed, CancellationToken token)
    {
        while (true)
        {
            var value = await read();
            if (completed(value)) return value;
            await Task.Delay(50, token);
        }
    }

    private static async Task StopAsync(KernelShellRuntime shell)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await new StopKernelShell(shell).TerminateAsync(stop.Token);
    }

    private static string Connection(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Supply a dedicated test database in {name}.");

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
