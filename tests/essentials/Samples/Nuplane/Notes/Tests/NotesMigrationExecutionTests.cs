using Elsa.Persistence.EntityFramework.SchemaBackfill;
using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Samples.Nuplane.Notes.Tests;

/// <summary>
/// The migrations run against a real SQLite database, release by release, the way an operator applies them: release 1.0.0's
/// Initial creates the table and the finalization tables, release 1.1.0's AddTags adds the one column, and a row written in
/// between is read by release 1.1.0 as a note with no tags, because the upcaster says so.
/// </summary>
public sealed class NotesMigrationExecutionTests : IAsyncDisposable
{
    private static readonly string[] ModuleTables =
        [.. new[] { NotesModule.TableName, $"__ElsaSchemaFinalization_{NotesModule.HistoryModuleName}", $"__ElsaDatabaseIdentity_{NotesModule.HistoryModuleName}" }.Order(StringComparer.Ordinal)];

    private readonly TemporarySqliteDatabase database = new("notes-migrations");
    private readonly NotesRelease release1 = NotesRelease.One();
    private readonly NotesRelease release2 = NotesRelease.Two();
    private readonly ServiceProvider services = new ServiceCollection().BuildServiceProvider();
    private readonly List<NotesDbContext> release2Contexts = [];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Each_release_model_is_what_its_snapshot_says(int release)
    {
        using var context = Release(release).SqliteContext(database.ConnectionString);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Initial_creates_the_notes_table_and_the_finalization_tables_and_AddTags_adds_the_one_column()
    {
        await using var context1 = release1.SqliteContext(database.ConnectionString);
        await NotesRelease.MigrateAsync(context1);

        Assert.Equal([release1.SqliteMigrationId("Initial")], [.. await context1.Database.GetAppliedMigrationsAsync()]);
        Assert.Equal(ModuleTables, Tables().Where(name => !name.StartsWith("__EF", StringComparison.Ordinal)));
        Assert.Equal(["CreatedAt", "Id", "SchemaVersion", "Text"], Columns(NotesModule.TableName));

        await using var context2 = release2.SqliteContext(database.ConnectionString);
        await NotesRelease.MigrateAsync(context2);

        Assert.Equal([release2.SqliteMigrationId("Initial"), release2.SqliteMigrationId("AddTags")], [.. await context2.Database.GetAppliedMigrationsAsync()]);
        Assert.Equal(["CreatedAt", "Id", "SchemaVersion", "TagsJson", "Text"], Columns(NotesModule.TableName));
        Assert.False(context2.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task A_row_written_under_release_1_reads_as_no_tags_through_release_2_and_the_upcaster()
    {
        await AddNoteUnderRelease1Async();
        var store = await MigrateToRelease2Async(release2.SqliteMigrationId("AddTags"));

        Assert.Equal([("1.0.0", null)], NoteStamps());
        var note = Assert.Single(await store.ListWithTagsAsync());
        Assert.Equal("written under 1.0.0", note.Text);
        Assert.Empty(note.Tags);
    }

    [Fact]
    public async Task The_rewriter_brings_a_1_0_0_note_up_to_2_0_0_with_empty_tags()
    {
        var id = await AddNoteUnderRelease1Async();
        var store = await MigrateToRelease2Async();

        Assert.Equal(EfSchemaRewriteOutcome.Rewritten, await store.RewriteAsync(Ask(id), NotesModule.TagsVersion));
        Assert.Equal([("2.0.0", "[]")], NoteStamps());
    }

    [Fact]
    public async Task The_rewriter_leaves_a_note_already_at_2_0_0_alone()
    {
        var id = await AddNoteUnderRelease1Async();
        var store = await MigrateToRelease2Async();
        await store.AddTagsAsync(id, ["demo"]);

        Assert.Equal(EfSchemaRewriteOutcome.AlreadyCurrent, await NewStore().RewriteAsync(Ask(id), NotesModule.TagsVersion));
        Assert.Equal([("2.0.0", "[\"demo\"]")], NoteStamps());
    }

    [Fact]
    public async Task The_rewriter_reports_a_note_that_no_longer_exists_as_missing()
    {
        await AddNoteUnderRelease1Async();
        var store = await MigrateToRelease2Async();

        Assert.Equal(EfSchemaRewriteOutcome.Missing, await store.RewriteAsync(Ask("no-such-note"), NotesModule.TagsVersion));
        Assert.Equal([("1.0.0", null)], NoteStamps());
    }

    [Fact]
    public async Task The_rewriter_reports_a_conflict_and_writes_nothing_when_the_note_changes_between_its_read_and_its_save()
    {
        var id = await AddNoteUnderRelease1Async();
        await MigrateToRelease2Async();
        // Another writer of the same note, as a host tagging it would be, lands after the rewriter has read the row and before it saves.
        var racing = NewStore();
        var rewriting = NewStore(new BeforeSave(() => racing.AddTagsAsync(id, ["raced"])));

        Assert.Equal(EfSchemaRewriteOutcome.Conflict, await rewriting.RewriteAsync(Ask(id), NotesModule.TagsVersion));
        Assert.Equal([("2.0.0", "[\"raced\"]")], NoteStamps());
    }

    [Fact]
    public async Task The_rewriter_refuses_to_write_a_note_below_the_version_it_must_reach()
    {
        var id = await AddNoteUnderRelease1Async();
        var store = await MigrateToRelease2Async();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RewriteAsync(Ask(id), "1.0.0"));
        Assert.Equal([("1.0.0", null)], NoteStamps());
    }

    [Fact]
    public async Task The_rewriter_refuses_to_write_when_the_finalization_gate_has_not_admitted_the_module()
    {
        var id = await AddNoteUnderRelease1Async();
        var store = await MigrateToRelease2Async();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RewriteAsync(Ask(id)));
        Assert.Equal([("1.0.0", null)], NoteStamps());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in release2Contexts)
            await context.DisposeAsync();
        release1.Dispose();
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

    private static EfSchemaRowToRewrite Ask(string key) => new(typeof(NoteRecord), [key], "1.0.0", NotesModule.TagsVersion);

    /// <summary>Migrates the database to release 1.0.0 and adds one note through that release, which stamps it 1.0.0. Returns its id.</summary>
    private async Task<string> AddNoteUnderRelease1Async()
    {
        await using var context1 = release1.SqliteContext(database.ConnectionString);
        await NotesRelease.MigrateAsync(context1);
        await release1.AddNoteAsync(context1, "written under 1.0.0");
        return Rows($"SELECT Id FROM {NotesModule.TableName}", row => row.GetString(0)).Single();
    }

    /// <summary>Applies release 1.1.0's migrations, up to <paramref name="targetMigration"/> when given, and returns a store on the release 1.1.0 context that did.</summary>
    private async Task<NoteStore> MigrateToRelease2Async(string? targetMigration = null)
    {
        var store = NewStore();
        await NotesRelease.MigrateAsync(release2Contexts[^1], targetMigration);
        return store;
    }

    private NoteStore NewStore(params IInterceptor[] interceptors)
    {
        var context = (NotesDbContext)release2.SqliteContext(database.ConnectionString, interceptors);
        release2Contexts.Add(context);
        return new NoteStore(context, services);
    }

    /// <summary>The stamp and the tags of every note, as the table holds them.</summary>
    private (string Stamp, string? Tags)[] NoteStamps() =>
        Rows($"SELECT SchemaVersion, TagsJson FROM {NotesModule.TableName}", row => (row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1)));

    private NotesRelease Release(int release) => release == 1 ? release1 : release2;

    private string[] Tables() => [.. Rows("SELECT name FROM sqlite_master WHERE type = 'table'", row => row.GetString(0)).Order(StringComparer.Ordinal)];

    private string[] Columns(string table) => [.. Rows($"SELECT name FROM pragma_table_info('{table}')", row => row.GetString(0)).Order(StringComparer.Ordinal)];

    private T[] Rows<T>(string sql, Func<SqliteDataReader, T> read)
    {
        using var connection = new SqliteConnection(database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
            rows.Add(read(reader));
        return [.. rows];
    }

    /// <summary>Runs <paramref name="action"/> when a save is about to start: after the rewriter has read its row, before it writes it.</summary>
    private sealed class BeforeSave(Func<Task> action) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await action();
            return result;
        }
    }
}
