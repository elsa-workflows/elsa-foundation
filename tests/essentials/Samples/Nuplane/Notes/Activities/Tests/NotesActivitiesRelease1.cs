using System.Reflection;
using System.Runtime.Loader;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes.Activities.Tests;

/// <summary>
/// Release 1.0.0 of the activity package and of the Notes package it was built against, loaded into a load context of their
/// own from the copies the project's build put under <c>release-1/</c>. Everything else, the activity contracts and EF Core
/// among them, is the test's own, as a host shares it with the packages it loads. Only the context's own lookup finds
/// release 1.0.0 of Notes: the default context would answer release 1.1.0, which this project is compiled against, and the
/// activity would then run on the newer store without anything failing.
/// </summary>
internal sealed class NotesActivitiesRelease1 : IDisposable
{
    private const string Namespace = "Elsa.Samples.Nuplane.Notes";

    private readonly ReleaseLoadContext _context = new(Path.Join(AppContext.BaseDirectory, "release-1"));

    public Assembly Activities => _context.LoadFromAssemblyName(new AssemblyName($"{Namespace}.Activities"));

    public Assembly Notes => _context.LoadFromAssemblyName(new AssemblyName(Namespace));

    public IActivityReconciliationSource ReconciliationSource() =>
        (IActivityReconciliationSource)Activator.CreateInstance(Activities.GetType($"{Namespace}.Activities.NotesActivityReconciliationSource", throwOnError: true)!)!;

    /// <summary>
    /// Migrates the database at <paramref name="connectionString"/> to release 1.0.0 of Notes, then runs release 1.0.0 of
    /// "Add note" with <paramref name="text"/> in a scope of its own, as the runtime does, over that release's store.
    /// </summary>
    public async Task AddNoteAsync(string connectionString, string text, ActivityExecutionContext execution)
    {
        await using var context = SqliteContext(connectionString);
        await context.Database.MigrateAsync();

        var store = NotesType("NoteStore");
        await using var services = new ServiceCollection()
            .AddScoped(store, provider => Activator.CreateInstance(store, context, provider)!)
            .BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        var activity = (IActivity)Activator.CreateInstance(Activities.GetType($"{Namespace}.Activities.AddNote", throwOnError: true)!, scope.ServiceProvider)!;
        activity.GetType().GetProperty("Text")!.SetValue(activity, text);

        await activity.ExecuteAsync(execution);
    }

    public void Dispose() => _context.Unload();

    private DbContext SqliteContext(string connectionString)
    {
        var context = NotesType("NotesSqliteDbContext");
        var options = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(context))!;
        return (DbContext)Activator.CreateInstance(context, options.UseSqlite(connectionString).Options)!;
    }

    private Type NotesType(string name) => Notes.GetType($"{Namespace}.{name}", throwOnError: true)!;

    /// <summary>Loads the two sample assemblies from <paramref name="directory"/>, and leaves everything else to the default context.</summary>
    private sealed class ReleaseLoadContext(string directory) : AssemblyLoadContext("notes-activities-release-1", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) =>
            Path.Join(directory, $"{name.Name}.dll") is var path && File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }
}
