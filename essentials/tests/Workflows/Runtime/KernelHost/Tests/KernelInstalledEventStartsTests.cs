using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.Hosting;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Serialization.Core;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using static Elsa.Workflows.Runtime.KernelHost.Tests.KernelPublishedExecuteTests;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class KernelInstalledEventStartsTests
{
    private const string FoundationDatabase = "KERNEL_EVENT_FOUNDATION_POSTGRES";
    private const string RuntimeDatabase = "KERNEL_EVENT_RUNTIME_POSTGRES";
    private const string Definition = "event-subscriber";
    private static readonly PersistenceScope Persistence = new(PersistenceScope.DefaultValue);
    private static readonly Contract.BoundScope Tenant = EmbeddedTenant.Bind(PersistenceScope.DefaultValue);
    private static readonly TriggerScope Scope = new(Persistence, Tenant.Tenant);

    [Fact]
    public Task Sqlite_retains_an_accepted_Event_start_through_republish_and_restart() => RunAsync(false);

    [SkippableFact]
    public Task Postgres_retains_an_accepted_Event_start_through_republish_and_restart()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true);
    }

    private static async Task RunAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = deadline.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-installed-events-");
        try
        {
            var foundation = postgres ? Environment.GetEnvironmentVariable(FoundationDatabase) : null;
            var runtime = postgres ? Environment.GetEnvironmentVariable(RuntimeDatabase)! :
                new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false }.ToString();
            if (postgres) Assert.NotEqual(new NpgsqlConnectionStringBuilder(foundation).Database,
                new NpgsqlConnectionStringBuilder(runtime).Database);
            var provider = postgres ? "PostgreSql" : "Sqlite";
            var path = directory is null ? null : Path.Combine(directory.FullName, "foundation.db");
            var first = Subscriber(flowchart: true);
            var replacement = Subscriber(flowchart: false);
            var producer = Producer("producer-old", "old");
            var nextProducer = Producer("producer-new", null);
            var wrongName = Producer("producer-wrong-case", "ignored", eventName: "changed");
            var gate = new RelayGate { Hold = true };
            PublicationActivationResult original;

            await using (var firstApp = OpenShell())
            {
                var services = firstApp.Services.GetRequiredService<IServiceScopeFactory>();
                await using (var initialize = await services.CreateAsyncScopeAsync(Persistence))
                {
                    await initialize.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
                    await initialize.ServiceProvider.GetRequiredService<PublishingSnapshotReviewDbContext>().Database.MigrateAsync(token);
                }
                var shell = firstApp.Services.GetRequiredService<KernelShellRuntime>();
                await shell.StartAsync(token);
                try
                {
                    await firstApp.StartAsync(token);
                    original = await PublishAsync(services, first, 0, null);
                    Assert.True(original.Succeeded, original.Failure?.Message);
                    foreach (var emitter in new[] { producer, nextProducer, wrongName })
                        Assert.True((await PublishAsync(services, emitter, 0, null)).Succeeded);
                    await ExecuteAsync(firstApp, producer);
                    var port = firstApp.Services.GetRequiredService<KernelShellStorage>().BindEventStarts(Tenant);
                    await UntilAsync(async () => (await port.ListAsync(null, token)) is
                        [{ Revision: var revision, Registration.ArtifactId: "subscriber-flowchart" }] && revision == original.Slot.Revision);
                    await using var read = await services.CreateAsyncScopeAsync(Persistence);
                    Assert.Null(await read.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>()
                        .FindAsync(Scope with { TenantId = "different" }, original.Slot.SlotId, original.Slot.Revision, token));
                }
                finally { await shell.StopAsync(token); }
            }

            // The slot commits with the exact new desired registration, but no provider reply reaches the publisher.
            // Retrying after a full restart recovers that same generation, while the earlier selected target is still old.
            PublicationActivationResult interrupted;
            await using (var staging = OpenShell())
            {
                var storage = staging.Services.GetRequiredService<KernelShellStorage>();
                await storage.InstallAsync(token, (_, _) => ValueTask.FromResult(false));
                gate.InterruptRevision = original.Slot.Revision + 1;
                interrupted = await PublishAsync(staging.Services.GetRequiredService<IServiceScopeFactory>(),
                    replacement, original.Slot.Revision, Tenant.Tenant);
                Assert.False(interrupted.Succeeded);
                Assert.Equal(PublicationFailureCodes.ProjectionActivationFailed, interrupted.Failure!.Code);
                Assert.Equal(PublicationStatus.Candidate, interrupted.Publication.Status);
                Assert.True(interrupted.Slot.Revision > original.Slot.Revision, interrupted.Failure.Message);
                Assert.False(await storage.BindEventStarts(Tenant).ConfirmDrainedAsync(original.Slot.SlotId, original.Slot.Revision, token));
                await using var read = await staging.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var roots = await read.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().ListMaterialRetentionRootIdsAsync(token);
                Assert.Contains(first.Identity.ArtifactId, roots);
                Assert.Contains(replacement.Identity.ArtifactId, roots);
            }

            gate.Hold = false;
            await using (var restarted = OpenShell())
            {
                var restartedShell = restarted.Services.GetRequiredService<KernelShellRuntime>();
                await restartedShell.StartAsync(token);
                try
                {
                    await restarted.StartAsync(token);
                    var services = restarted.Services.GetRequiredService<IServiceScopeFactory>();
                    await using (var owner = await services.CreateAsyncScopeAsync(Persistence))
                    {
                        var serving = await owner.ServiceProvider.GetRequiredService<PublicationActivator>()
                            .CompleteAsync(Definition, KernelServing.DefaultSlotName, token);
                        Assert.True(serving.Succeeded, serving.Failure?.Message);
                        Assert.Equal(interrupted.Slot.Revision, serving.Slot.Revision);
                        Assert.Equal(PublicationStatus.Active, serving.Publication!.Status);
                        Assert.Equal(interrupted.Publication.PublicationId, serving.Publication.PublicationId);
                        var old = await owner.ServiceProvider.GetRequiredService<IPublicationRecordStore>()
                            .FindAsync(original.Publication.PublicationId, token);
                        Assert.Equal(PublicationStatus.Retired, old!.Status);
                    }
                    await ExecuteAsync(restarted, wrongName);
                    await ExecuteAsync(restarted, nextProducer);
                    var runs = restarted.Services.GetRequiredService<KernelRuns>();
                    await UntilAsync(async () =>
                    {
                        var seen = new Dictionary<string, string?>(StringComparer.Ordinal);
                        await foreach (var (actor, run) in runs.ListAsync(Definition, token))
                        {
                            if (actor.Header.Status != Contract.ActorStatus.Finished) return false;
                            var output = await runs.ReadTerminalOutputAsync(actor.Header.Ref, actor.Header.EndedAt!.Value, true, token);
                            if (output?.Values is not { } json) return false;
                            Assert.True(seen.TryAdd(run.Workflow, JsonNode.Parse(json)!["seen"]?.GetValue<string>()), "duplicate subscriber birth");
                        }
                        if (seen.Count < 2) return false;
                        Assert.Equal(2, seen.Count);
                        Assert.Equal("old", seen[first.Identity.ArtifactId]);
                        Assert.Null(seen[replacement.Identity.ArtifactId]);
                        return true;
                    });
                    await UntilAsync(async () =>
                    {
                        await using var read = await services.CreateAsyncScopeAsync(Persistence);
                        var publications = read.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>();
                        return await publications.FindAsync(Scope, original.Slot.SlotId, original.Slot.Revision, token) is null;
                    });
                }
                finally { await restartedShell.StopAsync(token); }
            }

            await using (var staging = OpenShell())
            {
                var storage = staging.Services.GetRequiredService<KernelShellStorage>();
                await storage.InstallAsync(token, (_, _) => ValueTask.FromResult(false));
                gate.InterruptRevision = interrupted.Slot.Revision + 1;
                gate.FailBeforeCommit = true;
                await using var scope = await staging.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var unpublish = scope.ServiceProvider.GetRequiredService<UnpublishPublicationSlotRequestHandler>();
                await Assert.ThrowsAsync<PublicationActivationException>(() => unpublish.UnpublishAsync(Definition, KernelServing.DefaultSlotName, token));
                Assert.False(await storage.BindEventStarts(Tenant)
                    .ConfirmDrainedAsync(interrupted.Slot.SlotId, interrupted.Slot.Revision, token));
            }

            await using (var final = OpenShell())
            {
                var shell = final.Services.GetRequiredService<KernelShellRuntime>();
                await shell.StartAsync(token);
                try
                {
                    var port = final.Services.GetRequiredService<KernelShellStorage>().BindEventStarts(Tenant);
                    Assert.True(await port.ConfirmDrainedAsync(interrupted.Slot.SlotId, interrupted.Slot.Revision, token));
                    await using var scope = await final.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                    var unpublish = scope.ServiceProvider.GetRequiredService<UnpublishPublicationSlotRequestHandler>();
                    var tombstone = await unpublish.UnpublishAsync(Definition, KernelServing.DefaultSlotName, token);
                    Assert.Null(tombstone.ActiveActivationId);
                    Assert.Equal(interrupted.Slot.Revision + 1, tombstone.Revision);
                    var retired = await scope.ServiceProvider.GetRequiredService<IPublicationRecordStore>()
                        .FindAsync(interrupted.Publication.PublicationId, token);
                    Assert.Equal(PublicationStatus.Retired, retired!.Status);
                }
                finally { await shell.StopAsync(token); }
            }

            WebApplication OpenShell()
            {
                var builder = WebApplication.CreateBuilder();
                builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
                builder.Logging.SetMinimumLevel(LogLevel.Warning);
                var services = builder.Services;
                services.AddElsaEndpoints();
                services.AddHttpContextAccessor();
                services.AddAuthentication("KernelExecuteFixture")
                    .AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("KernelExecuteFixture", _ => { });
                services.AddFoundationIdentityAbstractions(options =>
                    options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "KernelExecuteFixture" });
                services.AddAuthorization();
                services.AddSingleton<IWorkflowExecutableStartPolicy, FixturePolicy>();
                services.AddSingleton<IWellKnownTypeRegistry, FixtureTypes>();
                services.AddWorkflowRuntime();
                services.AddRuntimeEntityFrameworkCore(new()
                {
                    Provider = provider, ConnectionString = runtime,
                    RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32)
                });
                services.AddPublishingEntityFrameworkCore(new() { Provider = provider, ConnectionString = runtime });
                services.AddScoped<PublicationActivator>();
                services.AddScoped<UnpublishPublicationSlotRequestHandler>();
                var api = new WorkflowsRuntimeApiFeature();
                api.ConfigureServices(services);
                var storage = KernelShellStorage.CreateEventStartsQualification(provider, path, foundation);
                new WorkflowsRuntimeKernelHostingFeature().ConfigureServices(services, storage, protectChildExecutables: true);
                services.Replace(ServiceDescriptor.Singleton(sp => new KernelEventPublicationRelay(sp.GetRequiredService<IServiceScopeFactory>(), Scope,
                    Tenant, sp.GetRequiredService<KernelServing>().Capabilities, () => new PausableAdmissions(storage.BindEventStarts(Tenant), gate),
                    TimeProvider.System, sp.GetRequiredService<ILogger<KernelEventPublicationRelay>>())));
                new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);
                api.PostConfigureServices(services);
                var app = builder.Build();
                app.UseAuthentication();
                app.UseAuthorization();
                WorkflowsRuntimeApi.MapWorkflowsRuntimeApi(app);
                return app;
            }

            async Task<PublicationActivationResult> PublishAsync(IServiceScopeFactory scopes, WorkflowExecutable executable, long previous, string? tenant)
            {
                await using var scope = await scopes.CreateAsyncScopeAsync(Persistence);
                var id = executable.Identity;
                await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable, token);
                var now = DateTimeOffset.UtcNow;
                var activationId = "publish-" + id.ArtifactId;
                var slotName = KernelServing.DefaultSlotName;
                var slotId = WorkflowActivationSlotIdentity.Create(id.DefinitionId, slotName);
                var referenceId = WorkflowActivationReferenceIdentity.Create(activationId);
                var source = new WorkflowExecutableSourceReference(referenceId, id.ArtifactId, "definition", id.DefinitionId, id.ArtifactVersion,
                    id.DefinitionId, id.DefinitionVersionId, id.ArtifactVersion, now, now, WorkflowExecutableReferenceScope.Published,
                    ActivationId: activationId, SlotId: slotId, TenantId: tenant);
                var journal = new PublicationRecord(activationId, slotId, id.DefinitionId, id.DefinitionVersionId, id.ArtifactId,
                    referenceId, previous, PublicationStatus.Candidate, now, null, null, null, slotName);
                return await scope.ServiceProvider.GetRequiredService<PublicationActivator>().ActivateAsync(new(journal, executable, source), token);
            }

            async Task ExecuteAsync(WebApplication app, WorkflowExecutable executable)
            {
                var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
                using var http = new HttpClient { BaseAddress = new(address) };
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/runtime/workflows/executables/{executable.Identity.ArtifactId}/execute")
                    { Content = JsonContent.Create(new { }) };
                request.Headers.Add("X-Fixture-Subject", "accepted");
                using var response = await http.SendAsync(request, token);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(token));
            }

            async Task UntilAsync(Func<Task<bool>> condition)
            {
                while (!await condition()) await Task.Delay(30, token);
            }
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static WorkflowExecutable Subscriber(bool flowchart)
    {
        var start = Node("start", Types.Event, [Literal("EventName", "Changed", "String")]);
        var output = SetOutput("seen", "seen", Result("value", "start", "Payload"));
        var root = flowchart ? Node("chart", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "start",
                connections = new[] { new { source = new { nodeId = "start", port = "Done" }, target = new { nodeId = "seen" } } }
            }), slots: [Slot("Flowchart.Activities", start, output)]) : Sequence("sequence", start, output);
        return ExecutableOf(flowchart ? "subscriber-flowchart" : "subscriber-native", Definition, root);
    }

    private static WorkflowExecutable Producer(string artifact, string? payload, string eventName = "Changed") =>
        ExecutableOf(artifact, artifact, Node("emit", "Elsa.Activities.Primitives.Activities.PublishEvent",
            [Literal("EventName", eventName, "String"), Literal("Payload", payload)]));

    private sealed class RelayGate
    {
        internal volatile bool Hold;
        internal long? InterruptRevision;
        internal bool FailBeforeCommit;
    }

    private sealed class PausableAdmissions(IEventStartAdmissions inner, RelayGate gate) : IEventStartAdmissions
    {
        public async Task<EventRegistrationResult> ApplyAsync(string slot, long revision, IReadOnlyList<EventStartRegistration> items, CancellationToken token)
        {
            if (gate.InterruptRevision == revision && gate.FailBeforeCommit)
            {
                gate.InterruptRevision = null;
                throw new IOException("Injected interruption before provider registration.");
            }
            var result = await inner.ApplyAsync(slot, revision, items, token);
            if (gate.InterruptRevision == revision)
            {
                gate.InterruptRevision = null;
                throw new IOException("Injected loss of the committed provider acknowledgment.");
            }
            return result;
        }

        public Task<IReadOnlyList<EventStartTarget>> ListAsync(string? after, CancellationToken token) => inner.ListAsync(after, token);

        public Task<Contract.Admission> AdmitAsync(string target, Func<EventStartTarget, Contract.StartSpec> start, ProtectRootStartAsync protect,
            CancellationToken token) => gate.Hold
            ? Task.FromResult(new Contract.Admission(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.NodeBusy))
            : inner.AdmitAsync(target, start, protect, token);

        public Task<bool> ConfirmDrainedAsync(string slot, long revision, CancellationToken token) => inner.ConfirmDrainedAsync(slot, revision, token);
    }
}
