using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static class KernelExecutablePinContract
{
    internal static async Task RunAsync(string provider, string connectionString, string scope = "kernel-pin-tenant")
    {
        var now = new DateTimeOffset(2026, 11, 27, 1, 0, 0, TimeSpan.Zero);
        var tenant = new PersistenceScope(scope);
        var clock = new FakeTimeProvider(now);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access(tenant));
        services.AddWorkflowRuntime();
        services.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = provider, ConnectionString = connectionString, CacheWorkflowExecutables = true,
            RecoveryContinuationSigningKey = new string('r', 64), HierarchyCursorSigningKey = new string('h', 64)
        });
        services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.ArtifactCreationGracePeriod = TimeSpan.Zero);
        await using var app = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var writerScope = app.CreateAsyncScope();
        var db = writerScope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        await db.Database.MigrateAsync();
        var material = writerScope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var pins = writerScope.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();

        var dependency = Artifact("dependency");
        var selected = Artifact("selected", new WorkflowExecutableDependency(dependency.Identity.ArtifactId, dependency.Identity.ArtifactHash, ["root"]));
        var orphan = Artifact("orphan");
        foreach (var artifact in new[] { selected, dependency, orphan }) await material.SaveAsync(artifact);
        var first = new RuntimeKernelExecutablePin(tenant, tenant.Value, "actor-one", selected.Identity, "issued-one");
        var second = first with { ActorReference = "actor-two", Issuance = "issued-two" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => pins.TryPinAsync([first with { Tenant = "other-tenant" }]).AsTask());

        await Assert.ThrowsAsync<WorkflowExecutableDependencyGraphException>(() =>
            pins.TryPinAsync([first, second with { Executable = selected.Identity with { ArtifactHash = "different-hash" } }]).AsTask());
        Assert.Empty(await pins.ListPinnedExecutableArtifactIdsAsync());

        var guard = Assert.IsType<WorkflowExecutableDeletionGuard>(await material.TryBeginDeletionAsync(dependency.Identity.ArtifactId,
            "deleting-dependency", now.AddMinutes(1), now));
        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() => pins.TryPinAsync([first, second]).AsTask());
        Assert.Empty(await pins.ListPinnedExecutableArtifactIdsAsync());
        await material.CancelDeletionAsync(guard);

        await using var peerScope = app.CreateAsyncScope();
        var peer = peerScope.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contender = Task.Run(async () =>
        {
            await start.Task;
            return await peer.TryPinAsync([first, second]);
        });
        start.SetResult();
        Assert.All(await Task.WhenAll(contender, pins.TryPinAsync([first, second]).AsTask()), result => Assert.True(result));
        Assert.Equal(2, await db.KernelExecutablePins.CountAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => pins.TryPinAsync([first with { Issuance = "another-issuance" }]).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => pins.TryPinAsync([first with { Executable = orphan.Identity }]).AsTask());

        await using var reopened = app.CreateAsyncScope();
        var roots = reopened.ServiceProvider.GetRequiredService<IRuntimeKernelExecutablePinStore>();
        var collector = reopened.ServiceProvider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();
        Assert.Equal([selected.Identity.ArtifactId], await roots.ListPinnedExecutableArtifactIdsAsync());
        var tail = Assert.IsType<string>(await roots.ReadOpenTailAsync(tenant));
        var left = await roots.ListOpenAsync(tenant, null, tail, 1);
        var right = await roots.ListOpenAsync(tenant, Assert.IsType<string>(left.Next), tail, 1);
        Assert.Null(right.Next);
        Assert.Equal(new[] { first, second }, left.Items.Concat(right.Items).OrderBy(pin => pin.ActorReference, StringComparer.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => roots.ReadOpenTailAsync(new("other-scope")).AsTask());
        Assert.Equal(1, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.Null(await material.FindAsync(orphan.Identity.ArtifactId));
        Assert.NotNull(await material.FindAsync(selected.Identity.ArtifactId));
        Assert.NotNull(await material.FindAsync(dependency.Identity.ArtifactId));

        Assert.False(await roots.TryReleaseAsync(first, (_, _) => ValueTask.FromResult(RuntimeKernelExecutablePinProof.Unresolved)));
        Assert.False(await roots.TryReleaseAsync(first, (_, _) => throw new TimeoutException()));
        Assert.False(await roots.TryReleaseAsync(first, (_, _) => ValueTask.FromResult((RuntimeKernelExecutablePinProof)40)));
        await Assert.ThrowsAsync<InvalidDataException>(() => roots.TryReleaseAsync(first with { Issuance = "invalid" }, UnexpectedProof).AsTask());
        Assert.Equal(0, (await collector.SweepAsync()).DeletedArtifactCount);

        Assert.True(await roots.TryReleaseAsync(first, (observed, _) =>
        {
            Assert.Equal(first, observed);
            return ValueTask.FromResult(RuntimeKernelExecutablePinProof.BirthRetired);
        }));
        Assert.True(await roots.TryReleaseAsync(first, UnexpectedProof));
        Assert.False(await roots.TryPinAsync([first, second with { ActorReference = "third", Issuance = "issued-three" }]));
        Assert.Equal(2, await db.KernelExecutablePins.CountAsync());
        Assert.Equal(0, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.True(await roots.TryReleaseAsync(second, (observed, _) =>
        {
            Assert.Equal(second, observed);
            return ValueTask.FromResult(RuntimeKernelExecutablePinProof.IssuanceFencedAbsent);
        }));
        Assert.Empty(await roots.ListPinnedExecutableArtifactIdsAsync());
        Assert.Null(await roots.ReadOpenTailAsync(tenant));
        Assert.All(await db.KernelExecutablePins.AsNoTracking().ToArrayAsync(), row =>
        {
            Assert.Equal(2, row.Revision);
            Assert.Null(row.ArtifactId);
            Assert.Null(row.ContentJson);
            Assert.Equal(64, row.Fingerprint.Length);
        });
        Assert.Equal(2, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.False(await roots.TryPinAsync([first, second]));

        static ValueTask<RuntimeKernelExecutablePinProof> UnexpectedProof(RuntimeKernelExecutablePin _, CancellationToken __) =>
            throw new InvalidOperationException("The store must not ask for a proof of this pin.");

        WorkflowExecutable Artifact(string id, params WorkflowExecutableDependency[] dependencies) => new(
            new(id, "definition-" + id, "version-" + id, "1", "hash-" + id),
            new ExecutableNode("root", "authored", "test/activity", "1.0.0",
                new RuntimeActivityDescriptor("test", "1.0.0", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), now.AddHours(-1), new Dictionary<string, string>(),
            inputContract: null, dependencies: dependencies, incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope);
    }
}
