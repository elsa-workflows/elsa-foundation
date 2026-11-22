using System.Data.Common;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed class RuleStoreAtomicityTests(RuleStoreTestFixture fixture) : IClassFixture<RuleStoreTestFixture>
{
    [Fact]
    public async Task Migration_is_applied_and_model_has_no_unscaffolded_changes()
    {
        await using var db = fixture.Contexts.CreateDbContext();
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Admission_failure_after_save_rolls_back_run_and_initial_fact_history()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var faulting = new EfRuleRunStore(new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath,
            [new FailAfterSave()]), new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready));
        await Assert.ThrowsAsync<InjectedFailure>(() => faulting.AdmitAsync(new(invocation)).AsTask());

        await using var db = fixture.Contexts.CreateDbContext();
        var key = RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(invocation.Identity));
        Assert.False(await db.Runs.AnyAsync(row => row.RunKey == key));
        Assert.False(await db.Facts.AnyAsync(row => row.RunKey == key));
        Assert.Equal(RuleAdmissionDisposition.Admitted,
            (await fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready)).AdmitAsync(new(invocation))).Disposition);
    }

    [Fact]
    public async Task Cancellation_failure_after_save_rolls_back_terminal_and_native_delivery()
    {
        var invocation = Copy(RuleStoreTestFixture.NewInvocation(), destination: Destination());
        var store = fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready));
        Assert.Equal(RuleAdmissionDisposition.Admitted, (await store.AdmitAsync(new(invocation))).Disposition);
        var faulting = new EfRuleRunStore(new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath,
            [new FailAfterSave()]), new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready));
        await Assert.ThrowsAsync<InjectedFailure>(() => faulting.CancelAsync(new(invocation)).AsTask());

        await using (var db = fixture.Contexts.CreateDbContext())
        {
            var key = RuleRecordCodec.Hash(RuleRecordCodec.ScopeBytes(invocation.Identity));
            var row = await db.Runs.SingleAsync(row => row.RunKey == key);
            Assert.Equal((int)RuleRunStatus.Running, row.Status);
            Assert.Equal(1, row.ModelRevision);
            Assert.Null(row.TerminalJson);
            Assert.Null(row.DeliveryPayloadJson);
            Assert.Single(await db.Facts.Where(fact => fact.RunKey == key).ToArrayAsync());
        }
        Assert.Equal(RuleCancellationDisposition.Cancelled, (await store.CancelAsync(new(invocation))).Disposition);
        Assert.Equal(RuleTerminalReadDisposition.Found, (await store.ReadTerminalAsync(invocation.Identity)).Disposition);
    }

    [Fact]
    public async Task Provider_rejects_oversized_terminal_projection_even_if_validator_incorrectly_admits_it()
    {
        // A deliberately permissive validator proves the provider's independent actual-byte boundary.
        // These are valid common-envelope facts, not a claimed valid finite program or package qualification.
        var facts = Enumerable.Range(0, RuleLimits.MaxVocabularyFacts)
            .Select(index => new RuleFact(new string('\u0001', 100) + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var invocation = Copy(RuleStoreTestFixture.NewInvocation(), facts: facts, destination: Destination());
        var store = fixture.CreateStore(new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready));
        Assert.Equal(RuleAdmissionDisposition.Admitted, (await store.AdmitAsync(new(invocation))).Disposition);

        var result = await store.CancelAsync(new(invocation));
        Assert.Equal(RuleCancellationDisposition.Unavailable, result.Disposition);
        var read = await store.FindAsync(invocation.Identity);
        Assert.Equal(RuleRunStatus.Running, read.View?.Status);
        Assert.Equal(1, read.View?.ModelRevision);
        Assert.Equal(RuleTerminalReadDisposition.NotTerminal, (await store.ReadTerminalAsync(invocation.Identity)).Disposition);
    }

    [Fact]
    public async Task Concurrent_cancellations_that_read_the_same_revision_have_one_winner()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var validator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready);
        await fixture.CreateStore(validator).AdmitAsync(new(invocation));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var barrier = new FirstTwoRunReads();
        var store = new EfRuleRunStore(new RuleStoreTestFixture.SqliteContextFactory(fixture.DatabasePath, [barrier]), validator);
        var outcomes = await Task.WhenAll(
            store.CancelAsync(new(invocation), deadline.Token).AsTask(),
            store.CancelAsync(new(invocation), deadline.Token).AsTask());

        Assert.Single(outcomes, result => result.Disposition == RuleCancellationDisposition.Cancelled);
        Assert.Single(outcomes, result => result.Disposition == RuleCancellationDisposition.Replay);
        Assert.All(outcomes, result => Assert.Equal(2, result.ModelRevision));
        Assert.Equal(outcomes[0].TerminalId, outcomes[1].TerminalId);
    }

    private sealed class FirstTwoRunReads : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("elsa_rules_runtime_runs", StringComparison.Ordinal))
            {
                var read = Interlocked.Increment(ref _reads);
                if (read == 2) _bothRead.TrySetResult();
                if (read <= 2) await _bothRead.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class InjectedFailure : Exception;

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InjectedFailure();
    }
}
