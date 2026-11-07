using System.Text;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Known canonical data vectors + real SQLite mechanics, not acceptance or producer authority.</summary>
public sealed class BusinessMessageCanonicalCodecTests
{
    private readonly BusinessMessageCanonicalCodec codec = new();

    [Fact]
    public void Actual_adapter_recomputes_header_and_work_fingerprints_from_the_owned_canonical_content()
    {
        var read = codec.ReadHeader(SourceJson); var work = codec.ReadWork(WorkJson);
        Assert.Equal(HeaderFingerprint, read.Snapshot.HeaderFingerprint); Assert.Equal(1, read.Snapshot.Revision);
        Assert.Equal(WorkFingerprint, work.Fingerprint); Assert.Equal(WorkId, work.WorkId);
        Assert.Equal(RuntimeDeliveryWorkRole.Plan, work.Role); Assert.Single(codec.Work);
        Assert.DoesNotContain(codec.Work, item => item.Role == RuntimeDeliveryWorkRole.Dispatch);
        codec.ValidateClaimable(SourceJson, work);
        var changed = JsonNode.Parse(SourceJson)!; changed["payloadSlot"]!["payload"] = JsonNode.Parse("{\"tampered\":true}");
        Assert.Throws<InvalidDataException>(() => codec.ReadHeader(changed.ToJsonString()));
    }

