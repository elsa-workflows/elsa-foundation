using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 171 User Story 5, end to end against a real SQLite database: a Secrets database with legacy
/// projection rows reports <see cref="SecretsProjectionReindex"/> as required after its migrations apply,
/// <c>post-migrate</c> reindexes it, a subsequent audit reports nothing required, and a database with no
/// legacy rows reports nothing required at all.
/// </summary>
/// <remarks>
/// Driven through <see cref="EfToolingHost"/>'s frozen JSON contract rather than the types underneath it,
/// because the failure this seam exists to prevent is a command <i>exiting 0</i> while a repair is still
/// outstanding, and only the response says what an operator was told. The negative direction is asserted
/// as hard as the positive one: after an <c>apply</c> that refuses, the legacy row is read back and must
/// still be legacy — an audit that quietly repaired would otherwise look exactly like a healthy run.
/// </remarks>
public sealed class SecretsProjectionReindexTests : IAsyncDisposable
{
    /// <summary>A projection written under the algorithm the module has since moved off.</summary>
    private const string LegacyKey = "ƛ";

    private const string CurrentKey = "Ƛ";

    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TemporarySqliteDatabase database = new("secrets-post-migrate");

    public ValueTask DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task A_legacy_row_is_reported_required_after_apply_reindexed_by_post_migrate_and_then_clean()
    {
        var applied = await RunAsync(EfToolingCommands.Apply);
        Assert.Equal(EfToolingExitCode.Success, applied.ExitCode);
        await SeedAsync(LegacyKey);

        // 1. apply reports the action as required instead of exiting 0 as if nothing were pending.
        var refused = await RunAsync(EfToolingCommands.Apply);
        Assert.Equal(EfToolingExitCode.NegativeResult, refused.ExitCode);
        Assert.Equal("post-migration-required", Code(refused));
        var detail = Assert.Single(Details(refused));
        Assert.Contains(nameof(SecretsProjectionReindex), detail, StringComparison.Ordinal);
        Assert.Contains("dotnet elsa persistence post-migrate --modules Secrets --provider Sqlite", detail, StringComparison.Ordinal);

        // …and nothing ran as a side effect of it: the row is still legacy.
        Assert.Equal(LegacyKey, await TypeNameLookupKeyAsync());

        // validate agrees, because a host under Migrate:Policy=Validate would refuse to start.
        var validated = await RunAsync(EfToolingCommands.Validate);
        Assert.Equal(EfToolingExitCode.NegativeResult, validated.ExitCode);
        Assert.Equal("post-migration-required", Code(validated));
        Assert.Equal(LegacyKey, await TypeNameLookupKeyAsync());

        // 2. post-migrate reindexes it.
        var repaired = await RunAsync(EfToolingCommands.PostMigrate);
        Assert.Equal(EfToolingExitCode.Success, repaired.ExitCode);
        var module = Assert.Single(repaired.Body.GetProperty("postMigrate").GetProperty("modules").EnumerateArray());
        Assert.Equal("Secrets", module.GetProperty("module").GetString());
        Assert.Equal([nameof(SecretsProjectionReindex)], Strings(module, "declared"));
        Assert.Equal([nameof(SecretsProjectionReindex)], Strings(module, "ran"));
        Assert.Equal(CurrentKey, await TypeNameLookupKeyAsync());

        // 3. a subsequent audit reports nothing required, from either command.
        Assert.Equal(EfToolingExitCode.Success, (await RunAsync(EfToolingCommands.Apply)).ExitCode);
        Assert.Equal(EfToolingExitCode.Success, (await RunAsync(EfToolingCommands.Validate)).ExitCode);
    }

    [Fact]
    public async Task A_database_with_no_legacy_rows_reports_nothing_required_and_post_migrate_runs_nothing()
    {
        Assert.Equal(EfToolingExitCode.Success, (await RunAsync(EfToolingCommands.Apply)).ExitCode);
        await SeedAsync(CurrentKey);

        Assert.Equal(EfToolingExitCode.Success, (await RunAsync(EfToolingCommands.Apply)).ExitCode);
        Assert.Equal(EfToolingExitCode.Success, (await RunAsync(EfToolingCommands.Validate)).ExitCode);

        var run = await RunAsync(EfToolingCommands.PostMigrate);

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
        var module = Assert.Single(run.Body.GetProperty("postMigrate").GetProperty("modules").EnumerateArray());
        Assert.Equal([nameof(SecretsProjectionReindex)], Strings(module, "declared"));
        Assert.Empty(Strings(module, "ran"));
    }

    /// <summary>
    /// A database whose schema is behind is refused by <c>post-migrate</c> rather than audited: an action
    /// audited against a schema the module has moved past answers a question about a database that no longer
    /// exists, and a clean exit would tell the operator the opposite.
    /// </summary>
    [Fact]
    public async Task Post_migrate_refuses_a_module_whose_migrations_are_pending()
    {
        var run = await RunAsync(EfToolingCommands.PostMigrate);

        Assert.Equal(EfToolingExitCode.NegativeResult, run.ExitCode);
        Assert.Equal("pending-migrations", Code(run));
    }

