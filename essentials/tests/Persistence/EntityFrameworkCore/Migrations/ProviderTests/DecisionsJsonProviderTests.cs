using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Decisions.Core;
using Elsa.Decisions.Persistence.Core;
using Elsa.Decisions.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;
using Elsa.Serialization.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>The actual Decisions store and module migrations must preserve carried JSON on each native engine.</summary>
public sealed class DecisionsJsonProviderTests
{
    private const string Tenant = "json-tenant";
    private const string Actor = "json-actor";
    private const string Raw = """{ "OrderID":9007199254740993, "amount":1.00, "nil":null, "text":"\u0061 <+>", "Nested": { "Upper":2 } }""";
    private const string RawActual = """[ null, 1.0, { "ActualID":9007199254740993, "text":"\u0041 <+>" } ]""";
    private static readonly Type ContextType = typeof(DecisionsEfModule).Assembly.GetType(
        "Elsa.Decisions.Persistence.EntityFrameworkCore.DecisionsDbContext", throwOnError: true)!;

    [SkippableFact]
    public Task PostgreSql_jsonb_rows_keep_their_meaning_and_new_case_and_receipt_data_remain_raw() =>
        ProviderDatabase.RunAsync("PostgreSql", connection => RunAsync("PostgreSql", connection,
            "20261105223015_Initial", "20261123021754_PreserveJsonText", "jsonb", "text"));

    [SkippableFact]
    public Task MySql_json_rows_keep_their_meaning_and_new_case_and_receipt_data_remain_raw() =>
        ProviderDatabase.RunAsync("MySql", connection => RunAsync("MySql", connection,
            "20261105223018_Initial", "20261123021759_PreserveJsonText", "json", "longtext"));

    [SkippableFact]
    public Task SqlServer_case_and_receipt_data_remain_raw_across_fresh_contexts_replay_and_tenants() =>
        ProviderDatabase.RunAsync("SqlServer", connection => RunAsync("SqlServer", connection));

    private static async Task RunAsync(string providerName, string connectionString, string? initialMigration = null,
        string? textMigration = null, string? oldColumnType = null, string? textColumnType = null)
    {
        // Anchor this proof in the same real context inventory used by Every_module_installs.
        Assert.Single(ModuleContextCatalog.Contexts(providerName),
            type => type.Assembly == typeof(DecisionsEfModule).Assembly);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        new DecisionsEntityFrameworkCoreFeature { Provider = providerName, ConnectionString = connectionString }
            .ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            Assert.Equal(EfRelationalProviderBinding.ExpectedProviderName(providerName), Database(scope.ServiceProvider).Database.ProviderName);

        if (initialMigration is not null)
            await UpgradeNativeJsonAsync(provider, providerName, initialMigration, textMigration!, oldColumnType!, textColumnType!);
        else
        {
            await using var scope = provider.CreateAsyncScope();
            await Database(scope.ServiceProvider).Database.MigrateAsync();
        }

        await RawStoreRoundTripAsync(provider);
    }

    private static async Task UpgradeNativeJsonAsync(ServiceProvider provider, string providerName,
        string initialMigration, string textMigration, string oldColumnType, string textColumnType)
    {
        const string legacyTenant = "legacy-tenant";
        var date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
        var legacy = new DecisionCase("legacy-case", "legacy-asset", "Original case", "legacy-revision",
            new Dictionary<string, DecisionValue> { ["Amount"] = new("number", "9007199254740993.0") },
            "Success", Snapshot(Raw), false, date, date);
        const string source = "<definitions>legacy native JSON storage</definitions>";
        var asset = new DecisionAsset(legacy.AssetId, "Original asset", null, "legacy-asset-revision",
            source, Hash(source), false, null, date, date);
        // The former store's Web profile wrote camelCase metadata. Native JSON columns may already have
        // normalized raw spelling; the migration owes that existing row's meaning, not unrecoverable bytes.
        var originalWriter = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string originalStored;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = Database(scope.ServiceProvider);
            var migrations = context.Database.GetMigrations().ToArray();
            Assert.Contains(initialMigration, migrations);
            Assert.Contains(textMigration, migrations);
            await context.GetService<IMigrator>().MigrateAsync(initialMigration);
            Assert.Equal(new[] { initialMigration }, await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(oldColumnType, await ColumnTypeAsync(context, providerName));
            await InsertLegacyRecordAsync(context, legacyTenant, "asset", asset.Id, asset.Id, asset.Revision, JsonSerializer.Serialize(asset, originalWriter));
            await InsertLegacyRecordAsync(context, legacyTenant, "case", legacy.Id, legacy.AssetId, legacy.Revision, JsonSerializer.Serialize(legacy, originalWriter));
            originalStored = await ReadRecordAsync(context, legacyTenant, "case", legacy.Id);
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = Database(scope.ServiceProvider);
            await context.Database.MigrateAsync(); // Runs the actual PreserveJsonText Up migration.
            Assert.Contains(textMigration, await context.Database.GetAppliedMigrationsAsync());
            Assert.Equal(textColumnType, await ColumnTypeAsync(context, providerName));
            using var before = JsonDocument.Parse(originalStored);
            using var after = JsonDocument.Parse(await ReadRecordAsync(context, legacyTenant, "case", legacy.Id));
            Assert.True(JsonCanonicalForm.Equals(before.RootElement, after.RootElement));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IDecisionReader>();
            Assert.Equal(asset, await reader.FindAssetAsync(legacyTenant, asset.Id));
            var restored = await reader.FindCaseAsync(legacyTenant, legacy.AssetId, legacy.Id);
            Assert.NotNull(restored);
            var originalReader = JsonSerializer.Deserialize<DecisionCase>(originalStored, originalWriter)!;
            Assert.Equal(originalReader.Id, restored!.Id);
            Assert.Equal(originalReader.AssetId, restored.AssetId);
            Assert.Equal(originalReader.Name, restored.Name);
            Assert.Equal(originalReader.Revision, restored.Revision);
            Assert.Equal(originalReader.ExpectedStatus, restored.ExpectedStatus);
            Assert.Equal(originalReader.IsSensitive, restored.IsSensitive);
            Assert.Equal(originalReader.CreatedAt, restored.CreatedAt);
            Assert.Equal(originalReader.UpdatedAt, restored.UpdatedAt);
            Assert.Equal(originalReader.CreatedAt.Offset, restored.CreatedAt.Offset);
            Assert.Equal(originalReader.UpdatedAt.Offset, restored.UpdatedAt.Offset);
            Assert.Equal(originalReader.Inputs["Amount"], restored.Inputs["Amount"]);
            Assert.True(JsonCanonicalForm.Equals(originalReader.ExpectedResult, restored.ExpectedResult));
        }
    }

