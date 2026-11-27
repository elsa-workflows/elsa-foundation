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
using Elsa.Workflows.Runtime.Api.Models;
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

    [Fact]
    public Task Sqlite_commits_an_Ephemeral_publisher_and_its_first_Event_together() => RunAsync(false, combined: true);

    [SkippableFact]
    public Task Postgres_commits_an_Ephemeral_publisher_and_its_first_Event_together()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true, combined: true);
    }

    [Fact]
    public Task Sqlite_adopts_the_active_stock_Event_without_republishing() => RunAsync(false, combined: true, adoptStock: true);

    [SkippableFact]
    public Task Postgres_adopts_the_active_stock_Event_without_republishing()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)), "Requires two dedicated PostgreSQL databases.");
        return RunAsync(true, combined: true, adoptStock: true);
    }

    private static async Task RunAsync(bool postgres, bool combined = false, bool adoptStock = false)
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
            var first = Subscriber(flowchart: true, mode: PersistenceMode.Durable);
            var replacement = Subscriber(flowchart: false);
            var ephemeral = Subscriber(flowchart: true, mode: PersistenceMode.Ephemeral, definition: "ephemeral-event-subscriber");
            var producer = Producer("producer-old", "old");
            var nextProducer = Producer("producer-new", null, mode: combined ? PersistenceMode.Ephemeral : null);
            var wrongName = Producer("producer-wrong-case", "ignored", eventName: "changed");
            var gate = new RelayGate { Hold = true };
            PublicationActivationResult original;

            if (adoptStock)
            {
                await AdoptStockAsync();
                return;
            }

            await using (var firstApp = OpenShell())
            {
                var services = firstApp.Services.GetRequiredService<IServiceScopeFactory>();
                await MigrateAsync(services);
                var shell = firstApp.Services.GetRequiredService<KernelShellRuntime>();
                await shell.StartAsync(token);
                try
                {
                    await firstApp.StartAsync(token);
                    original = await PublishAsync(services, first, 0, null);
                    Assert.True(original.Succeeded, original.Failure?.Message);
                    var unsupported = await PublishAsync(services, ephemeral, 0, null);
                    Assert.False(unsupported.Succeeded);
                    Assert.Null(unsupported.Slot.ActiveActivationId);
                    Assert.Contains("Balanced or Durable persistence", unsupported.Failure!.Message, StringComparison.Ordinal);
                    foreach (var emitter in new[] { producer, nextProducer, wrongName })
                        Assert.True((await PublishAsync(services, emitter, 0, null)).Succeeded);
                    await ExecuteAsync(firstApp, producer);
                    var port = firstApp.Services.GetRequiredService<KernelShellStorage>().BindEventStarts(Tenant);
                    await UntilAsync(async () => (await port.ListAsync(null, token)) is
                        [{ Revision: var revision, Registration: { ArtifactId: "subscriber-flowchart", Mode: "Durable" } }] &&
                        revision == original.Slot.Revision);
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

            gate.DefaultMode = PersistenceMode.Durable;
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
                    var pendingStarts = Assert.IsAssignableFrom<IOrderedEventStartAdmissions>(
                        restarted.Services.GetRequiredService<KernelShellStorage>().BindEventStarts(Tenant));
                    var firstTail = await pendingStarts.ReadPendingTailAsync(token);
                    var olderTarget = Assert.Single(await pendingStarts.ReadPendingPageAsync(0, firstTail, 1, token));
                    Assert.Equal(first.Identity.ArtifactId, olderTarget.Target.Registration.ArtifactId);
                    await ExecuteAsync(restarted, wrongName);
                    await ExecuteAsync(restarted, nextProducer);
                    await UntilAsync(async () => await pendingStarts.ReadPendingTailAsync(token) > firstTail);
                    var nextTail = await pendingStarts.ReadPendingTailAsync(token);
                    var frozen = Assert.Single(await pendingStarts.ReadPendingPageAsync(0, firstTail, 16, token));
                    Assert.Equal(olderTarget, frozen);
                    var newerTarget = Assert.Single(await pendingStarts.ReadPendingPageAsync(firstTail, nextTail, 16, token));
                    Assert.Equal(replacement.Identity.ArtifactId, newerTarget.Target.Registration.ArtifactId);

                    // The older target uses its complete item deadline. The relay must still visit and settle its healthy suffix.
                    gate.BlockedTarget = olderTarget.Target.Id;
                    gate.Hold = false;
                    var runs = restarted.Services.GetRequiredService<KernelRuns>();
                    await gate.TimedOut.Task.WaitAsync(token);
                    await UntilAsync(async () =>
                    {
                        await foreach (var (actor, run) in runs.ListAsync(Definition, token))
                            if (run.Workflow == replacement.Identity.ArtifactId && actor.Header.Status == Contract.ActorStatus.Finished)
                                return true;
                        return false;
                    });
                    Assert.Equal(olderTarget, Assert.Single(await pendingStarts.ReadPendingPageAsync(0, firstTail, 16, token)));
                    gate.BlockedTarget = null;
                    await UntilAsync(async () =>
                    {
                        var seen = new Dictionary<string, string?>(StringComparer.Ordinal);
                        await foreach (var (actor, run) in runs.ListAsync(Definition, token))
                        {
                            if (actor.Header.Status != Contract.ActorStatus.Finished) return false;
                            var output = await runs.ReadTerminalOutputAsync(actor.Header.Ref, actor.Header.EndedAt!.Value, true, token);
                            if (output?.Values is not { } json) return false;
                            var journal = await runs.ReadSequencedJournalAsync(actor.Header.Ref, 0, null, 100, token);
                            var eventRevision = Assert.Single(journal!.Items.Where(item => Completed(item, "start"))).Revision;
                            var outputRevision = Assert.Single(journal.Items.Where(item => Completed(item, "seen"))).Revision;
                            if (run.Workflow == first.Identity.ArtifactId) Assert.True(outputRevision > eventRevision);
                            else Assert.Equal(eventRevision, outputRevision);
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

            await using (var withdrawal = OpenShell())
            {
                var storage = withdrawal.Services.GetRequiredService<KernelShellStorage>();
                await storage.InstallAsync(token, (_, _) => ValueTask.FromResult(false));
                var services = withdrawal.Services.GetRequiredService<IServiceScopeFactory>();
                var subscriber = ExecutableOf("subscriber-withdrawal", Definition,
                    Node("start", Types.Event, [Literal("EventName", "Changed", "String")]));
                var registered = await PublishAsync(services, subscriber, interrupted.Slot.Revision + 1, Tenant.Tenant);
                Assert.True(registered.Succeeded, registered.Failure?.Message);
                var port = storage.BindEventStarts(Tenant);
                Assert.False(await port.ConfirmDrainedAsync(registered.Slot.SlotId, registered.Slot.Revision, token));

                gate.InterruptRevision = registered.Slot.Revision + 1;
                var plain = ExecutableOf("subscriber-without-event", Definition, SetOutput("seen", "seen", Literal("value", "plain")));
                var pending = await PublishAsync(services, plain, registered.Slot.Revision, Tenant.Tenant);
                Assert.False(pending.Succeeded);
                Assert.Equal(PublicationStatus.Candidate, pending.Publication.Status);
                Assert.Equal(registered.Slot.Revision + 1, pending.Slot.Revision);
                Assert.False(await port.ConfirmDrainedAsync(registered.Slot.SlotId, registered.Slot.Revision, token));

                await using var scope = await services.CreateAsyncScopeAsync(Persistence);
                var deactivated = await scope.ServiceProvider.GetRequiredService<UnpublishPublicationSlotRequestHandler>()
                    .UnpublishAsync(Definition, KernelServing.DefaultSlotName, token);
                Assert.Null(deactivated.ActiveActivationId);
                Assert.Equal(pending.Slot.Revision + 1, deactivated.Revision);
                var publications = scope.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>();
                var tombstone = await publications.FindAsync(Scope, deactivated.SlotId, deactivated.Revision, token);
                Assert.NotNull(tombstone);
                Assert.Null(tombstone.Source);
                Assert.True(await port.ConfirmDrainedAsync(registered.Slot.SlotId, registered.Slot.Revision, token));
            }

            await CheckStartupSupersessionAsync(afterProvider: false);
            await CheckStartupSupersessionAsync(afterProvider: true);
            await CheckActiveStartupSupersessionAsync();
            await CheckCurrentStartupConflictAsync();

            async Task CheckStartupSupersessionAsync(bool afterProvider)
            {
                var definition = afterProvider ? "startup-successor-ack" : "startup-successor-stale";
                var previous = ExecutableOf(definition + "-previous", definition,
                    Node("start", Types.Event, [Literal("EventName", "Changed", "String")]));
                var successor = ExecutableOf(definition + "-new", definition,
                    Node("start", Types.Event, [Literal("EventName", "Changed", "String")]));
                await using var competitor = OpenShell();
                var storage = competitor.Services.GetRequiredService<KernelShellStorage>();
                await storage.InstallAsync(token, (_, _) => ValueTask.FromResult(false));
                var competitorScopes = competitor.Services.GetRequiredService<IServiceScopeFactory>();
                var published = await PublishAsync(competitorScopes, previous, 0, Tenant.Tenant);
                Assert.True(published.Succeeded, published.Failure?.Message);
                await using var competition = await competitorScopes.CreateAsyncScopeAsync(Persistence);
                var services = competition.ServiceProvider;
                gate.InterruptRevision = published.Slot.Revision + 1;
                gate.FailBeforeCommit = true;
                await Assert.ThrowsAsync<PublicationActivationException>(() => services.GetRequiredService<UnpublishPublicationSlotRequestHandler>()
                    .UnpublishAsync(definition, KernelServing.DefaultSlotName, token));
                var reviewed = await services.GetRequiredService<IWorkflowActivationAuthority>()
                    .FindAsync(definition, KernelServing.DefaultSlotName, token);
                Assert.NotNull(reviewed);
                Assert.Null(reviewed.ActiveActivationId);
                var publications = services.GetRequiredService<IRuntimeKernelEventPublicationStore>();
                var through = await publications.ReadScanTailAsync(Scope, token);
                Assert.Contains(await publications.ListScanAsync(Scope, false, 0, through, 16, token),
                    item => item.Publication.SlotId == reviewed.SlotId && item.Publication.Revision == reviewed.Revision);

                var race = new ApplyRace(reviewed.SlotId, reviewed.Revision, afterProvider, async () =>
                {
                    if (afterProvider) gate.InterruptRevision = reviewed.Revision + 1;
                    var result = await PublishAsync(competitorScopes, successor, reviewed.Revision, Tenant.Tenant);
                    Assert.Equal(!afterProvider, result.Succeeded);
                    Assert.Equal(reviewed.Revision + 1, result.Slot.Revision);
                    if (afterProvider) Assert.Equal(PublicationStatus.Candidate, result.Publication.Status);
                });
                gate.Race = race;
                await using var starting = OpenShell();
                var startup = starting.Services.GetRequiredService<KernelShellRuntime>();
                await startup.StartAsync(token);
                try
                {
                    Assert.Equal(afterProvider ? EventRegistrationResult.Applied : EventRegistrationResult.Stale, race.Observed);
                    var current = await publications.FindCurrentAsync(Scope, reviewed.SlotId, token);
                    Assert.Equal(reviewed.Revision + 1, current!.Revision);
                    Assert.Equal(successor.Identity.ArtifactId, current.Source!.Executable.ArtifactId);
                    var currentThrough = await publications.ReadScanTailAsync(Scope, token);
                    Assert.True(currentThrough > through);
                    Assert.DoesNotContain(await publications.ListScanAsync(Scope, false, 0, currentThrough, 16, token),
                        item => item.Publication.SlotId == current.SlotId);
                    Assert.Equal(EventRegistrationResult.Stale,
                        await starting.Services.GetRequiredService<KernelShellStorage>().BindEventStarts(Tenant)
                            .ApplyAsync(reviewed.SlotId, reviewed.Revision, [], token));

                    if (!afterProvider)
                    {
                        await using var serving = await starting.Services.GetRequiredService<IServiceScopeFactory>()
                            .CreateAsyncScopeAsync(Persistence);
                        var currentServices = serving.ServiceProvider;
                        var unpublish = new UnpublishPublicationSlotRequestHandler(new SlotSnapshot(reviewed),
                            currentServices.GetRequiredService<IWorkflowActivationCoordinator>(), currentServices.GetRequiredService<IPublicationRecordStore>(),
                            currentServices.GetRequiredService<IWorkflowExecutableStore>(), currentServices.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(),
                            TimeProvider.System);
                        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() =>
                            unpublish.UnpublishAtRevisionAsync(definition, KernelServing.DefaultSlotName, reviewed.Revision, token));
                        Assert.Equal(PublicationFailureCodes.SlotRevisionConflict, refusal.Code);
                        var stillServing = await services.GetRequiredService<IWorkflowActivationAuthority>()
                            .FindAsync(definition, KernelServing.DefaultSlotName, token);
                        Assert.Equal(current.Source.ActivationId, stillServing!.ActiveActivationId);
                    }
                }
                finally { await startup.StopAsync(token); }
            }

            async Task CheckActiveStartupSupersessionAsync()
            {
                const string definition = "startup-active-slot";
                var executable = ExecutableOf(definition, definition,
                    Node("start", Types.Event, [Literal("EventName", "Changed", "String")]));
                await using var competitor = OpenShell();
                await competitor.Services.GetRequiredService<KernelShellStorage>().InstallAsync(token, (_, _) => ValueTask.FromResult(false));
                var competitorScopes = competitor.Services.GetRequiredService<IServiceScopeFactory>();
                gate.InterruptRevision = 1;
                gate.FailBeforeCommit = true;
                var pending = await PublishAsync(competitorScopes, executable, 0, Tenant.Tenant);
                Assert.False(pending.Succeeded);
                Assert.NotNull(pending.Slot.ActiveActivationId);

                var race = new ApplyRace(pending.Slot.SlotId, pending.Slot.Revision, false, async () =>
                {
                    await using var scope = await competitorScopes.CreateAsyncScopeAsync(Persistence);
                    var withdrawn = await scope.ServiceProvider.GetRequiredService<UnpublishPublicationSlotRequestHandler>()
                        .UnpublishAsync(definition, KernelServing.DefaultSlotName, token);
                    Assert.Null(withdrawn.ActiveActivationId);
                    Assert.Equal(pending.Slot.Revision + 1, withdrawn.Revision);
                });
                gate.Race = race;
                await using var starting = OpenShell();
                var startup = starting.Services.GetRequiredService<KernelShellRuntime>();
                await startup.StartAsync(token);
                try
                {
                    Assert.Equal(EventRegistrationResult.Stale, race.Observed);
                    Assert.Null(starting.Services.GetRequiredService<KernelServing>().Find(pending.Slot.SlotId));
                }
                finally { await startup.StopAsync(token); }
            }

            async Task CheckCurrentStartupConflictAsync()
            {
                const string definition = "startup-current-conflict";
                var executable = ExecutableOf(definition, definition,
                    Node("start", Types.Event, [Literal("EventName", "Changed", "String")]));
                await using var staging = OpenShell();
                var storage = staging.Services.GetRequiredService<KernelShellStorage>();
                await storage.InstallAsync(token, (_, _) => ValueTask.FromResult(false));
                var scopes = staging.Services.GetRequiredService<IServiceScopeFactory>();
                gate.InterruptRevision = 1;
                gate.FailBeforeCommit = true;
                var pending = await PublishAsync(scopes, executable, 0, Tenant.Tenant);
                Assert.False(pending.Succeeded);
                Assert.Equal(EventRegistrationResult.Applied, await storage.BindEventStarts(Tenant)
                    .ApplyAsync(pending.Slot.SlotId, pending.Slot.Revision, [], token));
                var relay = staging.Services.GetRequiredService<KernelEventPublicationRelay>();
                var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() => relay.RestoreAsync(token));
                Assert.Contains(nameof(EventRegistrationResult.Conflict), conflict.Message, StringComparison.Ordinal);
            }

            async Task AdoptStockAsync()
            {
                PublicationActivationResult published;
                PublicationActivationResult excluded;
                await using (var stock = OpenShell(stock: true))
                {
                    var services = stock.Services.GetRequiredService<IServiceScopeFactory>();
                    await MigrateAsync(services);
                    published = await PublishAsync(services, first, 0, null);
                    excluded = await PublishAsync(services, ephemeral, 0, null);
                    Assert.True(published.Succeeded, published.Failure?.Message);
                    Assert.True(excluded.Succeeded, excluded.Failure?.Message);
                    Assert.True((await PublishAsync(services, producer, 0, null)).Succeeded);
                    Assert.Null(await ReadGenerationAsync(services, published.Slot));
                    Assert.Null(await ReadGenerationAsync(services, excluded.Slot));
                }

                // The stock publications use the retained Runtime database. The combined owner installs fresh
                // Actor Foundation storage and loses acknowledgment after its first provider registration.
                gate.InterruptRevision = published.Slot.Revision;
                RuntimeKernelEventPublication staged;
                await using (var interrupted = OpenShell())
                {
                    var shell = interrupted.Services.GetRequiredService<KernelShellRuntime>();
                    await Assert.ThrowsAsync<InvalidOperationException>(() => shell.StartAsync(token));
                    var services = interrupted.Services.GetRequiredService<IServiceScopeFactory>();
                    staged = Assert.IsType<RuntimeKernelEventPublication>(await ReadGenerationAsync(services, published.Slot));
                    Assert.Equal(published.Publication.PublicationId, staged.Source!.ActivationId);
                    Assert.Equal(published.Slot.Revision, staged.Revision);
                    Assert.Equal(PersistenceMode.Durable, staged.Source.Mode);
                    Assert.Null(await ReadGenerationAsync(services, excluded.Slot));
                }

                await using var owner = OpenShell();
                var installed = owner.Services.GetRequiredService<KernelShellRuntime>();
                await installed.StartAsync(token);
                try
                {
                    await owner.StartAsync(token);
                    var services = owner.Services.GetRequiredService<IServiceScopeFactory>();
                    await using (var read = await services.CreateAsyncScopeAsync(Persistence))
                    {
                        var authority = read.ServiceProvider.GetRequiredService<IWorkflowActivationAuthority>();
                        Assert.Equal(published.Slot, await authority.FindAsync(Definition, KernelServing.DefaultSlotName, token));
                        Assert.Equal(excluded.Slot, await authority.FindAsync(ephemeral.Identity.DefinitionId, KernelServing.DefaultSlotName, token));
                        var records = read.ServiceProvider.GetRequiredService<IPublicationRecordStore>();
                        Assert.Equal(published.Publication, await records.FindAsync(published.Publication.PublicationId, token));
                    }
                    var recovered = Assert.IsType<RuntimeKernelEventPublication>(await ReadGenerationAsync(services, published.Slot));
                    Assert.Equal(staged.Source, recovered.Source);
                    Assert.Equal(staged.Entries, recovered.Entries);
                    Assert.Null(await ReadGenerationAsync(services, excluded.Slot));

                    await ExecuteAsync(owner, producer);
                    var pending = Assert.IsAssignableFrom<IOrderedEventStartAdmissions>(
                        owner.Services.GetRequiredService<KernelShellStorage>().BindEventStarts(Tenant));
                    await UntilAsync(async () => await pending.ReadPendingTailAsync(token) > 0);
                    var tail = await pending.ReadPendingTailAsync(token);
                    var target = Assert.Single(await pending.ReadPendingPageAsync(0, tail, 16, token));
                    Assert.Equal(published.Slot.SlotId, target.Target.Slot);
                    Assert.Equal(published.Slot.Revision, target.Target.Revision);
                    Assert.Equal(first.Identity.ArtifactId, target.Target.Registration.ArtifactId);
                    gate.Hold = false;

                    var runs = owner.Services.GetRequiredService<KernelRuns>();
                    await UntilAsync(async () =>
                    {
                        await foreach (var (actor, run) in runs.ListAsync(Definition, token))
                            if (run.Workflow == first.Identity.ArtifactId && actor.Header.Status == Contract.ActorStatus.Finished)
                            {
                                var output = await runs.ReadTerminalOutputAsync(actor.Header.Ref, actor.Header.EndedAt!.Value, true, token);
                                Assert.Equal("old", JsonNode.Parse(output!.Values!)!["seen"]!.GetValue<string>());
                                return true;
                            }
                        return false;
                    });

                    // Ephemeral remains normally executable. Its start Event never joined the adopted cohort.
                    await ExecuteAsync(owner, ephemeral, expectTerminal: false);
                }
                finally { await installed.StopAsync(token); }
            }

            async Task MigrateAsync(IServiceScopeFactory services)
            {
                await using var initialize = await services.CreateAsyncScopeAsync(Persistence);
                await initialize.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
                await initialize.ServiceProvider.GetRequiredService<PublishingSnapshotReviewDbContext>().Database.MigrateAsync(token);
            }

            async Task<RuntimeKernelEventPublication?> ReadGenerationAsync(IServiceScopeFactory services, WorkflowActivationSlot slot)
            {
                await using var read = await services.CreateAsyncScopeAsync(Persistence);
                return await read.ServiceProvider.GetRequiredService<IRuntimeKernelEventPublicationStore>()
                    .FindAsync(Scope, slot.SlotId, slot.Revision, token);
            }

            WebApplication OpenShell(bool stock = false)
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
                services.RemoveAll<PersistenceModeOptions>();
                services.AddSingleton(new PersistenceModeOptions { Mode = gate.DefaultMode });
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
                var storage = stock ? KernelShellStorage.Create(provider, path, foundation)
                    : combined ? KernelShellStorage.CreateSharedActivationQualification(provider, path, foundation)
                    : KernelShellStorage.CreateEventStartScanQualification(provider, path, foundation);
                new WorkflowsRuntimeKernelHostingFeature().ConfigureServices(services, storage, protectChildExecutables: true);
                if (!stock)
                    services.Replace(ServiceDescriptor.Singleton(sp => new KernelEventPublicationRelay(sp.GetRequiredService<IServiceScopeFactory>(), Scope,
                        Tenant, sp.GetRequiredService<KernelServing>().Capabilities, () => new PausableAdmissions(storage.BindEventStarts(Tenant), gate),
                        TimeProvider.System, sp.GetRequiredService<ILogger<KernelEventPublicationRelay>>(), retainsReceipts: true, orderedScan: true,
                        adoptExisting: combined)));
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

            async Task ExecuteAsync(WebApplication app, WorkflowExecutable executable, bool expectTerminal = true)
            {
                var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
                using var http = new HttpClient { BaseAddress = new(address) };
                var inline = executable.PersistenceMode == PersistenceMode.Ephemeral;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                while (true)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"/runtime/workflows/executables/{executable.Identity.ArtifactId}/execute")
                        { Content = JsonContent.Create(new { }) };
                    request.Headers.Add("X-Fixture-Subject", "accepted");
                    using var response = await http.SendAsync(request, token);
                    if (!inline)
                    {
                        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(token));
                        return;
                    }
                    var receipt = await response.Content.ReadFromJsonAsync<WorkflowExecutionStartDispatchView>(token);
                    Assert.NotNull(receipt);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests && receipt.PersistenceOutcome == "Unavailable" &&
                        System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(8))
                    {
                        await Task.Delay(100, token);
                        continue;
                    }
                    Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(token));
                    Assert.Equal("Committed", receipt.PersistenceOutcome);
                    Assert.NotNull(receipt.WorkflowExecutionId);
                    if (!expectTerminal) return;
                    var runs = app.Services.GetRequiredService<KernelRuns>();
                    await UntilAsync(async () =>
                    {
                        await foreach (var (actor, run) in runs.ListAsync(executable.Identity.DefinitionId, token))
                            if (run.Workflow == executable.Identity.ArtifactId && actor.Header.Status == Contract.ActorStatus.Finished)
                                return true;
                        return false;
                    });
                    return;
                }
            }

            async Task UntilAsync(Func<Task<bool>> condition)
            {
                while (!await condition()) await Task.Delay(30, token);
            }
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static WorkflowExecutable Subscriber(bool flowchart, PersistenceMode? mode = null, string definition = Definition)
    {
        var start = Node("start", Types.Event, [Literal("EventName", "Changed", "String")]);
        var output = SetOutput("seen", "seen", Result("value", "start", "Payload"));
        var root = flowchart ? Node("chart", Types.Flowchart,
            structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "start",
                connections = new[] { new { source = new { nodeId = "start", port = "Done" }, target = new { nodeId = "seen" } } }
            }), slots: [Slot("Flowchart.Activities", start, output)]) : Sequence("sequence", start, output);
        var model = ExecutableOf(mode == PersistenceMode.Ephemeral ? "subscriber-ephemeral" : flowchart ? "subscriber-flowchart" : "subscriber-native",
            definition, root);
        return new(model.Identity, model.RootActivity, model.ResumeTargets, model.CreatedAt, model.CompatibilityMetadata,
            model.InputContract, model.Dependencies, model.RuntimeRequirements, model.StorageDriverRequirements, model.IncidentStrategy,
            persistenceMode: mode);
    }

    private static bool Completed(Contract.SequencedJournalEntry entry, string node)
    {
        if (entry.Entry is null) return false;
        using var document = JsonDocument.Parse(entry.Entry);
        var row = document.RootElement;
        return row.ValueKind == JsonValueKind.Object && row.TryGetProperty("NativeStructure", out _) &&
            row.GetProperty("Kind").GetInt32() == 3 && row.GetProperty("Node").GetString() == node;
    }

    private static WorkflowExecutable Producer(string artifact, string? payload, string eventName = "Changed", PersistenceMode? mode = null)
    {
        var model = ExecutableOf(artifact, artifact, Node("emit", "Elsa.Activities.Primitives.Activities.PublishEvent",
            [Literal("EventName", eventName, "String"), Literal("Payload", payload)]));
        return mode is null ? model : new(model.Identity, model.RootActivity, model.ResumeTargets, model.CreatedAt,
            model.CompatibilityMetadata, model.InputContract, model.Dependencies, model.RuntimeRequirements,
            model.StorageDriverRequirements, model.IncidentStrategy, persistenceMode: mode);
    }

    private sealed class RelayGate
    {
        internal volatile bool Hold;
        internal PersistenceMode DefaultMode = PersistenceMode.Balanced;
        internal long? InterruptRevision;
        internal bool FailBeforeCommit;
        internal ApplyRace? Race;
        internal volatile string? BlockedTarget;
        internal readonly TaskCompletionSource TimedOut = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record ApplyRace(string Slot, long Revision, bool AfterProvider, Func<Task> Win)
    {
        internal EventRegistrationResult? Observed;
    }

    private sealed class SlotSnapshot(WorkflowActivationSlot snapshot) : IWorkflowActivationAuthority
    {
        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken) =>
            ValueTask.FromResult<WorkflowActivationSlot?>(snapshot);
        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string workflowDefinitionId, string slotName,
            WorkflowActivationSource source, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class PausableAdmissions(IEventStartAdmissions inner, RelayGate gate) : IOrderedEventStartAdmissions
    {
        public async Task<EventRegistrationResult> ApplyAsync(string slot, long revision, IReadOnlyList<EventStartRegistration> items, CancellationToken token)
        {
            if (gate.InterruptRevision == revision && gate.FailBeforeCommit)
            {
                gate.InterruptRevision = null;
                throw new IOException("Injected interruption before provider registration.");
            }
            var race = gate.Race;
            if (race is not null && race.Slot == slot && race.Revision == revision)
            {
                gate.Race = null;
                if (!race.AfterProvider) await race.Win();
            }
            else race = null;
            var result = await inner.ApplyAsync(slot, revision, items, token);
            if (race is not null)
            {
                race.Observed = result;
                if (race.AfterProvider) await race.Win();
            }
            if (gate.InterruptRevision == revision)
            {
                gate.InterruptRevision = null;
                throw new IOException("Injected loss of the committed provider acknowledgment.");
            }
            return result;
        }

        public Task<IReadOnlyList<EventStartTarget>> ListAsync(string? after, CancellationToken token) => inner.ListAsync(after, token);

        public Task<long> ReadPendingTailAsync(CancellationToken token) => ((IOrderedEventStartAdmissions)inner).ReadPendingTailAsync(token);

        public Task<IReadOnlyList<EventStartScanItem>> ReadPendingPageAsync(long after, long through, int limit, CancellationToken token) =>
            ((IOrderedEventStartAdmissions)inner).ReadPendingPageAsync(after, through, limit, token);

        public async Task<Contract.Admission> AdmitAsync(string target, Func<EventStartTarget, Contract.StartSpec> start, ProtectRootStartAsync protect,
            CancellationToken token)
        {
            if (gate.Hold)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.NodeBusy);
            if (gate.BlockedTarget == target)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    gate.TimedOut.TrySetResult();
                    throw;
                }
            }
            return await inner.AdmitAsync(target, start, protect, token);
        }

        public Task<int> PruneSettledAsync(CancellationToken cancellationToken) => inner.PruneSettledAsync(cancellationToken);

        public Task<bool> ConfirmDrainedAsync(string slot, long revision, CancellationToken token) => inner.ConfirmDrainedAsync(slot, revision, token);
    }
}
