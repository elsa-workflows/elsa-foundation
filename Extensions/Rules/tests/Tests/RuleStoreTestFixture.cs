using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed class RuleStoreTestFixture : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-rules-store-{Guid.NewGuid():N}");

    internal string DatabasePath => Path.Combine(_directory, "rules.db");
    internal SqliteContextFactory Contexts { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        Contexts = new SqliteContextFactory(DatabasePath);
        await using var db = Contexts.CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    internal EfRuleRunStore CreateStore(IRuleAdmissionValidator validator) => new(Contexts, validator);

    // The validator and semantic pin are synthetic: these tests exercise store transactions, not package provenance.
    internal static RuleInvocationEnvelope NewInvocation()
    {
        var template = Invocation();
        var id = Guid.NewGuid().ToString("N");
        return Copy(template, identity: new RuleRunIdentity(new RuleScope(null, $"store-{id}"), $"invocation-{id}"));
    }

    internal sealed class FixedAdmissionValidator(RuleAdmissionValidationDisposition disposition,
        RuleRunDiagnostic? diagnostic = null) : IRuleAdmissionValidator
    {
        internal int Calls { get; private set; }

        public ValueTask<RuleAdmissionValidationResult> ValidateAsync(RuleInvocationEnvelope invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(new RuleAdmissionValidationResult(disposition, diagnostic));
        }
    }

    internal sealed class BarrierAdmissionValidator(RuleAdmissionValidationDisposition disposition,
        RuleRunDiagnostic? diagnostic = null, Exception? failure = null) : IRuleAdmissionValidator
    {
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }

        public async ValueTask<RuleAdmissionValidationResult> ValidateAsync(RuleInvocationEnvelope invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            if (failure is not null) throw failure;
            return new RuleAdmissionValidationResult(disposition, diagnostic);
        }
    }

    internal sealed class SqliteContextFactory(string databasePath, IInterceptor[]? interceptors = null) : IDbContextFactory<RulesDbContext>
    {
        private readonly DbContextOptions<RulesSqliteDbContext> _options = new DbContextOptionsBuilder<RulesSqliteDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False", sqlite =>
                sqlite.MigrationsHistoryTable(RulesEfModule.HistoryTableName))
            .AddInterceptors(interceptors ?? [])
            .Options;

        public RulesDbContext CreateDbContext() => new RulesSqliteDbContext(_options);

        public Task<RulesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
