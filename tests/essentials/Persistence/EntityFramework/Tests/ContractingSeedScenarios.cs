using System.Data.Common;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ContractingModule;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// The migrator seeds first (#2136, the owner's decision on #2093 of 2026-09-29), proved the same way on each engine a
/// test project runs it on. On a database no host has admitted the module in, the family's finalization record stands at
/// the version the contraction names before any operation of the contraction runs, so a release that reads only earlier
/// versions is refused however its start interleaves with the apply: after it, in the middle of it, or first.
/// </summary>
internal static class ContractingSeedScenarios
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The member a record names when the persistence tool, or a host that composes no fleet, creates it.</summary>
    public static string MachineMigrator => EfContractingMigrationCheck.MigratorHostIdPrefix + Environment.MachineName;

    /// <summary>
    /// Step by step: <c>dotnet elsa persistence apply</c> takes a fresh database through the contraction, and an older
    /// release starts next. The record is read as EF begins the contraction, so one created only after it fails here.
    /// </summary>
    public static async Task ApplyThenAnOlderReleaseStartsAsync(string provider, string connection)
    {
        SchemaFinalizationRecord? atContraction = null;
        ContractingProbe.WhenApplying<ContractingContractMigration>(() => atContraction = Wait(() => RecordAsync(provider, connection)));

        var run = await ToolAsync(provider, connection, "apply");

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
        AssertSeeded(atContraction, MachineMigrator);
        Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(provider, connection));
        Assert.False(await HasColumnAsync(provider, connection, RowsTable, "Legacy"));
        AssertRefused(await StartOlderReleaseAsync(provider, connection));
    }

    /// <summary>
    /// A process that ends after the seed and before the contraction, simulated by failing as soon as the family's record
    /// has been written, so nothing after it runs: the record stands without the contraction, an older release starting
    /// then is refused, and the next apply finds only its own seed's record, at the version, and completes.
    /// </summary>
    /// <param name="apply">One run of the migrator, as a fresh process would start it, with these interceptors on its context.</param>
    /// <param name="migrator">The member the seeded record names.</param>
    public static async Task ACrashBetweenTheSeedAndTheContractionAsync(string provider, string connection, Func<IInterceptor[], Task> apply, string migrator)
    {
        await Assert.ThrowsAsync<ProcessEndedException>(() => apply([new EndTheProcessOnceWritten(EfSchemaFinalization.RecordTableName(HistoryModule))]));

        Assert.Equal([Initial, Expand, DropObsolete], await AppliedAsync(provider, connection));
        Assert.True(await HasColumnAsync(provider, connection, RowsTable, "Legacy"));
        AssertSeeded(await RecordAsync(provider, connection), migrator);
        AssertRefused(await StartOlderReleaseAsync(provider, connection));

        await apply([]);

        Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(provider, connection));
        Assert.False(await HasColumnAsync(provider, connection, RowsTable, "Legacy"));
    }

    /// <summary>
    /// The two-releases race, the older one winning it: its gate admits the module, creating the family's record at the
    /// only version it reads, after the newer migrator found the module unadmitted and before its seed. The seed then
    /// finds that record, and the re-check refuses the contraction, which never runs; what ran before it was expand-only.
    /// </summary>
    public static async Task AnOlderReleaseThatCreatesTheRecordFirstKeepsTheContractionFromRunningAsync(string provider, string connection)
    {
        await using var newer = Create(provider, connection, new BeforeFirstFinalizationInsert(async () =>
        {
            await using var older = Create(provider, connection);
            await Gate(EarlierVersion).ActivateAsync(older);
        }));

        var refusal = await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(
            () => EfDatabaseMigrator.ApplyAsync(newer, EfRelationalProviderBinding.ExpectedProviderName(provider)));

        Assert.Equal(
            new EfContractingMigrationRefusal(Contract, Family, CurrentVersion, EarlierVersion, EfContractingMigrationRefusalReason.NotFinalized),
            Assert.Single(refusal.Refusals));
        Assert.Equal([Initial, Expand, DropObsolete], refusal.Applied);
        Assert.Equal([Contract], refusal.PendingMigrations);
        Assert.Contains($"so {Initial}, {Expand}, {DropObsolete} were applied first", refusal.Message, StringComparison.Ordinal);
        Assert.Equal([Initial, Expand, DropObsolete], await AppliedAsync(provider, connection));
        Assert.True(await HasColumnAsync(provider, connection, RowsTable, "Legacy"));
        var record = await RecordAsync(provider, connection);
        Assert.Equal(
            (EarlierVersion, Environment.MachineName),
            (record!.FinalizedVersion, record.History[0].Actor.Member?.HostId));
    }

    /// <summary>The two-releases race, the newer one winning it: the older release starts while the contraction is being applied, after the seed.</summary>
    public static async Task AnOlderReleaseThatStartsAfterTheSeedIsRefusedAndTheContractionRunsAsync(string provider, string connection)
    {
        EfSchemaActivationRefusedException? refused = null;
        ContractingProbe.WhenApplying<ContractingContractMigration>(() => refused = Wait(() => StartOlderReleaseAsync(provider, connection)));

        await using (var newer = Create(provider, connection))
            await EfDatabaseMigrator.ApplyAsync(newer, EfRelationalProviderBinding.ExpectedProviderName(provider));

        AssertRefused(refused);
        Assert.Contains(Contract, await AppliedAsync(provider, connection));
        AssertSeeded(await RecordAsync(provider, connection), MachineMigrator);
    }

    /// <summary>Runs one persistence-tool command against the module, as <c>dotnet elsa persistence</c> does.</summary>
    public static async Task<(int ExitCode, JsonElement Response)> ToolAsync(string provider, string connection, string command)
    {
        var request = new { version = 1, command, provider, selection = new { kind = "modules", modules = new[] { Name } }, connection };
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(request, Json));
        using var output = new MemoryStream();
        var exitCode = await EfToolingHost.RunAsync(input, output, [typeof(ContractingDbContext).Assembly]);
        using var response = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
        return (exitCode, response.RootElement.Clone());
    }

    /// <summary>The record the migrator created before the contraction: at the version it names, by <paramref name="migrator"/>.</summary>
    public static void AssertSeeded(SchemaFinalizationRecord? record, string migrator)
    {
        Assert.NotNull(record);
        Assert.Equal(CurrentVersion, record.FinalizedVersion);
        var created = record.History[0];
        Assert.Equal((SchemaFinalizationTransition.Created, CurrentVersion, migrator), (created.Transition, created.Version, created.Actor.Member?.HostId));
    }

    /// <summary>A release that reads only <see cref="EarlierVersion"/>, starting on the database: its gate's refusal, or null when it was admitted.</summary>
    private static async Task<EfSchemaActivationRefusedException?> StartOlderReleaseAsync(string provider, string connection)
    {
        await using var context = Create(provider, connection);
        try
        {
            await Gate(EarlierVersion).ActivateAsync(context);
            return null;
        }
        catch (EfSchemaActivationRefusedException refusal)
        {
            return refusal;
        }
    }

    /// <summary>Refused because the record stands at the contraction's version, not because it is missing.</summary>
    private static void AssertRefused(EfSchemaActivationRefusedException? refusal)
    {
        Assert.NotNull(refusal);
        Assert.Equal((EfSchemaActivationRefusal.FinalizedUnreadable, Family, CurrentVersion), (refusal.Refusal, refusal.Family, refusal.Version));
    }

    /// <summary>Runs <paramref name="read"/> to completion from EF's synchronous logging callback, off any synchronization context.</summary>
    public static T Wait<T>(Func<Task<T>> read) => Task.Run(read).GetAwaiter().GetResult();

    /// <summary>
    /// Whether <paramref name="command"/> creates a row of a finalization table (<paramref name="table"/> when given, else the
    /// record or identity table of any module). The store creates them with an insert-unless-present statement sent outside
    /// <c>SaveChanges</c> (#2162), so a test that pins a moment in a seed pins the command, not the save.
    /// </summary>
    internal static bool CreatesFinalizationRow(DbCommand command, string? table = null) =>
        command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
        (table is null
            ? command.CommandText.Contains(EfSchemaFinalization.RecordTablePrefix, StringComparison.Ordinal) ||
              command.CommandText.Contains(EfSchemaFinalization.DatabaseIdentityTablePrefix, StringComparison.Ordinal)
            : command.CommandText.Contains(table, StringComparison.Ordinal));

    /// <summary>Runs an action once, before the first finalization row the context it is added to creates.</summary>
    internal sealed class BeforeFirstFinalizationInsert(Func<Task> action) : DbCommandInterceptor
    {
        private Func<Task>? _action = action;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (CreatesFinalizationRow(command) && Interlocked.Exchange(ref _action, null) is { } run)
                await run();
            return result;
        }
    }

    /// <summary>Ends the process, as far as the migrator can tell, the moment the command that creates a row of <paramref name="table"/> has run.</summary>
    internal sealed class EndTheProcessOnceWritten(string table) : DbCommandInterceptor
    {
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            int result,
            CancellationToken cancellationToken = default) =>
            CreatesFinalizationRow(command, table) ? throw new ProcessEndedException() : ValueTask.FromResult(result);
    }

    internal sealed class ProcessEndedException() : Exception("The process ended here.");
}
