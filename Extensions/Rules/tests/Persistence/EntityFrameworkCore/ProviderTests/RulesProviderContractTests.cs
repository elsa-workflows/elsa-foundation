using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Elsa.Rules.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit.Abstractions;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Persistence.EntityFrameworkCore.ProviderTests;

[Collection("rules-provider-sqlite")]
public sealed class RulesSqliteProviderTests(RulesSqliteProviderFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Feature_registration_migrations_and_contracts(bool pooling) =>
        RulesProviderContractTests.RunAsync(fixture, "Sqlite", pooling, output);
}

[Collection("rules-provider-postgresql")]
public sealed class RulesPostgreSqlProviderTests(RulesPostgreSqlProviderFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Feature_registration_migrations_and_contracts(bool pooling) =>
        RulesProviderContractTests.RunAsync(fixture, "PostgreSql", pooling, output);
}

[Collection("rules-provider-sqlserver")]
public sealed class RulesSqlServerProviderTests(RulesSqlServerProviderFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Feature_registration_migrations_and_contracts(bool pooling) =>
        RulesProviderContractTests.RunAsync(fixture, "SqlServer", pooling, output);
}

[Collection("rules-provider-mysql")]
public sealed class RulesMySqlProviderTests(RulesMySqlProviderFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Feature_registration_migrations_and_contracts(bool pooling) =>
        RulesProviderContractTests.RunAsync(fixture, "MySql", pooling, output);
}

internal static partial class RulesProviderContractTests
{
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(30);

    internal static async Task RunAsync(RulesNativeProviderFixture fixture, string provider, bool pooling,
        ITestOutputHelper output)
    {
        fixture.RequireAvailable();
        await using var database = await fixture.CreateDatabaseAsync();
        await using var host = CreateFeatureHost(provider, database.ConnectionString, pooling);
        await using var scope = host.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var contexts = services.GetRequiredService<IDbContextFactory<RulesDbContext>>();
        var store = services.GetRequiredService<IRuleRunStore>();
        var reader = services.GetRequiredService<IRuleRunReader>();
        Assert.Same(store, reader);

        await ApplyAndValidateMigrationsAsync(contexts, provider, output);
        await VerifyDatabaseClockAsync(contexts);

        var successful = await DriveDirectRunAsync(store, reader, contexts, MakeInvocation("direct-success", maxRounds: 4),
            expectedKind: RuleTerminalKind.Succeeded, recoverAcceptedReceiptWithoutInterpreter: true,
            checkWorkLeaseAndRevision: true);
        var exhausted = await DriveDirectRunAsync(store, reader, contexts, MakeInvocation("round-budget", maxRounds: 3),
            expectedKind: RuleTerminalKind.Faulted, recoverAcceptedReceiptWithoutInterpreter: false,
            checkWorkLeaseAndRevision: false);
        Assert.Equal("rules.round-budget-exhausted", exhausted.Diagnostic?.Code);
        await AssertNoDeliveryObligationAsync(contexts, successful, exhausted);

        await VerifyAdmissionRaceAsync(contexts);
        await VerifyConsumptionRaceAndSnapshotAsync(store, reader, contexts);
        await VerifyPendingCancellationAsync(store, reader);
        await VerifyExpiryRollbackAndReclaimAsync(contexts);
        await VerifyTerminalDeliveryAtomicityAsync(contexts);
        await VerifyDeliveryExpiryRollbackAsync(contexts);
        await VerifyExactScopeKeysAsync(store, reader, contexts);
        await VerifySupplementaryUnicodeFactStorageAsync(store, contexts);
        await VerifyIndexedAddressCollisionAsync(store, reader, contexts);
        await VerifyCancellationReplayAsync(store, reader,
            MakeInvocation("direct-cancel", maxRounds: 4));
        await VerifyNativeDeliveryAsync(store, reader, contexts,
            MakeInvocation("native-cancel", maxRounds: 4, native: true));
    }

