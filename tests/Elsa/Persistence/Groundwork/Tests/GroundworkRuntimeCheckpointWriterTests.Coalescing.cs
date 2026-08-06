using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed partial class GroundworkRuntimeCheckpointWriterTests
{
    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Groundwork_adoption_operates_on_real_durable_prepared_rows_and_cannot_be_a_no_op(
        RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 2);
        var writer = fixture.Writer;
        var before = await SnapshotPreparedAsync(writer);
        var beforeNormalized = fixture.NormalizedRawSnapshot();
        var members = before.Reservations.Select(reservation => new RuntimeCheckpointPreparedAdoptionMember(
            reservation.CommitId,
            reservation.Token.LedgerToken,
            reservation.Provenance.WorkflowCheckpointOrder,
            reservation.InputFingerprint,
            reservation.Token.CanonicalInputReference,
            reservation.ExpectedFence,
            reservation.ExpectedOrderRevision,
            reservation.ExpectedContextRevision,
            reservation.RecoveryAuthority,
            reservation.CurrentAuthorityFence,
            reservation.AuthorityRevision)).ToArray();
        var target = new RuntimeExecutionFence($"groundwork-{route}", "adoption", 2);
        var request = new RuntimeCheckpointPreparedAdoptionRequest("wf-1", route, members[^1].WorkflowCheckpointOrder, target, members);

        var receipt = await InvokeAdoptionAsync(writer, request);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted, receipt.Status);
        var adopted = await SnapshotPreparedAsync(writer);
        Assert.Equal(before.ImmutablePreparedBytes, adopted.ImmutablePreparedBytes);
        Assert.Equal(beforeNormalized, fixture.NormalizedRawSnapshot());
        Assert.Equal(before.Reservations.Select(x => x with { CurrentAuthorityFence = target, AuthorityRevision = x.AuthorityRevision + 1 }), adopted.Reservations);

        var replayRequest = request with
        {
            Members = adopted.Reservations.Select(reservation => new RuntimeCheckpointPreparedAdoptionMember(
                reservation.CommitId, reservation.Token.LedgerToken, reservation.Provenance.WorkflowCheckpointOrder,
                reservation.InputFingerprint, reservation.Token.CanonicalInputReference, reservation.ExpectedFence,
                reservation.ExpectedOrderRevision, reservation.ExpectedContextRevision, reservation.RecoveryAuthority,
                reservation.CurrentAuthorityFence, reservation.AuthorityRevision)).ToArray()
        };
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Replay, (await InvokeAdoptionAsync(writer, replayRequest)).Status);
        var replayed = await SnapshotPreparedAsync(writer);
        Assert.Equal(adopted.Reservations, replayed.Reservations);
        Assert.Equal(adopted.ImmutablePreparedBytes, replayed.ImmutablePreparedBytes);
    }

    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Groundwork_adopted_current_fence_rejects_older_or_different_ownership_without_raw_mutation(
        RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 2);
        var current = new RuntimeExecutionFence("lease-current", "owner-current", 5);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await InvokeAdoptionAsync(fixture.Writer, fixture.Request(current))).Status);
        var adoptedAtFive = fixture.RawSnapshot();
        var currentMembers = await fixture.ReadMembersAsync();
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Replay,
            (await InvokeAdoptionAsync(fixture.Writer, fixture.Request(current, currentMembers))).Status);
        Assert.Equal(adoptedAtFive, fixture.RawSnapshot());

        var newer = new RuntimeExecutionFence("lease-current", "owner-current", 6);
        Assert.Equal(RuntimeCheckpointPreparedAdoptionStatus.Adopted,
            (await InvokeAdoptionAsync(fixture.Writer, fixture.Request(newer, currentMembers))).Status);
        var adoptedAtSix = fixture.RawSnapshot();
        var newerMembers = await fixture.ReadMembersAsync();

        RuntimeExecutionFence[] rejectedTargets =
        [
            new("lease-current", "owner-current", 5),
            new("lease-current", "owner-current", 4),
            new("lease-current", "owner-other", 7),
            new("lease-other", "owner-current", 7)
        ];
        foreach (var rejected in rejectedTargets)
        {
            var receipt = await InvokeAdoptionAsync(fixture.Writer, fixture.Request(rejected, newerMembers));
            Assert.True(receipt.Status is RuntimeCheckpointPreparedAdoptionStatus.Conflict or RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost);
            Assert.Equal(adoptedAtSix, fixture.RawSnapshot());
        }
    }

    [Theory]
    [MemberData(nameof(GroundworkRejectedExactSetCases))]
    public async Task Groundwork_exact_set_adoption_rejects_provider_owned_mismatches_with_raw_documents_unchanged(
        RuntimeCheckpointRecoveryRoute route,
        string name,
        Func<GroundworkAdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest> buildRequest)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 3);
        var before = fixture.RawSnapshot();
        var receipt = await InvokeAdoptionAsync(fixture.Writer, buildRequest(fixture));

        Assert.True(
            receipt.Status is RuntimeCheckpointPreparedAdoptionStatus.Conflict or RuntimeCheckpointPreparedAdoptionStatus.OwnershipLost,
            $"Expected conflict or ownership loss for '{name}' ({route}), but received {receipt.Status}.");
        Assert.Equal(before, fixture.RawSnapshot());
    }

    [Theory]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceBound)]
    [InlineData(RuntimeCheckpointRecoveryRoute.SourceFree)]
    public async Task Groundwork_cancellation_and_mid_transaction_document_failure_roll_back_every_raw_document(RuntimeCheckpointRecoveryRoute route)
    {
        var fixture = await GroundworkAdoptionFixture.CreateAsync(route, 3);
        var before = fixture.RawSnapshot();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InvokeAdoptionAsync(fixture.Writer, fixture.Request(GroundworkFence("cancelled", 2)), cancellation.Token));
        Assert.Equal(before, fixture.RawSnapshot());

        var saves = 0;
        fixture.Interceptor.OnSaveResult = _ => ++saves == 2
            ? throw new InvalidOperationException("mid-adoption-document-write")
            : null;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeAdoptionAsync(fixture.Writer, fixture.Request(GroundworkFence("failed", 2))));

        Assert.True(saves >= 2, "The injected failure must occur after at least one staged document write.");
        Assert.Equal(before, fixture.RawSnapshot());
    }

    private static IEnumerable<(string Name, Func<GroundworkAdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest> Build)> GroundworkRejectedExactSetRequests()
    {
        yield return ("missing member", fixture => fixture.Request(GroundworkFence("missing", 2), fixture.Members[..1]));
        yield return ("extra member", fixture => fixture.Request(GroundworkFence("extra", 2), [.. fixture.Members, fixture.Members[0] with { CommitId = "extra" }]));
        yield return ("duplicate member", fixture => fixture.Request(GroundworkFence("duplicate", 2), [.. fixture.Members, fixture.Members[0]]));
        yield return ("partial exact set", fixture => fixture.Request(GroundworkFence("partial", 2), fixture.Members[..2]));
        yield return ("out of order members", fixture => fixture.Request(GroundworkFence("order", 2), fixture.Members.Reverse().ToArray()));
        yield return ("mixed workflow", fixture => fixture.Request(GroundworkFence("workflow", 2)) with { WorkflowExecutionId = "wf-other" });
        yield return ("mixed original authority", fixture => fixture.Request(GroundworkFence("authority", 2), [fixture.Members[0] with { RecoveryAuthority = GroundworkAuthority("work-other") }, .. fixture.Members[1..]]));
        yield return ("mixed current fence", fixture => fixture.Request(GroundworkFence("current", 2), [fixture.Members[0] with { ExpectedCurrentAuthorityFence = GroundworkFence("wrong-current", 1) }, .. fixture.Members[1..]]));
        yield return ("ledger-token mismatch", fixture => fixture.Request(GroundworkFence("token", 2), [fixture.Members[0] with { LedgerToken = "wrong-token" }, .. fixture.Members[1..]]));
        yield return ("canonical digest mismatch", fixture => fixture.Request(GroundworkFence("digest", 2), [fixture.Members[0] with { CanonicalInputReference = "wrong-digest-reference" }, .. fixture.Members[1..]]));
        yield return ("original order revision mismatch", fixture => fixture.Request(GroundworkFence("order-revision", 2), [fixture.Members[0] with { OriginalOrderRevision = fixture.Members[0].OriginalOrderRevision + 1 }, .. fixture.Members[1..]]));
        yield return ("original context revision mismatch", fixture => fixture.Request(GroundworkFence("context-revision", 2), [fixture.Members[0] with { OriginalContextRevision = fixture.Members[0].OriginalContextRevision + 1 }, .. fixture.Members[1..]]));
        yield return ("authority revision mismatch", fixture => fixture.Request(GroundworkFence("authority-revision", 2), [fixture.Members[0] with { ExpectedAuthorityRevision = fixture.Members[0].ExpectedAuthorityRevision + 1 }, .. fixture.Members[1..]]));
        yield return ("canonical fingerprint mismatch", fixture => fixture.Request(GroundworkFence("fingerprint", 2), [fixture.Members[0] with { CanonicalInputFingerprint = "sha256:wrong" }, .. fixture.Members[1..]]));
        yield return ("hidden prepared-set gap", fixture => fixture.Request(GroundworkFence("gap", 2), [fixture.Members[0], fixture.Members[2]]));
    }

    public static TheoryData<RuntimeCheckpointRecoveryRoute, string, Func<GroundworkAdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest>> GroundworkRejectedExactSetCases
    {
        get
        {
            var data = new TheoryData<RuntimeCheckpointRecoveryRoute, string, Func<GroundworkAdoptionFixture, RuntimeCheckpointPreparedAdoptionRequest>>();
            foreach (var route in new[] { RuntimeCheckpointRecoveryRoute.SourceBound, RuntimeCheckpointRecoveryRoute.SourceFree })
            foreach (var (name, build) in GroundworkRejectedExactSetRequests())
                data.Add(route, name, build);
            return data;
        }
    }

    private static RuntimeExecutionFence GroundworkFence(string owner, long token) =>
        new($"lease-{owner}", owner, token);

    private static RuntimeCheckpointRecoveryAuthority GroundworkAuthority(string workItemId) =>
        new(1, "runtime.scheduler-work", "wf-1", workItemId, $"sha256:{workItemId}");

    public sealed class GroundworkAdoptionFixture
    {
        private GroundworkAdoptionFixture(
            InMemoryDocumentStore documents,
            InterceptingDocumentStore interceptor,
            GroundworkRuntimeCheckpointWriter writer,
            RuntimeCheckpointRecoveryRoute route,
            RuntimeCheckpointPreparedAdoptionMember[] members)
        {
            Documents = documents;
            Interceptor = interceptor;
            Writer = writer;
            Route = route;
            Members = members;
        }

        public InMemoryDocumentStore Documents { get; }
        internal InterceptingDocumentStore Interceptor { get; }
        public GroundworkRuntimeCheckpointWriter Writer { get; }
        public RuntimeCheckpointRecoveryRoute Route { get; }
        public RuntimeCheckpointPreparedAdoptionMember[] Members { get; }

        public static async Task<GroundworkAdoptionFixture> CreateAsync(RuntimeCheckpointRecoveryRoute route, int entryCount)
        {
            var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
            var interceptor = new InterceptingDocumentStore(documents);
            var writer = CreateWriter(interceptor);
            var authority = route == RuntimeCheckpointRecoveryRoute.SourceBound
                ? EncodeRecoveryAuthority(BuildRecoveryAuthorityWorkItem())
                : null;

            // Seed every durability family before adoption: domain state, execution context, outbox, dispatch,
            // marker/receipt, ledger compaction, and checkpoint-order high-watermarks. Adoption may change only the
            // prepared ledger's current authority binding/revision; every other raw storage unit remains identical.
            var seedCommit = BuildCommit($"groundwork-adoption-seed-{route}", includeDispatch: true);
            var seedOutbox = PendingDispatchOutbox(seedCommit.CommitId, "wf-1");
            seedCommit = seedCommit with
            {
                StateChanges = seedCommit.StateChanges.WithPostCommitOutbox(
                    [Change(seedOutbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, seedOutbox)])
            };
            var seedRequest = new RuntimeCheckpointPrepareRequest(
                seedCommit,
                "groundwork-adoption-seed",
                $"groundwork-adoption-seed-{route}",
                new RuntimeExecutionContextSnapshot(1, new Dictionary<string, string> { ["adoption.seed"] = route.ToString() }));
            var seedPreparation = await writer.PrepareAsync(seedRequest);
            var seedToken = Assert.IsType<RuntimeCheckpointPreparationToken>(seedPreparation.Token);
            var enrichedSeed = seedCommit with { Checkpoint = seedCommit.Checkpoint with { Provenance = seedToken.Provenance } };
            Assert.Equal(
                RuntimeCheckpointCommitStoreStatus.Committed,
                (await writer.CommitPreparedAsync(seedToken, enrichedSeed, Decision)).Status);

            for (var index = 1; index <= entryCount; index++)
            {
                var request = RuntimeCheckpointPrepareRequest.From(BuildCommit($"groundwork-negative-{route}-{index}"));
                if (authority is not null)
                    request = WithRecoveryAuthority(request, authority);
                Assert.Equal(RuntimeCheckpointPreparationStatus.Prepared, (await writer.PrepareAsync(request)).Status);
            }

            var reservations = (await writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250)))
                .Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder).ToArray();
            return new(documents, interceptor, writer, route, reservations.Select(ToAdoptionMember).ToArray());
        }

        public RuntimeCheckpointPreparedAdoptionRequest Request(
            RuntimeExecutionFence target,
            IReadOnlyList<RuntimeCheckpointPreparedAdoptionMember>? members = null) =>
            new("wf-1", Route, Members[^1].WorkflowCheckpointOrder, target, members ?? Members);

        public async Task<RuntimeCheckpointPreparedAdoptionMember[]> ReadMembersAsync()
        {
            var reservations = (await Writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250)))
                .Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder);
            return reservations.Select(ToAdoptionMember).ToArray();
        }

        public string RawSnapshot()
            => SnapshotRawDocuments(normalizeAuthorityBinding: false);

        public string NormalizedRawSnapshot()
            => SnapshotRawDocuments(normalizeAuthorityBinding: true);

        private string SnapshotRawDocuments(bool normalizeAuthorityBinding)
        {
            var kinds = ElsaRuntimeStorageManifest.Create().StorageUnits
                .Select(unit => unit.Identity.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);
            return JsonSerializer.Serialize(kinds.Select(kind => new
            {
                Kind = kind,
                Documents = Documents.Snapshot(kind)
                    .Select(document => normalizeAuthorityBinding
                        ? NormalizeAuthorityBinding(document)
                        : JsonSerializer.Serialize(document))
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            }));
        }

        private static string NormalizeAuthorityBinding(object document)
        {
            var root = JsonNode.Parse(JsonSerializer.Serialize(document))!;
            Normalize(root);
            return root.ToJsonString();

            static void Normalize(JsonNode? node)
            {
                if (node is JsonObject obj)
                {
                    foreach (var name in obj.Select(property => property.Key).ToArray())
                    {
                        if (StringComparer.OrdinalIgnoreCase.Equals(name, "CurrentAuthorityFence"))
                            obj[name] = null;
                        else if (StringComparer.OrdinalIgnoreCase.Equals(name, "AuthorityRevision"))
                            obj[name] = 0;
                        else
                            Normalize(obj[name]);
                    }
                }
                else if (node is JsonArray array)
                {
                    foreach (var item in array)
                        Normalize(item);
                }
            }
        }

        private static RuntimeCheckpointPreparedAdoptionMember ToAdoptionMember(RuntimeCheckpointPreparedReservation reservation) =>
            new(
                reservation.CommitId,
                reservation.Token.LedgerToken,
                reservation.Provenance.WorkflowCheckpointOrder,
                reservation.InputFingerprint,
                reservation.Token.CanonicalInputReference,
                reservation.ExpectedFence,
                reservation.ExpectedOrderRevision,
                reservation.ExpectedContextRevision,
                reservation.RecoveryAuthority,
                reservation.CurrentAuthorityFence,
                reservation.AuthorityRevision);
    }

    private static async Task<(RuntimeCheckpointPreparedReservation[] Reservations, string ImmutablePreparedBytes)> SnapshotPreparedAsync(
        GroundworkRuntimeCheckpointWriter writer)
    {
        var page = await writer.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 250));
        return (
            page.Reservations.OrderBy(reservation => reservation.Provenance.WorkflowCheckpointOrder).ToArray(),
            JsonSerializer.Serialize(page.Reservations.Select(reservation => new
            {
                reservation.Status,
                reservation.Token.CommitId,
                reservation.Token.LedgerToken,
                reservation.Provenance,
                reservation.Token.CanonicalInputReference,
                reservation.Token.CanonicalInputFingerprint,
                reservation.ExpectedFence,
                reservation.ExpectedOrderRevision,
                reservation.ExpectedContextRevision,
                reservation.RecoveryAuthority,
                reservation.CanonicalEnvelope
            })));
    }

    private static async Task<RuntimeCheckpointPreparedAdoptionReceipt> InvokeAdoptionAsync(
        IRuntimeCheckpointPreparedLedgerStore store,
        RuntimeCheckpointPreparedAdoptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var operation = typeof(IRuntimeCheckpointPreparedLedgerStore).GetMethod(
            "AdoptPreparedAsync",
            [typeof(RuntimeCheckpointPreparedAdoptionRequest), typeof(CancellationToken)]);
        Assert.True(operation is not null,
            "T027 must add the single provider-atomic adoption CAS after this T026 fixture has persisted its rows.");
        var awaitable = operation!.Invoke(store, [request, cancellationToken]);
        var task = (Task)awaitable!.GetType().GetMethod("AsTask")!.Invoke(awaitable, [])!;
        await task;
        return (RuntimeCheckpointPreparedAdoptionReceipt)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    [Fact]
    public async Task Prepared_ledger_page_uses_the_declared_workflow_status_order_commit_route()
    {
        var manifest = ElsaRuntimeStorageManifest.Create();
        var ledgerUnit = Assert.Single(
            manifest.StorageUnits,
            unit => unit.Identity.Value == ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerDocumentKind);
        var route = Assert.Single(
            ledgerUnit.Queries,
            query => query.Identity == ElsaRuntimeStorageManifest.PagePreparedRuntimeCheckpointsQuery);
        var index = Assert.Single(
            ledgerUnit.Indexes,
            candidate => candidate.Identity == ElsaRuntimeStorageManifest.RuntimeCheckpointPreparedLedgerByWorkflowStatusOrderCommit);

        Assert.Equal(ElsaRuntimeStorageManifest.RuntimeCheckpointPreparedLedgerByWorkflowStatusOrderCommit, route.IndexIdentity);
        Assert.Equal(
            [
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerWorkflowExecutionIdField,
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerStatusField,
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerWorkflowCheckpointOrderField,
                ElsaRuntimeStorageManifest.RuntimeCheckpointLedgerCommitIdField
            ],
            index.Fields.Select(field => field.Path));

        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var writer = CreateWriter(store);
        var preparedLedger = Assert.IsAssignableFrom<IRuntimeCheckpointPreparedLedgerStore>(writer);
        await writer.PrepareAsync(RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-prepared-page-a")));
        await writer.PrepareAsync(RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-prepared-page-b")));

        var page = await preparedLedger.PagePreparedAsync(new RuntimeCheckpointPreparedQuery("wf-1", 1, null));
        Assert.Single(page.Reservations);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task Deferred_candidate_round_trips_through_token_envelope_and_shared_replayer()
    {
        var writer = CreateWriter(new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized()));
        var request = RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-deferred-candidate")) with
        {
            InitialPersistenceMode = RuntimeCheckpointPersistenceMode.Deferred
        };

        var preparation = await writer.PrepareAsync(request);
        var reservation = Assert.Single((await writer.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations);
        var decoded = new RuntimeCheckpointPreparationPayloadCodec().Decode(reservation.CanonicalEnvelope);
        var replayed = await new RuntimeCheckpointPreparationReplayer(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                [],
                [])
            .RehydrateAsync(reservation);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, preparation.Token!.InitialPersistenceMode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, reservation.CandidatePersistenceMode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, decoded.InitialPersistenceMode);
        Assert.Equal(reservation.InputFingerprint, reservation.CanonicalEnvelope.PayloadSha256);
        Assert.Equal(request.Commit.CommitId, replayed.Commit.CommitId);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, replayed.Decision.Mode);
    }

    [Fact]
    public async Task Prepared_authority_round_trips_through_groundwork_restart_page_and_decode_without_marker_or_state_side_effects()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var initialWriter = CreateWriter(store);
        var authority = EncodeRecoveryAuthority(BuildRecoveryAuthorityWorkItem());
        var request = WithRecoveryAuthority(
            RuntimeCheckpointPrepareRequest.From(BuildCommit("commit-authority-groundwork")),
            authority);

        var prepared = await initialWriter.PrepareAsync(request);
        var restartedWriter = CreateWriter(store);
        var reservation = Assert.Single((await restartedWriter.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations);
        var decoded = new RuntimeCheckpointPreparationPayloadCodec().Decode(reservation.CanonicalEnvelope);

        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(prepared.Token!));
        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(reservation));
        AssertRecoveryAuthorityEquivalent(authority, GetRecoveryAuthority(decoded));
        Assert.Equal(prepared.Token!.CanonicalInputFingerprint, reservation.InputFingerprint);
        Assert.Equal(reservation.InputFingerprint, reservation.CanonicalEnvelope.PayloadSha256);
        Assert.Null(await store.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, request.Commit.CommitId));
        Assert.Empty((await restartedWriter.PagePreparedAsync(
            new RuntimeCheckpointPreparedQuery(request.Commit.WorkflowExecutionId, 1))).Reservations
            .Where(candidate => candidate.CommitId != request.Commit.CommitId));
    }

    private static RuntimeSchedulerWorkItem BuildRecoveryAuthorityWorkItem()
    {
        using var document = JsonDocument.Parse("""{"source":"authority"}""");
        return new RuntimeSchedulerWorkItem(
            "work-authority-groundwork",
            "wf-1",
            "command-authority-groundwork",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            "envelope-authority-groundwork",
            "idempotency-authority-groundwork",
            DateTimeOffset.UnixEpoch.AddTicks(11),
            DateTimeOffset.UnixEpoch.AddTicks(17),
            7,
            document.RootElement.Clone(),
            new Dictionary<string, string> { ["command"] = "authority" },
            new Dictionary<string, string> { ["envelope"] = "authority" },
            "scope-authority-groundwork",
            new ActivityExecutionAttemptLineage(2, "attempt-first", "attempt-previous"));
    }

    private static object EncodeRecoveryAuthority(RuntimeSchedulerWorkItem workItem)
    {
        var authorityType = typeof(RuntimeSchedulerWorkItem).Assembly.GetType(
            "Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryAuthority");
        var codecType = typeof(RuntimeSchedulerWorkItem).Assembly.GetType(
            "Elsa.Workflows.Runtime.Core.Models.RuntimeCheckpointRecoveryAuthorityCodec");
        Assert.NotNull(authorityType);
        Assert.NotNull(codecType);
        var method = codecType!.GetMethods()
            .Single(candidate => candidate.ReturnType == authorityType &&
                                 candidate.GetParameters() is [{ ParameterType: var type }] &&
                                 type == typeof(RuntimeSchedulerWorkItem));
        return method.Invoke(method.IsStatic ? null : Activator.CreateInstance(codecType), [workItem])!;
    }

    private static RuntimeCheckpointPrepareRequest WithRecoveryAuthority(
        RuntimeCheckpointPrepareRequest request,
        object authority)
    {
        var property = typeof(RuntimeCheckpointPrepareRequest).GetProperty("RecoveryAuthority");
        Assert.NotNull(property);
        property!.SetValue(request, authority);
        return request;
    }

    private static object GetRecoveryAuthority(object value)
    {
        var property = value.GetType().GetProperty("RecoveryAuthority");
        Assert.NotNull(property);
        var authority = property!.GetValue(value);
        Assert.IsType(property.PropertyType, authority);
        return authority!;
    }

    private static void AssertRecoveryAuthorityEquivalent(object expected, object actual)
    {
        var authorityType = expected.GetType();
        foreach (var propertyName in new[] { "Fingerprint", "WorkflowExecutionId", "WorkItemId" })
            Assert.Equal(
                authorityType.GetProperty(propertyName)!.GetValue(expected),
                authorityType.GetProperty(propertyName)!.GetValue(actual));
    }
}
