using Elsa.Persistence.EntityFramework;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Persistence.EntityFrameworkCore.Entities;
using Elsa.Rules.Runtime;
using Elsa.Tasks.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static Elsa.Rules.Tests.RuleTestData;

namespace Elsa.Rules.Tests;

public sealed partial class RuleRuntimeCompositionTests
{
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    public async Task Runtime_features_migrate_admit_execute_and_project_direct_runs(
        bool registerIdentityBeforeFeature, bool pooling, int maxConcurrentAttempts)
    {
        await using var database = new RuntimeDatabase();
        // The fixture pin is synthetic. This proves feature composition and worker use, not package provenance.
        var identitySource = new TestIdentitySource(Invocation().ExpectedImplementation);
        await using var host = await StartHostAsync(database.DatabasePath, identitySource, registerIdentityBeforeFeature,
            pooling: pooling, maxConcurrentAttempts: maxConcurrentAttempts);

        var worker = Assert.Single(host.Services.GetServices<IBackgroundTask>().OfType<RulesRuntimeWorker>());
        Assert.Same(worker, Assert.Single(host.Services.GetServices<IHostedService>().OfType<RulesRuntimeWorker>()));
        var workerTask = Assert.IsAssignableFrom<Task>(worker.ExecuteTask);
        using (var activation = new CancellationTokenSource())
        {
            await ((IBackgroundTask)worker).StartAsync(activation.Token);
            await activation.CancelAsync();
            Assert.False(workerTask.IsCompleted);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IRuleRunStore>();
        var reader = services.GetRequiredService<IRuleRunReader>();
        Assert.Same(store, reader);
        Assert.Same(services.GetRequiredService<EfRuleRunStore>(), store);
        var factory = services.GetRequiredService<IRuleInvocationFactory>();

        var program = Witness();
        var defaultIdentity = NewIdentity("default-budget");
        var explicitIdentity = NewIdentity("explicit-budget");
        var exhaustedIdentity = NewIdentity("exhausted-budget");
        var defaultPreparation = await factory.PrepareAsync(defaultIdentity, program, Facts("seed"));
        var explicitPreparation = await factory.PrepareAsync(explicitIdentity, program, Facts("seed"), maxRounds: 4);
        var exhaustedPreparation = await factory.PrepareAsync(exhaustedIdentity, program, Facts("seed"), maxRounds: 3);

        Assert.Equal(RuleInvocationPreparationDisposition.Prepared, defaultPreparation.Disposition);
        Assert.Equal(RuleInvocationPreparationDisposition.Prepared, explicitPreparation.Disposition);
        Assert.Equal(RuleInvocationPreparationDisposition.Prepared, exhaustedPreparation.Disposition);
        Assert.Equal(7, defaultPreparation.Invocation!.MaxRounds);
        Assert.Equal(4, explicitPreparation.Invocation!.MaxRounds);
        Assert.Equal(3, exhaustedPreparation.Invocation!.MaxRounds);
        foreach (var prepared in new[] { defaultPreparation, explicitPreparation, exhaustedPreparation })
            Assert.Equal(RuleAdmissionDisposition.Admitted,
                (await store.AdmitAsync(new(prepared.Invocation!))).Disposition);

        var defaultRun = await WaitForTerminalAsync(reader, defaultIdentity);
        var explicitRun = await WaitForTerminalAsync(reader, explicitIdentity);
        var exhaustedRun = await WaitForTerminalAsync(reader, exhaustedIdentity);
        Assert.Equal(RuleRunStatus.Succeeded, defaultRun.Status);
        Assert.Equal(RuleRunStatus.Succeeded, explicitRun.Status);
        Assert.Equal(4, defaultRun.CommittedRounds);
        Assert.Equal(4, explicitRun.CommittedRounds);
        Assert.Equal(RuleRunStatus.Faulted, exhaustedRun.Status);
        Assert.Equal(3, exhaustedRun.CommittedRounds);

        foreach (var identity in new[] { defaultIdentity, explicitIdentity })
        {
            var terminal = await reader.ReadTerminalAsync(identity);
            Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
            Assert.Equal(RuleTerminalKind.Succeeded, terminal.Result!.Kind);
            Assert.Equal(new[] { "final", "joined", "left", "right", "seed" },
                terminal.Result.ResultFacts.Select(fact => fact.Atom).Order(StringComparer.Ordinal));
            var progress = await reader.ReadProgressAsync(identity, null, RuleLimits.MaxPageEntries);
            Assert.Equal(RuleProgressReadDisposition.Page, progress.Disposition);
            var rounds = progress.Page!.Items.Where(item => item.Kind == RuleProgressItemKind.Round)
                .Select(item => item.Round!).ToArray();
            Assert.Equal(4, rounds.Length);
            Assert.All(rounds, round => Assert.NotNull(round.ConsumedModelRevision));
            Assert.All(rounds, round => Assert.False(string.IsNullOrWhiteSpace(round.AcceptedAttemptId)));
        }

        var exhaustedTerminal = await reader.ReadTerminalAsync(exhaustedIdentity);
        Assert.Equal(RuleTerminalReadDisposition.Found, exhaustedTerminal.Disposition);
        Assert.Equal(RuleTerminalKind.Faulted, exhaustedTerminal.Result!.Kind);
        Assert.Equal("rules.round-budget-exhausted", exhaustedTerminal.Result.Diagnostic?.Code);

        var db = services.GetRequiredService<RulesDbContext>();
        var rows = await db.Runs.AsNoTracking().ToArrayAsync();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Null(row.DeliveryId64);
            Assert.Null(row.DeliveryPayloadJson);
            Assert.False(row.DeliveryAcknowledged);
        });
        await host.StopAsync();
        Assert.True(workerTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Invalid_preparations_and_unavailable_implementations_do_not_admit_but_existing_runs_replay()
    {
        await using var database = new RuntimeDatabase();
        var pin = Invocation().ExpectedImplementation;
        var identitySource = new TestIdentitySource(pin);
        RuleInvocationEnvelope retained;

        await using (var host = await StartHostAsync(database.DatabasePath, identitySource))
        {
            await using var scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var factory = services.GetRequiredService<IRuleInvocationFactory>();
            var store = services.GetRequiredService<IRuleRunStore>();
            var reader = services.GetRequiredService<IRuleRunReader>();
            var program = Witness();

            var invalidBudgetId = NewIdentity("invalid-budget");
            var invalidBudget = await factory.PrepareAsync(invalidBudgetId, program, Facts("seed"), maxRounds: 0);
            Assert.Equal(RuleInvocationPreparationDisposition.Invalid, invalidBudget.Disposition);
            Assert.Equal(RuleRunReadDisposition.NotFound, (await reader.FindAsync(invalidBudgetId)).Disposition);

            var invalidFactsId = NewIdentity("invalid-facts");
            var invalidFacts = await factory.PrepareAsync(invalidFactsId, program, Facts("outside-vocabulary"));
            Assert.Equal(RuleInvocationPreparationDisposition.Invalid, invalidFacts.Disposition);
            Assert.Equal(RuleRunReadDisposition.NotFound, (await reader.FindAsync(invalidFactsId)).Disposition);

            var mismatched = Copy(Invocation(), identity: NewIdentity("mismatched-pin"), pin: DifferentPin(pin));
            Assert.Equal(RuleAdmissionDisposition.Unavailable,
                (await store.AdmitAsync(new(mismatched))).Disposition);
            Assert.Equal(RuleRunReadDisposition.NotFound, (await reader.FindAsync(mismatched.Identity)).Disposition);

            var malformedArtifactBytes = "{}"u8.ToArray();
            var malformedArtifactHash = RuleRecordCodec.Hash(malformedArtifactBytes);
            var semanticallyInvalid = Copy(Invocation(), identity: NewIdentity("invalid-artifact"), artifact:
                new RuleArtifactSnapshot(1, "rules:" + malformedArtifactHash, malformedArtifactHash, malformedArtifactBytes));
            Assert.Equal(RuleAdmissionDisposition.Refused,
                (await store.AdmitAsync(new(semanticallyInvalid))).Disposition);
            var refusal = await reader.ReadTerminalAsync(semanticallyInvalid.Identity);
            Assert.Equal(RuleTerminalReadDisposition.Found, refusal.Disposition);
            Assert.Equal(RuleTerminalKind.Refused, refusal.Result!.Kind);

            var prepared = await factory.PrepareAsync(NewIdentity("retained-run"), program, Facts("seed"));
            Assert.Equal(RuleInvocationPreparationDisposition.Prepared, prepared.Disposition);
            retained = prepared.Invocation!;
            Assert.Equal(RuleAdmissionDisposition.Admitted, (await store.AdmitAsync(new(retained))).Disposition);
            _ = await WaitForTerminalAsync(reader, retained.Identity);
        }

        var missingIdentitySource = new TestIdentitySource(null);
        await using (var host = await StartHostAsync(database.DatabasePath, missingIdentitySource))
        {
            await host.StopAsync();
            await using var scope = host.Services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IRuleInvocationFactory>();
            var store = scope.ServiceProvider.GetRequiredService<IRuleRunStore>();
            var reader = scope.ServiceProvider.GetRequiredService<IRuleRunReader>();
            var unpreparedIdentity = NewIdentity("missing-pin-preparation");
            var unavailablePreparation = await factory.PrepareAsync(unpreparedIdentity, Witness(), Facts("seed"));
            Assert.Equal(RuleInvocationPreparationDisposition.Unavailable, unavailablePreparation.Disposition);
            Assert.Equal(RuleRunReadDisposition.NotFound, (await reader.FindAsync(unpreparedIdentity)).Disposition);
            var readsBeforeReplay = missingIdentitySource.ReadCount;
            Assert.Equal(RuleAdmissionDisposition.Replay, (await store.AdmitAsync(new(retained))).Disposition);
            Assert.Equal(readsBeforeReplay, missingIdentitySource.ReadCount);

            var newIdentity = RuleTestData.Copy(retained, identity: NewIdentity("missing-pin"));
            Assert.Equal(RuleAdmissionDisposition.Unavailable,
                (await store.AdmitAsync(new(newIdentity))).Disposition);
            Assert.Equal(RuleRunReadDisposition.NotFound, (await reader.FindAsync(newIdentity.Identity)).Disposition);
        }

        await using (var host = await StartHostAsync(database.DatabasePath, new TestIdentitySource(pin), acceptNewInvocations: false))
        {
            await host.StopAsync();
            await using var scope = host.Services.CreateAsyncScope();
            Assert.Equal(RuleAdmissionDisposition.Replay,
                (await scope.ServiceProvider.GetRequiredService<IRuleRunStore>().AdmitAsync(new(retained))).Disposition);
            var newInvocation = RuleTestData.Copy(retained, identity: NewIdentity("disabled-new-admission"));
            Assert.Equal(RuleAdmissionDisposition.Unavailable,
                (await scope.ServiceProvider.GetRequiredService<IRuleRunStore>().AdmitAsync(new(newInvocation))).Disposition);
            Assert.Equal(RuleRunReadDisposition.NotFound,
                (await scope.ServiceProvider.GetRequiredService<IRuleRunReader>().FindAsync(newInvocation.Identity)).Disposition);
        }
    }

    [Fact]
    public async Task Worker_consumes_a_retained_receipt_after_its_interpreter_is_unavailable_without_evaluating_again()
    {
        await using var fixture = new RuleStoreTestFixture();
        await fixture.InitializeAsync();
        var invocation = RuleStoreTestFixture.NewInvocation();
        var seedingStore = new EfRuleRunStore(fixture.Contexts,
            new RuleStoreTestFixture.FixedAdmissionValidator(RuleAdmissionValidationDisposition.Ready),
            roundValidator: new RuleRoundEvaluator());
        Assert.Equal(RuleAdmissionDisposition.Admitted, (await seedingStore.AdmitAsync(new(invocation))).Disposition);
        var claim = await seedingStore.ClaimWorkAsync(new("before-restart", [invocation.ExpectedImplementation], [1], [1], [1]));
        Assert.Equal(RuleWorkClaimDisposition.Claimed, claim.Disposition);
        var token = claim.Token!;
        var work = await seedingStore.ReadWorkAsync(token);
        Assert.Equal(RuleWorkReadDisposition.EvaluateRound, work.Disposition);
        var receipt = new RuleRoundEvaluator().Evaluate(work.RoundInput!);
        var accepted = await seedingStore.AcceptRoundAsync(token,
            new(receipt, new("accepted-before-restart", "before-restart", token.Lease.Fence)));
        Assert.Equal(RuleReceiptDisposition.Accepted, accepted.Disposition);
        Assert.Equal(RuleLeaseDisposition.Released, (await seedingStore.ReleaseAsync(token.Lease)).Disposition);

        await using var host = await StartHostAsync(fixture.DatabasePath, new TestIdentitySource(null));
        await using var scope = host.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRuleRunStore>();
        var reader = scope.ServiceProvider.GetRequiredService<IRuleRunReader>();
        Assert.Same(store, reader);

        var progress = await WaitForConsumedRoundAsync(reader, invocation.Identity);
        var savedRound = Assert.Single(progress.Page!.Items, item => item.Kind == RuleProgressItemKind.Round);
        Assert.Equal("accepted-before-restart", savedRound.Round!.AcceptedAttemptId);
        Assert.NotNull(savedRound.Round.ConsumedModelRevision);
        var run = (await reader.FindAsync(invocation.Identity)).View!;
        Assert.Equal(RuleRunStatus.Running, run.Status);
        Assert.Equal(1, run.CommittedRounds);
        Assert.Null(run.PendingReceiptId);
        Assert.Equal(RuleTerminalReadDisposition.NotTerminal,
            (await reader.ReadTerminalAsync(invocation.Identity)).Disposition);
        var noInterpreterWork = await store.ClaimWorkAsync(new("no-interpreter-check", [], [1], [1], [1]));
        Assert.Equal(RuleWorkClaimDisposition.NoEligibleWork, noInterpreterWork.Disposition);
        Assert.Null(noInterpreterWork.Token);

        var db = scope.ServiceProvider.GetRequiredService<RulesDbContext>();
        var row = await db.Runs.AsNoTracking().SingleAsync();
        Assert.Null(row.DeliveryId64);
        Assert.False(row.DeliveryAcknowledged);
    }

    private static async Task<RuleRunView> WaitForTerminalAsync(IRuleRunReader reader, RuleRunIdentity identity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var read = await reader.FindAsync(identity, timeout.Token);
            Assert.Equal(RuleRunReadDisposition.Found, read.Disposition);
            if (read.View!.Status != RuleRunStatus.Running)
                return read.View;
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<RuleProgressReadResult> WaitForConsumedRoundAsync(IRuleRunReader reader, RuleRunIdentity identity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var read = await reader.ReadProgressAsync(identity, null, RuleLimits.MaxPageEntries, timeout.Token);
            Assert.Equal(RuleProgressReadDisposition.Page, read.Disposition);
            if (read.Page!.Items.Any(item => item.Kind == RuleProgressItemKind.Round && item.Round?.ConsumedModelRevision is not null))
                return read;
            await Task.Delay(10, timeout.Token);
        }
    }

    private static RuleRunIdentity NewIdentity(string suffix)
    {
        var id = Guid.NewGuid().ToString("N");
        return new(new(null, $"runtime-composition-{id}"), $"{suffix}-{id}");
    }

    private static RuleSemanticPin DifferentPin(RuleSemanticPin source)
    {
        var packages = source.Packages.Select((package, index) => index == 0
            ? new RulePackagePin(package.PackageId, package.NuGetVersion + ".other", package.NuGetContentHash, package.SelectedAssets)
            : package).ToArray();
        return new(source.PinSchemaVersion, packages, source.LanguageId, source.LanguageVersion,
            source.ReceiptSchemaVersion, source.StateSchemaVersion);
    }

    private static IHost BuildHost(string databasePath, IRuleImplementationIdentitySource identitySource,
        bool registerIdentityBeforeFeature = true, bool acceptNewInvocations = true, bool pooling = false,
        int maxConcurrentAttempts = 1)
    {
        var builder = Host.CreateApplicationBuilder();
        if (registerIdentityBeforeFeature)
            builder.Services.AddSingleton(identitySource);

        new RulesRuntimeFeature
        {
            AcceptNewInvocations = acceptNewInvocations,
            MaxConcurrentAttempts = maxConcurrentAttempts,
            PollIntervalMilliseconds = 10
        }.ConfigureServices(builder.Services);
        new RulesRuntimeEntityFrameworkCoreFeature
        {
            ConnectionString = $"Data Source={databasePath};Pooling=False",
            Pooling = pooling
        }.ConfigureServices(builder.Services);
        if (!registerIdentityBeforeFeature)
            builder.Services.AddSingleton(identitySource);
        builder.Services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);

        return builder.Build();
    }

    private static async Task<RuntimeHost> StartHostAsync(string databasePath, IRuleImplementationIdentitySource identitySource,
        bool registerIdentityBeforeFeature = true, bool acceptNewInvocations = true, bool pooling = false,
        int maxConcurrentAttempts = 1)
    {
        var host = BuildHost(databasePath, identitySource, registerIdentityBeforeFeature,
            acceptNewInvocations, pooling, maxConcurrentAttempts);
        try
        {
            await host.StartAsync();
            return new RuntimeHost(host);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private sealed class TestIdentitySource(RuleSemanticPin? pin) : IRuleImplementationIdentitySource
    {
        private int _readCount;
        internal int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult(pin);
        }
    }

    private sealed class RuntimeHost(IHost host) : IAsyncDisposable
    {
        internal IServiceProvider Services => host.Services;

        internal Task StopAsync(CancellationToken cancellationToken = default) => host.StopAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    private sealed class RuntimeDatabase : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-rules-runtime-{Guid.NewGuid():N}");
        internal string DatabasePath => Path.Combine(_directory, "rules.db");

        public RuntimeDatabase() => Directory.CreateDirectory(_directory);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
