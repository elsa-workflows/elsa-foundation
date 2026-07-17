using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowExecutableStoreTests
{
    // The same contract assertions run against two host-selected providers (real Groundwork SQLite and
    // an in-memory document store). Identical behavior proves the executable bridge is provider-neutral,
    // and round-tripping a nested node tree proves the full executable graph survives serialization.
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task RoundTrips_Across_Providers(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        var dependency = Executable("artifact-2");
        await store.SaveAsync(Executable("artifact-1", dependencies: [dependency]));
        await store.SaveAsync(dependency);

        var found = await store.FindAsync("artifact-1");
        Assert.NotNull(found);
        Assert.Equal("artifact-1", found!.Identity.ArtifactId);
        Assert.Equal("definition-1", found.Identity.DefinitionId);
        Assert.Equal("hash-artifact-1", found.Identity.ArtifactHash);

        // Nested tree survives: root + child slot + child node.
        Assert.Equal("root", found.RootActivity.ExecutableNodeId);
        Assert.Equal("Elsa.Sequence", found.RootActivity.ActivityType);
        var slot = Assert.Single(found.RootActivity.ChildSlots);
        Assert.Equal("Body", slot.Name);
        var child = Assert.Single(slot.Activities);
        Assert.Equal("child", child.ExecutableNodeId);

        // Compiled input binding survives.
        var binding = child.InputBindings["to"];
        Assert.Equal(RuntimeInputBindingSource.WorkflowRequest, binding.Source);
        Assert.Equal("customerEmail", binding.WorkflowRequest!.MemberKey);

        // Raw descriptor payload survives as JSON.
        Assert.Equal("Send", found.RootActivity.Descriptor.Payload.GetProperty("kind").GetString());

        // Resume targets and recomputed projections survive.
        Assert.Equal("node-child", found.ResumeTargets["resume-1"].ExecutableNodeId);
        Assert.Equal(2, found.Nodes.Count);
        Assert.True(found.NodesById.ContainsKey("child"));
        Assert.Equal("slice-1", found.CompatibilityMetadata["slice"]);
        Assert.Equal(
            ["Elsa.Activities.SendEmailDescriptor", "Elsa.Activities.SequenceDescriptor"],
            found.RuntimeRequirements.Select(x => x.ConsumerKey).Order(StringComparer.Ordinal));
        Assert.Equal("sample.external", Assert.Single(found.StorageDriverRequirements).DriverKey);
        var retainedDependency = Assert.Single(found.Dependencies);
        Assert.Equal("artifact-2", retainedDependency.ArtifactId);
        Assert.Equal("hash-artifact-2", retainedDependency.ArtifactHash);
        Assert.Equal("root", Assert.Single(retainedDependency.DispatchNodeIds));

        var all = await store.ListAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(new[] { "artifact-1", "artifact-2" }, all.Select(x => x.Identity.ArtifactId).OrderBy(x => x));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Save_Is_Idempotent_By_ArtifactId(string provider)
    {
        // ADR 0038: artifacts are content-addressed and immutable. A second save under the same artifact id (a
        // behaviorally identical republish) leaves the existing artifact untouched rather than overwriting it.
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Executable("artifact-1", artifactVersion: "1"));
        await store.SaveAsync(Executable("artifact-1", artifactVersion: "2"));

        var found = await store.FindAsync("artifact-1");
        Assert.Equal("1", found!.Identity.ArtifactVersion);
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task SaveAsync_UsesCreateOnlyWrite_And_DoesNotOverwriteConcurrentWinner()
    {
        var documentStore = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.Create());
        var competingStore = new GroundworkWorkflowExecutableStore(documentStore, GroundworkTestSerialization.Serializer);
        var interceptingStore = new InterceptingDocumentStore(documentStore)
        {
            OnBeforeSave = async request =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind, request.DocumentKind);
                Assert.Equal("artifact-1", request.Id);
                Assert.Equal(0, request.ExpectedVersion);
                await competingStore.SaveAsync(Executable("artifact-1", artifactVersion: "winner"));
            }
        };
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(interceptingStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Executable("artifact-1", artifactVersion: "loser"));

        var winner = await competingStore.FindAsync("artifact-1");
        Assert.Equal("winner", winner!.Identity.ArtifactVersion);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Find_Returns_Null_When_Absent(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        Assert.Null(await store.FindAsync("missing"));
        Assert.Empty(await store.ListAsync());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Delete_Removes_Artifact(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

        await store.SaveAsync(Executable("artifact-1"));
        Assert.True(await store.DeleteAsync("artifact-1"));
        Assert.Null(await store.FindAsync("artifact-1"));
        Assert.Empty(await store.ListAsync());
        Assert.False(await store.DeleteAsync("artifact-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Root_Write_Leases_And_Deletion_Guards_Are_Mutually_Exclusive(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Executable("artifact-1"));
        var firstLease = await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer-1", now.AddMinutes(1), now);
        var secondLease = await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer-2", now.AddMinutes(1), now);

        Assert.NotNull(firstLease);
        Assert.NotNull(secondLease);
        Assert.True(await store.RenewRootWriteLeaseAsync(firstLease!, now.AddMinutes(2), now));
        Assert.True(await store.RenewRootWriteLeaseAsync(firstLease!, now.AddMinutes(3), now));
        Assert.Null(await store.TryBeginDeletionAsync("artifact-1", "gc-1", now.AddMinutes(1), now));

        await store.ReleaseRootWriteLeaseAsync(firstLease!);
        Assert.Null(await store.TryBeginDeletionAsync("artifact-1", "gc-1", now.AddMinutes(1), now));
        await store.ReleaseRootWriteLeaseAsync(secondLease!);

        var guard = await store.TryBeginDeletionAsync("artifact-1", "gc-1", now.AddMinutes(1), now);
        Assert.NotNull(guard);
        Assert.Null(await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer-3", now.AddMinutes(1), now));
        Assert.True(await store.CancelDeletionAsync(guard!));
        Assert.NotNull(await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer-3", now.AddMinutes(1), now));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Expired_And_Stale_Guards_Cannot_Mutate_Current_Ownership(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Executable("artifact-1"));
        var expiredLease = (await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer", now.AddSeconds(1), now))!;
        var currentLease = (await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer", now.AddMinutes(2), now.AddSeconds(2)))!;

        Assert.NotEqual(expiredLease.ConcurrencyToken, currentLease.ConcurrencyToken);
        Assert.False(await store.RenewRootWriteLeaseAsync(expiredLease, now.AddMinutes(3), now.AddSeconds(2)));
        await store.ReleaseRootWriteLeaseAsync(expiredLease);
        Assert.Null(await store.TryBeginDeletionAsync("artifact-1", "gc", now.AddMinutes(1), now.AddSeconds(2)));

        await store.ReleaseRootWriteLeaseAsync(currentLease);
        var expiredGuard = (await store.TryBeginDeletionAsync("artifact-1", "gc", now.AddSeconds(3), now.AddSeconds(2)))!;
        var replacementLease = await store.TryAcquireRootWriteLeaseAsync("artifact-1", "replacement", now.AddMinutes(1), now.AddSeconds(4));

        Assert.NotNull(replacementLease);
        Assert.False(await store.CancelDeletionAsync(expiredGuard));
        Assert.False(await store.DeleteAsync(expiredGuard, now.AddSeconds(4)));
        Assert.NotNull(await store.FindAsync("artifact-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Duplicate_Owner_Ids_Do_Not_Bypass_Fencing_To_Extend_Expiry(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Executable("artifact-1"));
        var lease = (await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer", now.AddSeconds(1), now))!;
        var duplicateLease = (await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer", now.AddMinutes(1), now))!;
        Assert.Equal(lease.ConcurrencyToken, duplicateLease.ConcurrencyToken);

        var guard = await store.TryBeginDeletionAsync("artifact-1", "gc", now.AddSeconds(3), now.AddSeconds(2));
        Assert.NotNull(guard);
        var duplicateGuard = (await store.TryBeginDeletionAsync("artifact-1", "gc", now.AddMinutes(1), now.AddSeconds(2)))!;
        Assert.Equal(guard!.ConcurrencyToken, duplicateGuard.ConcurrencyToken);

        Assert.NotNull(await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer-2", now.AddMinutes(1), now.AddSeconds(4)));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Guarded_Delete_Requires_The_Current_Unexpired_Guard(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Executable("artifact-1"));
        var guard = (await store.TryBeginDeletionAsync("artifact-1", "gc", now.AddMinutes(1), now))!;
        var forged = guard with { ConcurrencyToken = "stale" };

        Assert.False(await store.DeleteAsync(forged, now));
        Assert.NotNull(await store.FindAsync("artifact-1"));
        Assert.True(await store.DeleteAsync(guard, now));
        Assert.Null(await store.FindAsync("artifact-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Concurrent_Root_Lease_And_Deletion_Guard_Have_Exactly_One_Winner(string provider)
    {
        await using var fixture = CreateStore(provider);
        var synchronizedStore = new FirstTwoExecutableLoadsBarrierDocumentStore(fixture.DocumentStore);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(synchronizedStore, GroundworkTestSerialization.Serializer);
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Executable("artifact-1"));
        var leaseTask = store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer", now.AddMinutes(1), now).AsTask();
        var guardTask = store.TryBeginDeletionAsync("artifact-1", "gc", now.AddMinutes(1), now).AsTask();
        await Task.WhenAll(leaseTask, guardTask);
        var lease = await leaseTask;
        var guard = await guardTask;

        Assert.NotEqual(lease is null, guard is null);
        if (guard is not null)
            Assert.True(await store.DeleteAsync(guard, now));
        else
            Assert.NotNull(await store.FindAsync("artifact-1"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Missing_Artifact_Never_Authorizes_A_Root_Or_Deletion(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

        Assert.Null(await store.TryAcquireRootWriteLeaseAsync("missing", "writer", now.AddMinutes(1), now));
        Assert.Null(await store.TryBeginDeletionAsync("missing", "gc", now.AddMinutes(1), now));
    }

    [Fact]
    public async Task Retention_Guards_Survive_Restart_And_Remain_Usable()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-executable-guard-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        WorkflowExecutableRootWriteLease lease;
        WorkflowExecutableDeletionGuard guard;

        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                await store.SaveAsync(Executable("artifact-1"));
                lease = (await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer", now.AddMinutes(1), now))!;
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                Assert.Null(await store.TryBeginDeletionAsync("artifact-1", "gc", now.AddMinutes(1), now));
                await store.ReleaseRootWriteLeaseAsync(lease);
                guard = (await store.TryBeginDeletionAsync("artifact-1", "gc", now.AddMinutes(1), now))!;
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                Assert.Null(await store.TryAcquireRootWriteLeaseAsync("artifact-1", "writer-2", now.AddMinutes(1), now));
                Assert.True(await store.DeleteAsync(guard, now));
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SourceReferenceStore_RoundTrips_And_Filters(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableSourceReferenceStore store =
            new GroundworkWorkflowExecutableSourceReferenceStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                fixture.BoundedDocumentStore);
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Reference("ref-1", "artifact-1", WorkflowExecutableReferenceScope.Published, publishedAt: now));
        await store.SaveAsync(Reference("ref-2", "artifact-1", WorkflowExecutableReferenceScope.TestRun, expiresAt: now.AddMinutes(30)));
        await store.SaveAsync(Reference("ref-3", "artifact-2", WorkflowExecutableReferenceScope.TestRun, expiresAt: now.AddMinutes(-1)));

        var byArtifact = await store.ListByArtifactAsync("artifact-1");
        Assert.Equal(new[] { "ref-1", "ref-2" }, byArtifact.Select(r => r.SourceReferenceId).OrderBy(x => x));

        var live = await store.ListAsync(liveOnly: true, now: now);
        Assert.Equal(new[] { "ref-1", "ref-2" }, live.Select(r => r.SourceReferenceId).OrderBy(x => x));

        var published = await store.ListAsync(scope: WorkflowExecutableReferenceScope.Published, now: now);
        Assert.Equal("ref-1", Assert.Single(published).SourceReferenceId);

        // Layout sidecar survives the round-trip.
        var reloaded = await store.FindAsync("ref-1");
        Assert.Equal("node-a", Assert.Single(reloaded!.Layout).NodeId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task SourceReferenceStore_Retire_Expiry_And_Unreferenced_Primitives(string provider)
    {
        await using var fixture = CreateStore(provider);
        IWorkflowExecutableSourceReferenceStore store =
            new GroundworkWorkflowExecutableSourceReferenceStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                fixture.BoundedDocumentStore);
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(Reference("ref-live", "artifact-1", WorkflowExecutableReferenceScope.Published, publishedAt: now));
        await store.SaveAsync(Reference("ref-expired", "artifact-2", WorkflowExecutableReferenceScope.TestRun, expiresAt: now.AddMinutes(-1)));
        await store.SaveAsync(Reference("ref-to-retire", "artifact-3", WorkflowExecutableReferenceScope.Published, publishedAt: now));

        Assert.True(await store.RetireAsync("ref-to-retire", now, "manual"));
        var retired = await store.FindAsync("ref-to-retire");
        Assert.Equal(now, retired!.DeletedAt);
        Assert.Equal("manual", retired.DeletedReason);

        var swept = await store.DeleteExpiredOrRetiredAsync(now);
        Assert.Equal(new[] { "ref-expired", "ref-to-retire" }, swept.OrderBy(x => x));
        Assert.Null(await store.FindAsync("ref-expired"));

        var unreferenced = await store.ListUnreferencedArtifactIdsAsync(["artifact-1", "artifact-2", "artifact-3"], now);
        // artifact-1 still has a live reference; 2 and 3 lost theirs to the sweep.
        Assert.Equal(new[] { "artifact-2", "artifact-3" }, unreferenced.OrderBy(x => x));
    }

    [Fact]
    public async Task SourceReferenceStore_ScopedList_UsesDeclaredScopeRoute()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        IWorkflowExecutableSourceReferenceStore store =
            new GroundworkWorkflowExecutableSourceReferenceStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                queries);

        await store.ListAsync(scope: WorkflowExecutableReferenceScope.Published);

        var query = Assert.Single(queries.Observed);
        Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, query.DocumentKind);
        Assert.Equal(ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesByScopeQuery, query.QueryIdentity);
        var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
        Assert.Equal(ElsaRuntimeStorageManifest.ScopeField, comparison.Path);
        Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        Assert.Equal(WorkflowExecutableReferenceScope.Published.ToString(), Assert.Single(comparison.Values));
    }

    [Fact]
    public async Task SourceReferenceStore_DeleteExpiredOrRetired_UsesDeclaredGcRoutes()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        IWorkflowExecutableSourceReferenceStore store =
            new GroundworkWorkflowExecutableSourceReferenceStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                queries);
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

        await store.DeleteExpiredOrRetiredAsync(now);

        Assert.Collection(
            queries.Observed,
            expired =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.ListExpiredWorkflowExecutableSourceReferencesQuery, expired.QueryIdentity);
                var comparison = Assert.Single(Assert.Single(expired.Clauses).Comparisons);
                Assert.Equal(ElsaRuntimeStorageManifest.ExpiresAtField, comparison.Path);
                Assert.Equal(QueryComparisonOperator.LessThanOrEqual, comparison.Operator);
                Assert.Equal(now, DateTimeOffset.Parse(Assert.Single(comparison.Values)!));
                Assert.Equal(ElsaRuntimeStorageManifest.ExpiresAtField, Assert.Single(expired.Order).Path);
            },
            retired =>
            {
                Assert.Equal(ElsaRuntimeStorageManifest.ListRetiredWorkflowExecutableSourceReferencesQuery, retired.QueryIdentity);
                var comparison = Assert.Single(Assert.Single(retired.Clauses).Comparisons);
                Assert.Equal(ElsaRuntimeStorageManifest.IsRetiredField, comparison.Path);
                Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
                Assert.Equal(bool.TrueString, Assert.Single(comparison.Values));
            });
    }

    [Fact]
    public async Task SourceReferenceStore_UnreferencedArtifacts_UsesArtifactRoutePerCandidate()
    {
        await using var fixture = CreateStore("memory");
        var queries = new RecordingBoundedDocumentStore();
        IWorkflowExecutableSourceReferenceStore store =
            new GroundworkWorkflowExecutableSourceReferenceStore(
                fixture.DocumentStore,
                GroundworkTestSerialization.Serializer,
                queries);
        var now = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);

        await store.ListUnreferencedArtifactIdsAsync(["artifact-1", "artifact-1", "artifact-2"], now);

        Assert.Equal(2, queries.Observed.Count);
        Assert.Equal(
            new[] { "artifact-1", "artifact-2" },
            queries.Observed.Select(query => Assert.Single(Assert.Single(query.Clauses).Comparisons).Values.Single()!).ToArray());
        Assert.All(queries.Observed, query =>
        {
            Assert.Equal(ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind, query.DocumentKind);
            Assert.Equal(ElsaRuntimeStorageManifest.ListWorkflowExecutableSourceReferencesByArtifactQuery, query.QueryIdentity);
            var comparison = Assert.Single(Assert.Single(query.Clauses).Comparisons);
            Assert.Equal(ElsaRuntimeStorageManifest.ArtifactIdField, comparison.Path);
            Assert.Equal(QueryComparisonOperator.Equal, comparison.Operator);
        });
    }

    [Fact]
    public void Serialization_Omits_Derived_Node_Projections()
    {
        var json = GroundworkTestSerialization.Serializer.SerializeForComparison(Executable("artifact-1"));

        // RootActivity is the single source of truth; Nodes/NodesById are rebuilt on load (asserted in
        // RoundTrips_Across_Providers) and must not be persisted.
        Assert.Contains("\"rootActivity\"", json);
        Assert.DoesNotContain("\"nodesById\"", json);
        Assert.DoesNotContain("\"nodes\"", json);
    }

    [Fact]
    public async Task Published_Executable_Survives_Restart()
    {
        // DS-2 (durability): the production publish path persists compiled executables through
        // GroundworkWorkflowExecutableStore. A published artifact must survive a host restart — proven by
        // writing through one bridge instance over a file-backed SQLite database, disposing it, then reopening
        // the SAME database file with a FRESH bridge and reading the artifact (and its node tree) back.
        var dbPath = Path.Combine(Path.GetTempPath(), $"gw-executable-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);
                await store.SaveAsync(Executable("artifact-1"));
            }

            await using (var fixture = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                IWorkflowExecutableStore store = new GroundworkWorkflowExecutableStore(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

                var found = await store.FindAsync("artifact-1");
                Assert.NotNull(found);
                Assert.Equal("artifact-1", found!.Identity.ArtifactId);
                Assert.Equal("definition-1", found.Identity.DefinitionId);
                Assert.Equal("root", found.RootActivity.ExecutableNodeId);
                Assert.Equal("child", Assert.Single(Assert.Single(found.RootActivity.ChildSlots).Activities).ExecutableNodeId);
                Assert.Equal("artifact-1", Assert.Single(await store.ListAsync()).Identity.ArtifactId);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static WorkflowExecutable Executable(
        string artifactId,
        string artifactVersion = "1",
        IReadOnlyCollection<WorkflowExecutable>? dependencies = null)
    {
        var child = new ExecutableNode(
            executableNodeId: "child",
            authoredActivityId: "authored-child",
            activityType: "Elsa.SendEmail",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("Elsa.Activities.SendEmailDescriptor", RuntimeActivityDescriptor.InitialSchemaVersion, Json("""{ "kind": "Send" }""")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["to"] = new(
                    inputKey: "to",
                    targetType: new ValueTypeDescriptor("String"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.WorkflowRequest,
                    workflowRequest: new RuntimeWorkflowRequestReference("customerEmail"))
            },
            metadata: new Dictionary<string, string> { ["role"] = "leaf" });

        var root = new ExecutableNode(
            executableNodeId: "root",
            authoredActivityId: "authored-root",
            activityType: "Elsa.Sequence",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("Elsa.Activities.SequenceDescriptor", RuntimeActivityDescriptor.InitialSchemaVersion, Json("""{ "kind": "Send" }""")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot("Body", [child])]);

        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                ArtifactId: artifactId,
                DefinitionId: "definition-1",
                DefinitionVersionId: "version-1",
                ArtifactVersion: artifactVersion,
                ArtifactHash: $"hash-{artifactId}"),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-1"] = new("resume-1", "node-child", "Bookmark", new Dictionary<string, string> { ["stimulus"] = "Http" })
            },
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string> { ["slice"] = "slice-1" },
            inputContract: null,
            dependencies: dependencies?.Select(dependency => new WorkflowExecutableDependency(
                dependency.Identity.ArtifactId,
                dependency.Identity.ArtifactHash,
                ["root"])).ToArray(),
            runtimeRequirements: null,
            storageDriverRequirements: [new RuntimeStorageDriverRequirement("sample.external")]);
    }

    private static WorkflowExecutableSourceReference Reference(
        string sourceReferenceId,
        string artifactId,
        WorkflowExecutableReferenceScope scope,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: artifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: "version-1",
            SourceVersion: "1.0.0",
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactVersion: "1.0.0",
            CreatedAt: publishedAt ?? DateTimeOffset.UnixEpoch,
            PublishedAt: publishedAt,
            Scope: scope,
            ExpiresAt: expiresAt,
            Layout: [new WorkflowExecutableLayoutRecord("node-a", 1, 2, 3, 4, Json("""{ "k": "v" }"""))]);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static GroundworkDocumentStoreFixture CreateStore(string provider) =>
        GroundworkDocumentStoreFixture.Create(provider);

    private sealed class RecordingBoundedDocumentStore : IBoundedDocumentStore
    {
        public List<DocumentQuery> Observed { get; } = [];

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            Observed.Add(query);
            return Task.FromResult(new DocumentQueryResult([], 0));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Forces the two competing transitions to read the same document version before either CAS write,
    /// so the test exercises the provider's actual conflict-and-retry path rather than merely scheduling
    /// the operations concurrently.
    /// </summary>
    private sealed class FirstTwoExecutableLoadsBarrierDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        private readonly TaskCompletionSource _bothLoaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _relevantLoadCount;

        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;
        public DocumentStoreAccess Access => inner.Access;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public async Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            var envelope = await inner.LoadAsync(documentKind, id, cancellationToken);
            if (documentKind != ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind || id != "artifact-1")
                return envelope;

            var loadNumber = Interlocked.Increment(ref _relevantLoadCount);
            if (loadNumber <= 2)
            {
                if (loadNumber == 2)
                    _bothLoaded.TrySetResult();
                await _bothLoaded.Task.WaitAsync(cancellationToken);
            }

            return envelope;
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            inner.AnyAsync(query, cancellationToken);

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            inner.BeginAsync(scope, cancellationToken);
    }
}