    private static async Task RawStoreRoundTripAsync(ServiceProvider provider)
    {
        const string source = "<definitions>native JSON storage</definitions>";
        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var inputs = new Dictionary<string, DecisionValue>
        {
            ["Amount"] = new("number", "9007199254740993.0"),
            ["amount"] = new("string", "<+> 🧪")
        };
        var caseOperation = new DecisionOperation(Tenant, Actor, "save-raw-case");
        var runOperation = new DecisionOperation(Tenant, Actor, "save-raw-run");
        SaveDecisionCase command;
        DecisionWriteConfirmation savedCase;
        DecisionWriteConfirmation savedRun;
        DecisionTestReceipt receipt;
        await using (var scope = provider.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IDecisionWriter>();
            var asset = await writer.SaveAssetAsync(new(Tenant, Actor, "save-asset"),
                new(null, null, "Raw native data", null, source, sourceHash, false));
            command = new(asset.Id, null, null, "Raw case", inputs, "Success", Snapshot(Raw), false);
            savedCase = await writer.SaveCaseAsync(caseOperation, command);
            var date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
            receipt = new("raw-run", asset.Id, Actor, new string('b', 64), asset.Id, sourceHash,
                [new(savedCase.Id, savedCase.Revision, new string('a', 64))],
                [new(savedCase.Id, "Raw case", false, "Success", null, Snapshot(Raw), Snapshot(RawActual), [], [], [])],
                false, true, false, date, date) { CaseInputs = new Dictionary<string, IReadOnlyDictionary<string, DecisionValue>> { [savedCase.Id] = inputs } };
            savedRun = await writer.SaveCompletedRunAsync(runOperation, receipt);
            Assert.False(savedCase.Replayed);
            Assert.False(savedRun.Replayed);
        }

        string caseRow;
        string runRow;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = Database(scope.ServiceProvider);
            caseRow = await ReadRecordAsync(context, Tenant, "case", savedCase.Id);
            runRow = await ReadRecordAsync(context, Tenant, "run", savedRun.Id);
            using var storedCase = JsonDocument.Parse(caseRow);
            AssertRaw(storedCase.RootElement.GetProperty("expectedResult"));
            var storedInputs = storedCase.RootElement.GetProperty("inputs");
            Assert.Equal(2, storedInputs.EnumerateObject().Count());
            Assert.Equal("9007199254740993.0", storedInputs.GetProperty("Amount").GetProperty("value").GetString());
            Assert.Equal("<+> 🧪", storedInputs.GetProperty("amount").GetProperty("value").GetString());
            using var storedRun = JsonDocument.Parse(runRow);
            var comparison = storedRun.RootElement.GetProperty("comparisons")[0];
            AssertRaw(comparison.GetProperty("expected"));
            Assert.Equal(RawActual, comparison.GetProperty("actual").GetRawText());
        }