    private static ServiceProvider CreateFeatureHost(string provider, string connectionString, bool pooling)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRuleAdmissionValidator>(ReadyAdmissionValidator.Instance);
        services.AddSingleton<IRuleRoundValidator>(new RuleRoundEvaluator());
        new RulesRuntimeEntityFrameworkCoreFeature
        {
            Provider = provider,
            ConnectionString = connectionString,
            Pooling = pooling
        }.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static async Task ApplyAndValidateMigrationsAsync(IDbContextFactory<RulesDbContext> contexts,
        string provider, ITestOutputHelper output)
    {
        await using var db = await contexts.CreateDbContextAsync();
        var expectedProvider = EfRelationalProviderBinding.ExpectedProviderName(provider);
        Assert.Equal(expectedProvider, db.Database.ProviderName);

        await EfDatabaseMigrator.ApplyAsync(db, expectedProvider, EfMigratePolicy.AutoMigrate);
        await EfDatabaseMigrator.ApplyAsync(db, expectedProvider, EfMigratePolicy.Validate);
        var migrations = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.NotEmpty(migrations);
        Assert.Equal(migrations, applied);

        await db.Database.OpenConnectionAsync();
        output.WriteLine($"Rules {provider} database engine version: {db.Database.GetDbConnection().ServerVersion}");
        await db.Database.CloseConnectionAsync();
    }

    private static async Task VerifyDatabaseClockAsync(IDbContextFactory<RulesDbContext> contexts)
    {
        await using var db = await contexts.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var before = await db.ReadUtcTicksAsync(CancellationToken.None);
        AssertNearProcessUtc(before);

        await Task.Delay(TimeSpan.FromMilliseconds(1_100));

        var after = await db.ReadUtcTicksAsync(CancellationToken.None);
        AssertNearProcessUtc(after);
        Assert.True(after > before, "The provider clock must advance inside a transaction.");
        await transaction.CommitAsync();
    }

    private static void AssertNearProcessUtc(long databaseTicks)
    {
        var processTicks = DateTimeOffset.UtcNow.UtcTicks;
        Assert.InRange(databaseTicks, processTicks - ClockTolerance.Ticks, processTicks + ClockTolerance.Ticks);
    }