    /// <summary>
    /// <see cref="IEfPostMigrationAction.AuditAsync"/> must never mutate. Driven directly, because every
    /// command above calls it and a write there would travel silently into all of them; the concurrency
    /// token is the sensitive witness, since a repair that rewrote and re-saved a row would move it.
    /// </summary>
    [Fact]
    public async Task AuditAsync_reports_without_touching_a_single_row()
    {
        Assert.Equal(EfToolingExitCode.Success, (await RunAsync(EfToolingCommands.Apply)).ExitCode);
        var token = await SeedAsync(LegacyKey);
        var action = new SecretsProjectionReindex();

        await using var context = Context();
        Assert.True(await action.AuditAsync(context));
        Assert.True(await action.AuditAsync(context));

        var row = await context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(LegacyKey, row.TypeNameLookupKey);
        Assert.Equal(token, row.ConcurrencyToken);

        // And the repair, once asked for explicitly, leaves the revision alone while fixing the projection.
        await action.RunAsync(context);
        context.ChangeTracker.Clear();
        row = await context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(CurrentKey, row.TypeNameLookupKey);
        Assert.Equal(token, row.ConcurrencyToken);
        Assert.False(await action.AuditAsync(context));
    }

    /// <summary>The action is a plain type the seam can construct, which is what lets the CLI worker use it with no container.</summary>
    [Fact]
    public void The_declared_action_is_constructed_from_the_descriptor_alone()
    {
        var descriptor = Assert.Single(EfModuleCatalog.Discover([typeof(SecretsDbContext).Assembly]));
        var action = Assert.Single(EfPostMigrationActions.Create(descriptor.Name, descriptor.PostMigration));

        Assert.IsType<SecretsProjectionReindex>(action);
        Assert.Equal(nameof(SecretsProjectionReindex), action.Id);
        Assert.Equal("projection-reindex", action.Kind);
        Assert.Equal("legacy-projection-detected", action.RequiredWhen);
        Assert.Equal("SecretsProjectionContract.HasLegacyProjectionsAsync", action.Audit);
    }

    /// <summary>An action handed a context that is not this module's says so rather than failing deeper.</summary>
    [Fact]
    public async Task The_action_refuses_a_context_that_is_not_the_secrets_one()
    {
        await using var services = new ServiceCollection()
            .AddDbContext<ForeignDbContext>(options => options.UseSqlite(database.ConnectionString))
            .BuildServiceProvider();
        var context = services.GetRequiredService<ForeignDbContext>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SecretsProjectionReindex().AuditAsync(context));

        Assert.Contains(nameof(SecretsDbContext), failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ForeignDbContext), failure.Message, StringComparison.Ordinal);
    }

    private sealed class ForeignDbContext(DbContextOptions<ForeignDbContext> options) : DbContext(options);

    private SecretsSqliteDbContext Context() => new(new DbContextOptionsBuilder<SecretsSqliteDbContext>()
        .UseSqlite(database.ConnectionString, sqlite => sqlite
            .MigrationsAssembly(typeof(SecretsSqliteDbContext).Assembly.GetName().Name)
            .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
        .Options);

    /// <summary>Writes one row whose stored projection is <paramref name="typeNameLookupKey"/>, and returns its revision token.</summary>
    private async Task<byte[]> SeedAsync(string typeNameLookupKey)
    {
        await using var context = Context();
        var record = (SecretDocument.FromSecret(Secret(typeNameLookupKey)) with { TypeNameLookupKey = typeNameLookupKey }).ToRecord();
        context.Secrets.Add(record);
        await context.SaveChangesAsync();
        return record.ConcurrencyToken.ToArray();
    }

    private async Task<string> TypeNameLookupKeyAsync()
    {
        await using var context = Context();
        return (await context.Secrets.AsNoTracking().SingleAsync()).TypeNameLookupKey;
    }

    private async Task<(int ExitCode, JsonElement Body)> RunAsync(string command)
    {
        using var request = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                version = EfToolingContract.Version,
                command,
                provider = "Sqlite",
                selection = new { kind = "modules", modules = new[] { "Secrets" } },
                connection = database.ConnectionString
            },
            RequestJson));
        using var response = new MemoryStream();
        var exitCode = await EfToolingHost.RunAsync(request, response, [typeof(SecretsDbContext).Assembly]);
        using var document = JsonDocument.Parse(response.ToArray());
        return (exitCode, document.RootElement.Clone());
    }

    private static string? Code((int ExitCode, JsonElement Body) run) =>
        run.Body.GetProperty("error").GetProperty("code").GetString();

    private static string[] Details((int ExitCode, JsonElement Body) run) =>
        [.. run.Body.GetProperty("error").GetProperty("details").EnumerateArray().Select(detail => detail.GetString()!)];

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(value => value.GetString()!)];

    private static Secret Secret(string typeName) => new()
    {
        TenantId = "tenant-a",
        Name = "post.migrate",
        DisplayName = "Post migrate",
        TypeName = typeName,
        StoreName = SecretStoreNames.Encrypted,
        Versions = [new SecretVersion { Version = 1, Status = SecretStatus.Active, Payload = SecretPayload.FromValue("value") }]
    };
}