    [Theory]
    [InlineData("immutable-revision")] [InlineData("self-asserted-fingerprint")]
    public async Task Matching_source_and_work_projections_cannot_override_independently_decoded_header_expectations(string corrupt)
    {
        await using var fixture = await Fixture.CreateAsync(codec);
        var source = await fixture.Context.RuntimeIngressSources.SingleAsync(); var work = await fixture.Context.RuntimeDeliveryWorks.SingleAsync();
        if (corrupt == "immutable-revision") { source.HeaderRevision = 2; work.SourceHeaderRevision = 2; }
        else { source.HeaderFingerprint = "self-asserted"; work.SourceHeaderFingerprint = "self-asserted"; }
        await fixture.Context.SaveChangesAsync(); fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ClaimAsync(fixture.Request()).AsTask());
        Assert.Equal(0, (await fixture.Context.RuntimeDeliveryWorks.AsNoTracking().SingleAsync()).FencingToken);
        Assert.Equal(1, (await fixture.Context.RuntimeIngressSources.AsNoTracking().SingleAsync()).Revision);
    }

    [Fact]
    public async Task Canonical_buffered_plan_data_can_be_claimed_without_enabling_source_commit_or_delivery_capability()
    {
        await using var fixture = await Fixture.CreateAsync(codec);
        var claim = Assert.Single(await fixture.Store.ClaimAsync(fixture.Request()));
        Assert.Equal(WorkId, claim.WorkId); Assert.Equal(WorkFingerprint, claim.ContentFingerprint);
        Assert.Equal(RuntimeDeliveryWorkRole.Plan, claim.Role); Assert.Equal(1, claim.SourceRevision);
        Assert.Equal(2, (await fixture.Context.RuntimeIngressSources.AsNoTracking().SingleAsync()).Revision);
        Assert.False(((IRuntimeDeliverySourceStore)fixture.Store).CanParticipate(null!));
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Store.CommitAsync(null!).AsTask());
    }

    [Fact]
    public void Retired_payload_metadata_is_readable_but_cannot_authorize_old_pending_planning_or_turn_into_JSON_null()
    {
        var work = codec.ReadWork(WorkJson); var node = JsonNode.Parse(SourceJson)!;
        var row = node["progress"]![0]!.AsObject(); row.Remove("discovery"); row["status"] = 3;
        row["resolvedAtUtcTicks"] = DateTimeOffset.UnixEpoch.AddDays(1).UtcTicks;
        node["payloadSlot"] = new JsonObject { ["state"] = 1, ["operationId"] = O("retire"), ["operationFingerprint"] = O("retained-proof") };
        var metadata = codec.ReadHeader(node.ToJsonString());
        Assert.Equal(HeaderFingerprint, metadata.Snapshot.HeaderFingerprint); Assert.Equal(1, metadata.Status);
        Assert.Throws<InvalidDataException>(() => codec.ValidateClaimable(node.ToJsonString(), work));
        node["progress"] = JsonNode.Parse(SourceJson)!["progress"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => codec.ReadHeader(node.ToJsonString()));
    }

    private static string O(string value) => Convert.ToBase64String(Encoding.Unicode.GetBytes(value)); // ASCII fixture values only.
    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }
    private sealed class Owner(RuntimeDeliveryDiscoveryPartition partition) : IRuntimeDeliveryDiscoveryOwner
    { public RuntimeDeliveryDiscoveryPartition CaptureCurrent() => partition; }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(1); }
    private sealed class Fixture(SqliteConnection connection, RuntimeSqliteDbContext context, RuntimeDeliveryDiscoveryPartition partition,
        EfRuntimeDeliverySourceStore store) : IAsyncDisposable
    {
        internal RuntimeSqliteDbContext Context => context;
        internal EfRuntimeDeliverySourceStore Store => store;
        internal RuntimeDeliveryClaimRequest Request() => new(partition.Scope, ["BusinessMessage"], ["business-message.plan-definition/1"], "worker", TimeSpan.FromSeconds(10), 1, partition);
        internal static async Task<Fixture> CreateAsync(BusinessMessageCanonicalCodec codec)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync(); var read = codec.ReadHeader(SourceJson); var work = codec.ReadWork(WorkJson);
            var address = read.Snapshot.Address; var partition = read.Partition; var projection = RuntimeDeliveryDiscoveryProjection.Create(partition);
            var scope = EfRelationalIdentity.Encode(address.Scope.Value); var scopeHash = EfRelationalIdentity.Hash(address.Scope.Value);
            var family = EfRelationalIdentity.Encode(address.Family); var familyHash = EfRelationalIdentity.Hash(address.Family);
            var sourceId = EfRelationalIdentity.Encode(address.SourceId); var sourceIdHash = EfRelationalIdentity.Hash(address.SourceId);
            context.RuntimeIngressSources.Add(new()
            {
                Id = EfRelationalIdentity.HashLengthFramed(address.Scope.Value, address.Family, address.SourceId), ScopeKey = scope, ScopeKeyHash = scopeHash,
                SourceFamily = family, SourceFamilyHash = familyHash, SourceId = sourceId, SourceIdHash = sourceIdHash,
                RunKind = (int)partition.RunKind, AuthorityPartitionKey = partition.AuthorityPartitionKey,
                FullDiscoveryIdentity = projection.Identity, FullDiscoveryIdentityHash = projection.Hash,
                HeaderVersion = 1, HeaderRevision = 1, RequiredParticipantVersion = 1, HeaderFingerprint = HeaderFingerprint, Status = read.Status,
                RecordedAtUtcTicks = DateTimeOffset.UnixEpoch.UtcTicks, AcceptanceOperationId = "fixture-only", AcceptanceOperationFingerprint = "fixture-only",
                OperationProofJson = "{}", ContentJson = SourceJson, SchemaVersion = RuntimeDeliverySourceEfModule.SchemaVersion, Revision = 1
            });
            context.RuntimeDeliveryWorks.Add(new()
            {
                Id = EfRelationalIdentity.HashLengthFramed(address.Scope.Value, address.Family, address.SourceId, work.WorkId), ScopeKey = scope, ScopeKeyHash = scopeHash,
                SourceFamily = family, SourceFamilyHash = familyHash, SourceId = sourceId, SourceIdHash = sourceIdHash,
                RunKind = (int)partition.RunKind, AuthorityPartitionKey = partition.AuthorityPartitionKey,
                FullDiscoveryIdentity = projection.Identity, FullDiscoveryIdentityHash = projection.Hash, SourceHeaderRevision = 1, SourceHeaderFingerprint = HeaderFingerprint,
                WorkId = EfRelationalIdentity.Encode(work.WorkId), WorkIdHash = EfRelationalIdentity.Hash(work.WorkId),
                WorkKind = EfRelationalIdentity.Encode(work.Kind), WorkKindHash = EfRelationalIdentity.Hash(work.Kind), Role = (int)work.Role, RequiredParticipantVersion = 1,
                ContentFingerprint = WorkFingerprint, Status = RuntimeDeliveryWorkTransport.Pending, RecordedAtUtcTicks = work.NotBefore.UtcTicks,
                ClaimableIsEligible = true, ClaimableAtUtcTicks = work.NotBefore.UtcTicks, ContentJson = WorkJson, SchemaVersion = RuntimeDeliverySourceEfModule.SchemaVersion, Revision = 1
            });
            await context.SaveChangesAsync(); context.ChangeTracker.Clear();
            var store = new EfRuntimeDeliverySourceStore(context, new Access(partition.Scope), new Owner(partition), [new RuntimeIngressCanonicalFamilyReader(codec)], new Clock());
            return new(connection, context, partition, store);
        }
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await connection.DisposeAsync(); }
    }

    // Fixed ASCII-only external wire vectors, independently generated from the documented v1 layout.
    // Runtime domain tests pin these fingerprints using the actual factories. No Runtime internal friend is added.
    private const string HeaderFingerprint = "elsa.business-message-header/v1:09a1954565d6a077fde9e696ce8b3d60985cf7669776e2f9ba38b3999438a5d6";
    private const string WorkFingerprint = "elsa.business-message-work/v1:748baeae8ab08aa5c515729aafc4d02bad4771f157bfe752ec9d5464a8f9ada4";
    private const string WorkId = "elsa.business-message-work-address/v1:9cb1a856fbe7dab18069a83e97debf1e493df2708decfa76627363e4020653d5";
    private const string SourceJson = """
        {
          "version": 1,
          "header": {
            "version": 1,
            "family": "BusinessMessage",
            "messageId": "bQBlAHMAcwBhAGcAZQA=",
            "receiptId": "cgBlAGMAZQBpAHAAdAA=",
            "context": {
              "scope": "cwB0AG8AcgBlAA==",
              "tenant": null,
              "runKind": 2,
              "executionPartition": null,
              "authority": {
                "system": "cwB5AHMAdABlAG0A",
                "root": "cgBvAG8AdAA=",
                "metadata": []
              },
              "testScope": null
            },
            "producerNamespace": "cAByAG8AZAB1AGMAZQByAA==",
            "configurationRevision": "YwBvAG4AZgBpAGcA",
            "submission": {
              "name": "payment",
              "correlationKey": "001",
              "fingerprint": "elsa.message-submission/v1:89acd8cbe7c67fd77753bd86aceec10f4af0c9da0729bccd5fca0eb3afa4ce4a",
              "payloadCommitment": "elsa.business-message-payload/v1:74234e98afe7498fb5daf1f36ac2d78acc339464f950703b8c019892f982b90b"
            },
            "policy": {
              "version": 1,
              "defaultBufferSeconds": 86400,
              "maximumBufferSeconds": 2592000,
              "deduplicationSeconds": 604800
            },
            "acceptedAtUtcTicks": 621355968000000000,
            "bufferUntilUtcTicks": 621356832000000000,
            "interestEpoch": 1,
            "cohortDigest": "elsa.business-message-cohort/v1:7deb9de79124594ae3f395f671a7395d9a527514eb2a94310f5d6d826a80ad75",
            "definitions": [
              {
                "namespace": "VwBvAHIAawBmAGwAbwB3AEQAZQBmAGkAbgBpAHQAaQBvAG4A",
                "id": "YQBjAGMAbwB1AG4AdABpAG4AZwA="
              }
            ]
          },
          "payloadSlot": {
            "state": 0,
            "payload": null
          },
          "progress": [
            {
              "namespace": "VwBvAHIAawBmAGwAbwB3AEQAZQBmAGkAbgBpAHQAaQBvAG4A",
              "id": "YQBjAGMAbwB1AG4AdABpAG4AZwA=",
              "status": 0,
              "discovery": {
                "generation": 1,
                "notBeforeUtcTicks": 621355968000000000
              }
            }
          ]
        }
        """;
    private const string WorkJson = """
        {
          "version": 1,
          "family": "BusinessMessage",
          "kind": "business-message.plan-definition/1",
          "scope": {
            "persistenceScope": "cwB0AG8AcgBlAA==",
            "tenant": null,
            "testScopeId": null
          },
          "messageId": "bQBlAHMAcwBhAGcAZQA=",
          "namespace": "VwBvAHIAawBmAGwAbwB3AEQAZQBmAGkAbgBpAHQAaQBvAG4A",
          "definitionId": "YQBjAGMAbwB1AG4AdABpAG4AZwA=",
          "headerFingerprint": "elsa.business-message-header/v1:09a1954565d6a077fde9e696ce8b3d60985cf7669776e2f9ba38b3999438a5d6",
          "cohortDigest": "elsa.business-message-cohort/v1:7deb9de79124594ae3f395f671a7395d9a527514eb2a94310f5d6d826a80ad75",
          "generation": 1,
          "notBeforeUtcTicks": 621355968000000000,
          "workId": "elsa.business-message-work-address/v1:9cb1a856fbe7dab18069a83e97debf1e493df2708decfa76627363e4020653d5"
        }
        """;
}
