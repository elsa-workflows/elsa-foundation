using Elsa.Persistence.EntityFramework.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        await using (var context1 = release1.SqliteContext(database.ConnectionString))
        {
            await NotesRelease.MigrateAsync(context1);
            await release1.AddNoteAsync(context1, "written under 1.0.0");
        }

        await using var context2 = release2.SqliteContext(database.ConnectionString);
        await NotesRelease.MigrateAsync(context2, release2.SqliteMigrationId("AddTags"));

        Assert.Equal([("1.0.0", null)], Rows($"SELECT SchemaVersion, TagsJson FROM {NotesModule.TableName}", row => (row.GetString(0), row.IsDBNull(1) ? null : row.GetString(1))));
        var note = Assert.Single(await new NoteStore((NotesDbContext)context2, services).ListWithTagsAsync());
        Assert.Equal("written under 1.0.0", note.Text);
        Assert.Empty(note.Tags);
    }

    public async ValueTask DisposeAsync()
    {
        release1.Dispose();
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

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
}
