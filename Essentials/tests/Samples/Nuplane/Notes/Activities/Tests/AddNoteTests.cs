using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.InProcess;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Primitives.Exceptions;
using Elsa.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Samples.Nuplane.Notes.Activities.Tests;

/// <summary>
/// "Add note" run as the runtime runs it, in a scope of its own, against a real SQLite database: release 1.0.0 over release
/// 1.0.0 of Notes, and release 1.1.0 over release 1.1.0 of Notes and the shared dormancy check, which answers from what this
/// host observed of the notes family's finalization.
/// </summary>
public sealed class AddNoteTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("notes-activities");
    private readonly NotesActivitiesRelease1 _release1 = new();
    private readonly FakeObservedSchemaFinalization _finalization = new();
    private readonly ServiceProvider _release2;

    public AddNoteTests() => _release2 = new ServiceCollection()
        .AddDbContext<NotesSqliteDbContext>(options => options.UseSqlite(_database.ConnectionString))
        .AddScoped<NotesDbContext>(provider => provider.GetRequiredService<NotesSqliteDbContext>())
        .AddScoped<NoteStore>()
        .AddScoped<NotesWithTags>()
        .AddSingleton<ISchemaDormancyCheck>(new SchemaDormancyCheck(observed: _finalization))
        .BuildServiceProvider();

    [Fact]
    public async Task Release_1_0_0_writes_the_note()
    {
        await _release1.AddNoteAsync(_database.ConnectionString, "  written by 1.0.0 ", Execution());

        Assert.Equal(new Version(1, 0, 0, 0), _release1.Notes.GetName().Version);
        Assert.Equal([("written by 1.0.0", "1.0.0")], Rows($"SELECT Text, SchemaVersion FROM {NotesModule.TableName}", row => (row.GetString(0), row.GetString(1))));
    }

    [Fact]
    public async Task Release_1_1_0_writes_the_note_and_its_tags_once_2_0_0_is_finalized()
    {
        await MigrateToRelease2Async();
        Finalized(NotesModule.TagsVersion);

        await AddNoteAsync("tagged", tags: "demo, designer,");

        Assert.Equal([("tagged", NotesModule.TagsVersion, """["demo","designer"]""")], NoteRows());
    }

    [Fact]
    public async Task Release_1_1_0_refuses_a_tagged_note_while_2_0_0_is_not_finalized_and_writes_nothing()
    {
        await MigrateToRelease2Async();
        Finalized("1.0.0");

        var refusal = await Assert.ThrowsAsync<SchemaDormancyRefusedException>(() => AddNoteAsync("tagged", tags: "demo"));

        Assert.Equal(NotesModule.TagsFeature, refusal.FeatureId);
        Assert.Empty(NoteRows());
    }

    [Fact]
    public async Task Release_1_1_0_writes_an_untagged_note_in_the_old_format_while_2_0_0_is_not_finalized()
    {
        await MigrateToRelease2Async();
        Finalized("1.0.0");

        await AddNoteAsync("untagged", tags: null);

        Assert.Equal([("untagged", "1.0.0", null)], NoteRows());
    }

    public async ValueTask DisposeAsync()
    {
        await _release2.DisposeAsync();
        _release1.Dispose();
        await _database.DisposeAsync();
    }

    private static ActivityExecutionContext Execution() => new("workflow", "invocation", "attempt", "add-note", CancellationToken.None);

    private async Task MigrateToRelease2Async()
    {
        await using var scope = _release2.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotesSqliteDbContext>().Database.MigrateAsync();
    }

    /// <summary>What this host observed of the notes family: <paramref name="version"/> finalized, and adopted as its write version.</summary>
    private void Finalized(string version) => _finalization.Set(new SchemaFamilyObservation(
        NotesModule.Family, NotesModule.Name, NotesModule.Chain.ReadableVersions, version, version, false, null, [], null, DateTimeOffset.UtcNow));

    private async Task AddNoteAsync(string text, string? tags)
    {
        await using var scope = _release2.CreateAsyncScope();
        IActivity activity = new AddNote(scope.ServiceProvider) { Text = text, Tags = tags };
        await activity.ExecuteAsync(Execution());
    }

    /// <summary>The text, stamp and tags of every note, as the table holds them.</summary>
    private (string Text, string Stamp, string? Tags)[] NoteRows() =>
        Rows($"SELECT Text, SchemaVersion, TagsJson FROM {NotesModule.TableName}", row => (row.GetString(0), row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2)));

    private T[] Rows<T>(string sql, Func<SqliteDataReader, T> read)
    {
        using var connection = new SqliteConnection(_database.ConnectionString);
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