    private static async Task<RuleTerminalResult> DriveDirectRunAsync(IRuleRunStore store, IRuleRunReader reader,
        IDbContextFactory<RulesDbContext> contexts, RuleInvocationEnvelope invocation, RuleTerminalKind expectedKind,
        bool recoverAcceptedReceiptWithoutInterpreter, bool checkWorkLeaseAndRevision)
    {
        var admission = await store.AdmitAsync(new(invocation));
        Assert.Equal(RuleAdmissionDisposition.Admitted, admission.Disposition);
        Assert.Equal(1, admission.ModelRevision);
        var replay = await store.AdmitAsync(new(invocation));
        Assert.Equal(RuleAdmissionDisposition.Replay, replay.Disposition);
        Assert.Equal(admission.TerminalId, replay.TerminalId);
        Assert.Equal(admission.ModelRevision, replay.ModelRevision);

        var claim = await store.ClaimWorkAsync(ClaimRequest(invocation, "direct-worker"));
        Assert.Equal(RuleWorkClaimDisposition.Claimed, claim.Disposition);
        var token = Assert.IsType<RuleWorkToken>(claim.Token);

        if (checkWorkLeaseAndRevision)
        {
            var beforeRenewal = Assert.IsType<RuleRunView>((await reader.FindAsync(invocation.Identity)).View);
            var renewed = await store.RenewAsync(token.Lease);
            Assert.Equal(RuleLeaseDisposition.Renewed, renewed.Disposition);
            var renewedLease = Assert.IsType<RuleWorkLease>(renewed.Lease);
            Assert.True(renewedLease.ExpiresAt >= token.Lease.ExpiresAt);
            token = new(renewedLease, token.ExpectedModelRevision);

            var afterRenewal = Assert.IsType<RuleRunView>((await reader.FindAsync(invocation.Identity)).View);
            Assert.Equal(beforeRenewal.ModelRevision, afterRenewal.ModelRevision);
            Assert.Equal(1, afterRenewal.ModelRevision);

            var staleFence = new RuleWorkLease(invocation.Identity, token.Lease.Fingerprint,
                token.Lease.Owner, checked(token.Lease.Fence + 1), token.Lease.ExpiresAt);
            Assert.Equal(RuleLeaseDisposition.LeaseLost, (await store.RenewAsync(staleFence)).Disposition);

            var wrongScope = new RuleRunIdentity(new RuleScope("other-tenant", invocation.Identity.Scope.PersistencePartition),
                invocation.Identity.InvocationId);
            var wrongLease = new RuleWorkLease(wrongScope, token.Lease.Fingerprint,
                token.Lease.Owner, token.Lease.Fence, token.Lease.ExpiresAt);
            Assert.Equal(RuleWorkReadDisposition.NotFound,
                (await reader.ReadWorkAsync(new(wrongLease, token.ExpectedModelRevision))).Disposition);
        }

        var evaluator = new RuleRoundEvaluator();
        var recoveryStore = new EfRuleRunStore(contexts, ReadyAdmissionValidator.Instance,
            roundValidator: null);
        RuleTransitionConfirmation? lastTransition = null;
        for (var ordinal = 0; ordinal < invocation.MaxRounds; ordinal++)
        {
            var work = await reader.ReadWorkAsync(token);
            Assert.Equal(RuleWorkReadDisposition.EvaluateRound, work.Disposition);
            token = Assert.IsType<RuleWorkToken>(work.CurrentToken);
            var input = Assert.IsType<RuleRoundInput>(work.RoundInput);
            Assert.Equal(ordinal, input.Identity.Ordinal);
            var receipt = evaluator.Evaluate(input);
            var accepted = await store.AcceptRoundAsync(token,
                new(receipt, new($"attempt-{ordinal}", token.Lease.Owner, token.Lease.Fence)));
            Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
            Assert.Equal(token.ExpectedModelRevision + 1, accepted.ModelRevision);

            var pending = await (recoverAcceptedReceiptWithoutInterpreter && ordinal == 0
                ? recoveryStore.ReadWorkAsync(token)
                : reader.ReadWorkAsync(token));
            Assert.Equal(RuleWorkReadDisposition.ConsumeReceipt, pending.Disposition);
            Assert.NotNull(pending.PendingReceipt);
            Assert.Equal(accepted.ReceiptId, pending.PendingReceipt.ReceiptId);
            var pendingToken = Assert.IsType<RuleWorkToken>(pending.CurrentToken);
            Assert.Equal(accepted.ModelRevision, pendingToken.ExpectedModelRevision);
            var consumer = recoverAcceptedReceiptWithoutInterpreter && ordinal == 0 ? recoveryStore : store;
            var consumed = await consumer.ConsumeRoundAsync(pendingToken,
                new(pending.PendingReceipt.ReceiptId, pending.PendingReceipt.Receipt.SemanticReceiptSha256));
            Assert.Equal(RuleTransitionDisposition.Consumed, consumed.Disposition);
            Assert.Equal(accepted.ModelRevision + 1, consumed.ModelRevision);
            lastTransition = consumed;
            token = pendingToken;
            if (consumed.TerminalId is not null)
                break;
        }

        Assert.NotNull(lastTransition);
        var terminalRead = await reader.ReadTerminalAsync(invocation.Identity);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminalRead.Disposition);
        var terminal = Assert.IsType<RuleTerminalResult>(terminalRead.Result);
        Assert.Equal(expectedKind, terminal.Kind);
        Assert.Equal(invocation.MaxRounds, terminal.CommittedRounds);
        Assert.Equal(lastTransition.TerminalId, terminal.TerminalId);
        Assert.Null(lastTransition.NextRoundId);
        Assert.Equal(new[] { "final", "joined", "left", "right", "seed" },
            terminal.ResultFacts.Select(fact => fact.Atom).Order(StringComparer.Ordinal));

