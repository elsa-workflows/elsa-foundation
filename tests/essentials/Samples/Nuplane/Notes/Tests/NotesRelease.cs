using System.Reflection;
using System.Runtime.Loader;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes.Tests;

/// <summary>
/// One release of the sample as an assembly the tests can build a context and a store from. This project is compiled against
/// release 1.1.0, so release 1.1.0 is the assembly under test; release 1.0.0 is loaded, on first use, into a load context of its own
/// from the copy the project's build put under <c>notes-v1/</c>: two assemblies of one name, one process, nothing shared between them.
/// </summary>
internal sealed class NotesRelease : IDisposable
{
    private const string Namespace = "Elsa.Samples.Nuplane.Notes";
    private static readonly string Release1Path = Path.Join(AppContext.BaseDirectory, "notes-v1", "Elsa.Samples.Nuplane.Notes.dll");

    private readonly Lazy<(AssemblyLoadContext? Context, Assembly Assembly)> loaded;

    private NotesRelease(Func<(AssemblyLoadContext? Context, Assembly Assembly)> load) => loaded = new(load);

    public static NotesRelease One() => new(() =>
    {
        var context = new AssemblyLoadContext("notes-release-1", isCollectible: true);
        return (context, context.LoadFromAssemblyPath(Release1Path));
    });

    public static NotesRelease Two() => new(() => (null, typeof(NotesModule).Assembly));

    public Assembly Assembly => loaded.Value.Assembly;

    /// <summary>A Sqlite context of this release on <paramref name="connectionString"/>, with no provider calls beyond the connection.</summary>
    public DbContext SqliteContext(string connectionString)
    {
        var options = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(TypeNamed("NotesSqliteDbContext")))!;
        return (DbContext)Activator.CreateInstance(TypeNamed("NotesSqliteDbContext"), options.UseSqlite(connectionString).Options)!;
    }

    /// <summary>The id of this release's Sqlite migration <paramref name="name"/>, as the history table records it.</summary>
    public string SqliteMigrationId(string name) =>
        Assembly.GetType($"{Namespace}.Migrations.Notes.Sqlite.{name}")!.GetCustomAttribute<MigrationAttribute>()!.Id;

    /// <summary>Applies this release's migrations up to <paramref name="targetMigration"/>, or all of them when it is null.</summary>
    public static Task MigrateAsync(DbContext context, string? targetMigration = null) =>
        context.GetService<IMigrator>().MigrateAsync(targetMigration);

    /// <summary>Adds a note through this release's own store, which stamps it with the version this release may write.</summary>
    public async Task AddNoteAsync(DbContext context, string text)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var store = Activator.CreateInstance(TypeNamed("NoteStore"), context, services)!;
        await (Task)store.GetType().GetMethod("AddAsync")!.Invoke(store, [text, CancellationToken.None])!;
    }

    public void Dispose()
    {
        if (loaded.IsValueCreated)
            loaded.Value.Context?.Unload();
    }

    private Type TypeNamed(string name) => Assembly.GetType($"{Namespace}.{name}")!;
}
