using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;
using PostgreSqlMigrations = Elsa.Samples.Nuplane.Notes.Migrations.Notes.PostgreSql;
using SqliteMigrations = Elsa.Samples.Nuplane.Notes.Migrations.Notes.Sqlite;

namespace Elsa.Samples.Nuplane.Notes.Tests;

/// <summary>
/// The 1.1.0 migration is what makes a rolling upgrade safe: the hosts still on 1.0.0 keep working against the migrated
/// database. It runs through <see cref="ExpandOnlyMigrationGuard"/>, the same check spec 185 holds every first-party
/// migration to, and the baseline creates the finalization tables the module's gate keeps its record in.
/// </summary>
public sealed class NotesMigrationTests
{
    public static TheoryData<string, Migration> AddTagsMigrations() => new()
    {
        { "Sqlite", new SqliteMigrations.AddTags() },
        { "PostgreSql", new PostgreSqlMigrations.AddTags() }
    };

    public static TheoryData<string, Migration> InitialMigrations() => new()
    {
        { "Sqlite", new SqliteMigrations.Initial() },
        { "PostgreSql", new PostgreSqlMigrations.Initial() }
    };

    [Theory]
    [MemberData(nameof(AddTagsMigrations))]
    public void The_tags_migration_passes_the_expand_only_guard_by_adding_one_nullable_column(string provider, Migration migration)
    {
        var result = ExpandOnlyMigrationGuard.Evaluate(migration.UpOperations, optOut: null, ExpandOnlyMigrationFamilies.None);

        Assert.True(result.Passed, $"{provider}: {string.Join(", ", result.Violations)}");
        var added = Assert.IsType<AddColumnOperation>(Assert.Single(migration.UpOperations));
        Assert.Equal((NotesModule.TableName, nameof(NoteRecord.TagsJson), true), (added.Table, added.Name, added.IsNullable));
    }

    [Theory]
    [MemberData(nameof(InitialMigrations))]
    public void The_initial_migration_creates_the_notes_table_and_the_finalization_tables(string provider, Migration migration)
    {
        string[] tables = [.. migration.UpOperations.OfType<CreateTableOperation>().Select(table => table.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(
            [.. new[] { NotesModule.TableName, $"__ElsaSchemaFinalization_{NotesModule.HistoryModuleName}", $"__ElsaDatabaseIdentity_{NotesModule.HistoryModuleName}" }.Order(StringComparer.Ordinal)],
            tables);
        // The baseline knows nothing of tags: they arrive with 1.1.0, in a migration of their own.
        Assert.DoesNotContain(migration.UpOperations.OfType<CreateTableOperation>().SelectMany(table => table.Columns), column => column.Name == nameof(NoteRecord.TagsJson));
    }
}