        // Each reader call opens a new context; a new DI scope rules out tracked-object reuse as evidence.
        await using (var scope = provider.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IDecisionReader>();
            var restoredCase = await reader.FindCaseAsync(Tenant, command.AssetId, savedCase.Id);
            Assert.NotNull(restoredCase);
            AssertRaw(restoredCase!.ExpectedResult);
            Assert.Equal(inputs["Amount"], restoredCase.Inputs["Amount"]);
            Assert.Equal(inputs["amount"], restoredCase.Inputs["amount"]);
            var restoredRun = await reader.FindRunAsync(Tenant, command.AssetId, savedRun.Id);
            Assert.NotNull(restoredRun);
            Assert.True(restoredRun!.HasDetailedPayload);
            AssertRaw(restoredRun.Comparisons[0].Expected!.Value);
            Assert.Equal(RawActual, restoredRun.Comparisons[0].Actual!.Value.GetRawText());
            Assert.Equal(inputs["Amount"], restoredRun.CaseInputs![savedCase.Id]["Amount"]);
            Assert.Null(await reader.FindCaseAsync("other-tenant", command.AssetId, savedCase.Id));
            Assert.Empty(await reader.ListCasesAsync("other-tenant", command.AssetId));
            Assert.Null(await reader.FindRunAsync("other-tenant", command.AssetId, savedRun.Id));
            Assert.Null(await reader.FindCompletedOperationAsync(runOperation with { TenantId = "other-tenant" }, command.AssetId));
            Assert.Null(await reader.FindOperationAsync(caseOperation with { ActorId = "other-actor" }));
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IDecisionWriter>();
            Assert.Equal(savedCase with { Replayed = true }, await writer.SaveCaseAsync(caseOperation, command));
            Assert.Equal(savedRun with { Replayed = true }, await writer.SaveCompletedRunAsync(runOperation, receipt));
            var failure = await Assert.ThrowsAsync<DecisionDesignException>(() => writer.SaveCaseAsync(caseOperation,
                command with { ExpectedResult = Snapshot("{\"changed\":true}") }).AsTask());
            Assert.Equal("conflict", failure.Code);
            var replayed = await scope.ServiceProvider.GetRequiredService<IDecisionReader>()
                .FindCompletedOperationAsync(runOperation, command.AssetId);
            Assert.NotNull(replayed);
            AssertRaw(replayed!.Comparisons[0].Expected!.Value);
            Assert.Equal(RawActual, replayed.Comparisons[0].Actual!.Value.GetRawText());
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var context = Database(scope.ServiceProvider);
            Assert.Equal(caseRow, await ReadRecordAsync(context, Tenant, "case", savedCase.Id));
            Assert.Equal(runRow, await ReadRecordAsync(context, Tenant, "run", savedRun.Id));
            Assert.Single(await scope.ServiceProvider.GetRequiredService<IDecisionReader>().ListCasesAsync(Tenant, command.AssetId));
            Assert.Single(await scope.ServiceProvider.GetRequiredService<IDecisionReader>().ListRunsAsync(Tenant, command.AssetId));
        }
    }

    private static DbContext Database(IServiceProvider services) => (DbContext)services.GetRequiredService(ContextType);

    private static JsonElement Snapshot(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return document.RootElement.Clone();
    }

    private static void AssertRaw(JsonElement value)
    {
        Assert.Equal(Raw, value.GetRawText());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("nil").ValueKind);
        Assert.False(value.TryGetProperty("missing", out _));
        Assert.Equal("9007199254740993", value.GetProperty("OrderID").GetRawText());
        Assert.Equal("1.00", value.GetProperty("amount").GetRawText());
    }

    private static async Task InsertLegacyRecordAsync(DbContext context, string tenant, string kind, string id,
        string ownerId, string revision, string json)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var columns = new[] { "TenantKey", "Kind", "Id", "OwnerId", "Revision", "Json", "SchemaVersion" };
        var jsonParameter = context.Database.ProviderName == EfProviderNames.PostgreSql ? "CAST(@p5 AS jsonb)" : "@p5";
        var commandText = $"INSERT INTO {sql.DelimitIdentifier("elsa_decisions")} ({string.Join(", ", columns.Select(sql.DelimitIdentifier))}) " +
                          $"VALUES (@p0, @p1, @p2, @p3, @p4, {jsonParameter}, @p6)";
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = commandText;
            var values = new[] { Hash(tenant), kind, id, ownerId, revision, json, "1.0.0" };
            for (var i = 0; i < values.Length; i++) AddParameter(command, "@p" + i, values[i]);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private static async Task<string> ReadRecordAsync(DbContext context, string tenant, string kind, string id)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT {sql.DelimitIdentifier("Json")} FROM {sql.DelimitIdentifier("elsa_decisions")} " +
                                  $"WHERE {sql.DelimitIdentifier("TenantKey")} = @tenant AND {sql.DelimitIdentifier("Kind")} = @kind AND {sql.DelimitIdentifier("Id")} = @id";
            AddParameter(command, "@tenant", Hash(tenant)); AddParameter(command, "@kind", kind); AddParameter(command, "@id", id);
            return Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private static async Task<string> ColumnTypeAsync(DbContext context, string providerName)
    {
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = providerName == "PostgreSql"
                ? "SELECT data_type FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'elsa_decisions' AND column_name = 'Json'"
                : "SELECT DATA_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'elsa_decisions' AND COLUMN_NAME = 'Json'";
            return Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        finally { await context.Database.CloseConnectionAsync(); }
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