        var progress = await reader.ReadProgressAsync(invocation.Identity, null, RuleLimits.MaxPageEntries);
        Assert.Equal(RuleProgressReadDisposition.Page, progress.Disposition);
        var rounds = Assert.IsType<RuleProgressPage>(progress.Page).Items
            .Where(item => item.Kind == RuleProgressItemKind.Round).Select(item => item.Round!).ToArray();
        Assert.Equal(invocation.MaxRounds, rounds.Length);
        Assert.All(rounds, round => Assert.NotNull(round.ConsumedModelRevision));
        Assert.Equal(expectedKind == RuleTerminalKind.Succeeded ? RuleRunStatus.Succeeded : RuleRunStatus.Faulted,
            (await reader.FindAsync(invocation.Identity)).View?.Status);
        return terminal;
    }

    private static async Task AssertNoDeliveryObligationAsync(IDbContextFactory<RulesDbContext> contexts,
        RuleTerminalResult success, RuleTerminalResult exhausted)
    {
        Assert.Equal(RuleTerminalKind.Succeeded, success.Kind);
        Assert.Equal(RuleTerminalKind.Faulted, exhausted.Kind);
        var terminalIds = new[] { success.TerminalId, exhausted.TerminalId };
        Assert.All(terminalIds, terminalId => Assert.False(string.IsNullOrWhiteSpace(terminalId)));
        await using var db = await contexts.CreateDbContextAsync();
        var rows = await db.Runs.AsNoTracking()
            .Where(row => terminalIds.Contains(row.TerminalId64))
            .ToArrayAsync();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Null(row.DeliveryId64);
            Assert.Null(row.DeliveryPayloadJson);
            Assert.Null(row.DeliveryPayloadSha25664);
            Assert.Null(row.DeliveryOwner);
            Assert.Equal(0, row.DeliveryFence);
            Assert.Null(row.DeliveryExpiresAtUtcTicks);
            Assert.False(row.DeliveryAcknowledged);
            Assert.Null(row.DeliveryAcknowledgementJson);
        });
    }

    private static async Task VerifyExactScopeKeysAsync(IRuleRunStore store, IRuleRunReader reader,
        IDbContextFactory<RulesDbContext> contexts)
    {
        const string invocationId = "scope-identity";
        var maximumIdentifier = new string('a', RuleLimits.MaxIdentifierUtf8Bytes - 2) + "é";
        var oversizedIdentifier = new string('a', RuleLimits.MaxIdentifierUtf8Bytes - 1) + "é";
        RuleRunIdentity[] identities =
        [
            new(new(null, "Partition"), invocationId),
            new(new("tenant", "Partition"), invocationId),
            new(new(null, "partition"), invocationId),
            new(new(null, "é"), invocationId),
            new(new(null, "e\u0301"), invocationId),
            new(new(null, maximumIdentifier), invocationId),
            new(new(maximumIdentifier, "Partition"), invocationId),
            new(new(null, "Partition"), maximumIdentifier),
            new(new("Tenant", "Partition"), invocationId),
            new(new("é", "Partition"), invocationId),
            new(new("e\u0301", "Partition"), invocationId)
        ];
        var keys = identities.Select(Key).ToArray();
        Assert.Equal(identities.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.Matches("^[0-9a-f]{64}$", key));

        foreach (var identity in identities)
        {
            var admitted = await store.AdmitAsync(new(MakeInvocation(identity, maxRounds: 4)));
            Assert.Equal(RuleAdmissionDisposition.Admitted, admitted.Disposition);
            var found = await reader.FindAsync(identity);
            Assert.Equal(RuleRunReadDisposition.Found, found.Disposition);
            Assert.Equal(identity, found.View?.Identity);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleScope(null, oversizedIdentifier));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleScope(oversizedIdentifier, "Partition"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleRunIdentity(new(null, "Partition"), oversizedIdentifier));
        await using var db = await contexts.CreateDbContextAsync();
        var storedKeys = await db.Runs.AsNoTracking().Where(row => keys.Contains(row.RunKey))
            .Select(row => row.RunKey).ToArrayAsync();
        Assert.Equal(keys.Order(StringComparer.Ordinal), storedKeys.Order(StringComparer.Ordinal));
    }

    private static async Task VerifyIndexedAddressCollisionAsync(IRuleRunStore store, IRuleRunReader reader,
        IDbContextFactory<RulesDbContext> contexts)
    {
        var retained = MakeInvocation("collision-source", maxRounds: 4);
        var collision = MakeInvocation("collision-target", maxRounds: 4);
        Assert.Equal(RuleCancellationDisposition.Cancelled,
            (await store.CancelAsync(new(retained))).Disposition);
        var retainedKey = Key(retained.Identity);
        var collisionKey = Key(collision.Identity);
        string retainedRowBefore;
        await using (var db = await contexts.CreateDbContextAsync())
        {
            // Inject an impossible hash collision in test data, outside production mutation paths.
            var affected = await db.Runs.Where(row => row.RunKey == retainedKey)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RunKey, collisionKey));
            Assert.Equal(1, affected);
            var movedRow = await db.Runs.AsNoTracking().SingleAsync(row => row.RunKey == collisionKey);
            retainedRowBefore = JsonSerializer.Serialize(movedRow);
        }

        var result = await store.CancelAsync(new(collision));
        Assert.Equal(RuleCancellationDisposition.Conflict, result.Disposition);
        Assert.Equal(RuleRunReadDisposition.NotFound, (await reader.FindAsync(collision.Identity)).Disposition);
        Assert.Equal(RuleRunReadDisposition.NotFound, (await reader.FindAsync(retained.Identity)).Disposition);
        await using var verify = await contexts.CreateDbContextAsync();
        var retainedRowAfter = await verify.Runs.AsNoTracking().SingleAsync(row => row.RunKey == collisionKey);
        Assert.Equal(retainedRowBefore, JsonSerializer.Serialize(retainedRowAfter));
    }

    private static async Task VerifySupplementaryUnicodeFactStorageAsync(IRuleRunStore store,
        IDbContextFactory<RulesDbContext> contexts)
    {
        const string atom = "𐐷";
        var facts = Facts(atom);
        var semanticInput = Input(Program([atom], []), facts);
        var invocation = Copy(MakeInvocation("supplementary-fact"), artifact: semanticInput.Artifact,
            facts: facts, pin: semanticInput.Implementation);
        Assert.Equal(RuleAdmissionDisposition.Admitted, (await store.AdmitAsync(new(invocation))).Disposition);
        await using var db = await contexts.CreateDbContextAsync();
        var fact = await db.Facts.AsNoTracking().SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));
        Assert.Equal(atom, fact.Atom);
    }

    private static async Task VerifyCancellationReplayAsync(IRuleRunStore store, IRuleRunReader reader,
        RuleInvocationEnvelope invocation)
    {
        var cancelled = await store.CancelAsync(new(invocation));
        var replay = await store.CancelAsync(new(invocation));
        var afterCancellation = await store.AdmitAsync(new(invocation));

        Assert.Equal(RuleCancellationDisposition.Cancelled, cancelled.Disposition);
        Assert.Equal(RuleCancellationDisposition.Replay, replay.Disposition);
        Assert.Equal(cancelled.TerminalId, replay.TerminalId);
        Assert.Equal(cancelled.ModelRevision, replay.ModelRevision);
        Assert.Equal(RuleAdmissionDisposition.AlreadyCancelled, afterCancellation.Disposition);
        Assert.Equal(RuleTerminalKind.Cancelled,
            (await reader.ReadTerminalAsync(invocation.Identity)).Result?.Kind);
    }

    private static async Task VerifyNativeDeliveryAsync(IRuleRunStore store, IRuleRunReader reader,
        IDbContextFactory<RulesDbContext> contexts, RuleInvocationEnvelope invocation)
    {
        var cancelled = await store.CancelAsync(new(invocation));
        Assert.Equal(RuleCancellationDisposition.Cancelled, cancelled.Disposition);
        Assert.Equal(RuleCancellationDisposition.Replay,
            (await store.CancelAsync(new(invocation))).Disposition);

        var unsupported = await store.ClaimDeliveryAsync(new("native-delivery-worker", [2]));
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery, unsupported.Disposition);
        var claim = await store.ClaimDeliveryAsync(new("native-delivery-worker", [1]));
        Assert.Equal(RuleDeliveryClaimDisposition.Claimed, claim.Disposition);
        var lease = Assert.IsType<RuleDeliveryLease>(claim.Lease);
        var read = await reader.ReadDeliveryAsync(lease);
        Assert.Equal(RuleDeliveryReadDisposition.Ready, read.Disposition);
        var envelope = Assert.IsType<RuleTerminalEnvelope>(read.Envelope);
        Assert.Equal(invocation.NativeDestination, envelope.Destination);
        Assert.Equal(lease.DeliveryId, envelope.DeliveryId);
        Assert.Equal(RuleTerminalKind.Cancelled, envelope.Result.Kind);
        Assert.Equal(RuleRecordCodec.Hash(RuleRecordCodec.DeliveryBytes(envelope.DeliveryId,
            envelope.Destination, envelope.Result)), envelope.CanonicalPayloadSha256);

        var wrongIdentity = MakeInvocation("wrong-delivery-scope").Identity;
        var wrongScopeLease = new RuleDeliveryLease(wrongIdentity, lease.DeliveryId,
            lease.Owner, lease.Fence, lease.ExpiresAt);
        Assert.Equal(RuleDeliveryReadDisposition.NotFound,
            (await reader.ReadDeliveryAsync(wrongScopeLease)).Disposition);

        var staleLease = new RuleDeliveryLease(lease.Identity, lease.DeliveryId,
            lease.Owner, checked(lease.Fence + 1), lease.ExpiresAt);
        Assert.Equal(RuleDeliveryReadDisposition.LeaseLost, (await reader.ReadDeliveryAsync(staleLease)).Disposition);
        var proof = DeliveryProof(envelope);
        Assert.Equal(RuleTransitionDisposition.LeaseLost,
            (await store.AcknowledgeDeliveryAsync(wrongScopeLease, new(proof))).Disposition);
        Assert.Equal(RuleTransitionDisposition.LeaseLost,
            (await store.AcknowledgeDeliveryAsync(staleLease, new(proof))).Disposition);

        // The feature deliberately registers no proof validator here; a durable delivery stays retryable by default.
        Assert.Equal(RuleTransitionDisposition.ProofUnavailable,
            (await store.AcknowledgeDeliveryAsync(lease, new(proof))).Disposition);
        var validator = new ExactDeliveryProofValidator();
        var verifiedStore = new EfRuleRunStore(contexts, ReadyAdmissionValidator.Instance,
            deliveryProofValidator: validator);
        var differentDestination = new RuleDeliveryProof(proof.Kind, DifferentDigest(proof.DestinationIdentity),
            proof.TerminalId, proof.PayloadSha256, proof.ParentCommitOrTerminalId);
        Assert.Equal(RuleTransitionDisposition.Invalid,
            (await verifiedStore.AcknowledgeDeliveryAsync(lease, new(differentDestination))).Disposition);
        Assert.Equal(0, validator.Calls);

        var acknowledged = await verifiedStore.AcknowledgeDeliveryAsync(lease, new(proof));
        var replay = await verifiedStore.AcknowledgeDeliveryAsync(lease, new(proof));
        var changedProof = new RuleDeliveryProof(proof.Kind, proof.DestinationIdentity,
            proof.TerminalId, proof.PayloadSha256, "different-parent-commit");
        var changed = await verifiedStore.AcknowledgeDeliveryAsync(lease, new(changedProof));
        Assert.Equal(RuleTransitionDisposition.Acknowledged, acknowledged.Disposition);
        Assert.Equal(lease.DeliveryId, acknowledged.TransitionId);
        Assert.Equal(RuleTransitionDisposition.Replay, replay.Disposition);
        Assert.Equal(acknowledged.TransitionId, replay.TransitionId);
        Assert.Equal(RuleTransitionDisposition.Conflict, changed.Disposition);
        Assert.Equal(1, validator.Calls);

        await using var db = await contexts.CreateDbContextAsync();
        var row = await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.RunKey == Key(invocation.Identity));
        Assert.True(row.DeliveryAcknowledged);
        Assert.NotNull(row.DeliveryAcknowledgementJson);
    }

    private static RuleClaimRequest ClaimRequest(RuleInvocationEnvelope invocation, string owner) =>
        new(owner, [invocation.ExpectedImplementation], [invocation.InvocationSchemaVersion],
            [invocation.ExpectedImplementation.ReceiptSchemaVersion], [invocation.ExpectedImplementation.StateSchemaVersion]);

    private static RuleInvocationEnvelope MakeInvocation(string suffix, int maxRounds = 4, bool native = false)
    {
        var template = Invocation();
        var identity = new RuleRunIdentity(new RuleScope(null, $"provider-{Guid.NewGuid():N}"), $"invocation-{suffix}");
        return Copy(template, identity: identity, maxRounds: maxRounds, destination: native ? Destination() : null);
    }

    private static RuleInvocationEnvelope MakeInvocation(RuleRunIdentity identity, int maxRounds = 4)
    {
        var template = Invocation();
        return Copy(template, identity: identity, maxRounds: maxRounds);
    }

    private static string Key(RuleRunIdentity identity) => RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(identity));

    private static string DifferentDigest(string digest) => (digest[0] == '0' ? '1' : '0') + digest[1..];

    private sealed class ReadyAdmissionValidator : IRuleAdmissionValidator
    {
        internal static readonly ReadyAdmissionValidator Instance = new();

        public ValueTask<RuleAdmissionValidationResult> ValidateAsync(RuleInvocationEnvelope invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RuleAdmissionValidationResult(RuleAdmissionValidationDisposition.Ready, null));
        }
    }

    private sealed class ExactDeliveryProofValidator : IRuleDeliveryProofValidator
    {
        private int _calls;
        internal int Calls => Volatile.Read(ref _calls);

        public ValueTask<RuleDeliveryProofValidationResult> ValidateAsync(RuleTerminalEnvelope envelope,
            RuleDeliveryProof proof, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);
            var equivalent = proof.DestinationIdentity == RuleRecordCodec.DestinationIdentity(envelope.Destination)
                && proof.TerminalId == envelope.Result.TerminalId
                && proof.PayloadSha256 == envelope.CanonicalPayloadSha256
                && !string.IsNullOrWhiteSpace(proof.ParentCommitOrTerminalId);
            return ValueTask.FromResult(new RuleDeliveryProofValidationResult(equivalent
                ? RuleDeliveryProofValidationDisposition.Equivalent
                : RuleDeliveryProofValidationDisposition.Invalid));
        }
    }
}
