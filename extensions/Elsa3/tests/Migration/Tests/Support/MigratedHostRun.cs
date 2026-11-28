using CShells.Lifecycle;
using Elsa.Migration.Host;
using Elsa3.Migration.Cli;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Migration.Tests.Support;

/// <summary>
/// The migration host's committed composition, built in process through <c>MigrationHost.CreateBuilder</c> and
/// <c>MigrationHost.Build</c> (spec 1236 R-5) with its JSON workflow and activity reconciliation settings pointed at a
/// <c>migrated/</c> folder, and its default shell activated, which reconciles and publishes that folder (spec 1237 T024).
/// The app itself is never started, so no server listens. The content root holds copies of the host's committed settings
/// files; the database, the lock folder and the migrated paths are command-line overrides, the highest source.
/// </summary>
internal sealed class MigratedHostRun : IAsyncDisposable
{
    private const string Features = "CShells:Shells:default:Features";

    private readonly WebApplication _app;
    private readonly string _root;

    private MigratedHostRun(WebApplication app, IShell shell, string root)
    {
        _app = app;
        Shell = shell;
        _root = root;
    }

    public IShell Shell { get; }

    /// <summary>Composes the host on <paramref name="migratedFolder"/> and activates its default shell.</summary>
    public static async Task<MigratedHostRun> StartAsync(string migratedFolder)
    {
        var root = Path.Combine(Path.GetTempPath(), "elsa3-migration-tests", "host-" + Guid.NewGuid().ToString("N"));
        var content = Path.Combine(root, "content");
        Directory.CreateDirectory(content);
        var hostFolder = CliHostWiring.Create().HostProjectFolder;
        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json", "shells.json" })
            File.Copy(Path.Combine(hostFolder, file), Path.Combine(content, file));

        var settings = new Dictionary<string, string>
        {
            [$"{Features}:JsonWorkflowReconciliation:Options:FolderPath"] = Path.Combine(migratedFolder, "workflows"),
            [$"{Features}:JsonActivityReconciliation:Options:FilePath"] = Path.Combine(migratedFolder, "activities", "activities.json"),
            ["ConnectionStrings:Elsa"] = $"Data Source={Path.Combine(root, "data", "elsa.db")}",
            [$"{Features}:FileSystemDistributedLocking:LocksFolderPath"] = Path.Combine(root, "locks")
        };
        var args = new List<string> { "--environment=Development", $"--contentRoot={content}" };
        args.AddRange(settings.Select(setting => $"--{setting.Key}={setting.Value}"));

        WebApplication? app = null;
        try
        {
            app = MigrationHost.Build(MigrationHost.CreateBuilder([.. args]));
            var shell = await app.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(MigrationHost.DefaultShell, CancellationToken.None);
            return new MigratedHostRun(app, shell, root);
        }
        catch
        {
            if (app is not null)
                await app.DisposeAsync();
            Delete(root);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _app.DisposeAsync();
        }
        finally
        {
            Delete(_root);
        }
    }

    private static void Delete(string root)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
