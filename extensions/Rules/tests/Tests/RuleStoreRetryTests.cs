using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Rules.Tests;

public sealed class RuleStoreRetryTests(RuleStoreTestFixture fixture) : IClassFixture<RuleStoreTestFixture>
{
    [Fact]
    public async Task Validation_on_last_retry_slot_still_rechecks_a_concurrent_cancel_winner()
    {
        var invocation = RuleStoreTestFixture.NewInvocation();
        var validator = new RuleStoreTestFixture.BarrierAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
        var contexts = new BusyThenContextFactory(fixture.Contexts, busyFailures: 7);
        var admissionTask = new EfRuleRunStore(contexts, validator).AdmitAsync(new(invocation)).AsTask();

        try
        {
            await validator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var cancelValidator = new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Unavailable);
            var cancellation = await fixture.CreateStore(cancelValidator).CancelAsync(new(invocation));
            validator.Release.TrySetResult(true);
            var admission = await admissionTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(RuleCancellationDisposition.Cancelled, cancellation.Disposition);
            Assert.Equal(RuleAdmissionDisposition.AlreadyCancelled, admission.Disposition);
            Assert.Equal(0, cancelValidator.Calls);
            Assert.Equal(1, validator.Calls);
            Assert.Equal(9, contexts.AsyncCalls);
            Assert.Equal(cancellation.TerminalId, admission.TerminalId);
        }
        finally
        {
            validator.Release.TrySetResult(true);
        }
    }

    private sealed class BusyThenContextFactory(IDbContextFactory<RulesDbContext> inner, int busyFailures)
        : IDbContextFactory<RulesDbContext>
    {
        private int _asyncCalls;

        internal int AsyncCalls => Volatile.Read(ref _asyncCalls);

        public RulesDbContext CreateDbContext() => inner.CreateDbContext();

        public Task<RulesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var call = Interlocked.Increment(ref _asyncCalls);
            if (call <= busyFailures)
                return Task.FromException<RulesDbContext>(new SqliteException("Injected SQLite busy", 5));
            return inner.CreateDbContextAsync(cancellationToken);
        }
    }
}
