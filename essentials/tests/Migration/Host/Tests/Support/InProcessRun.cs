using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;

namespace Elsa.Migration.Host.Tests.Support;

/// <summary>
/// One in-process run of the migration host (spec 1236 R-5): <c>MigrationHost.CreateBuilder</c>, a test server, then
/// <c>MigrationHost.Build</c>, in Development, on its own temporary content root, data folder and migrated folder. The
/// content root holds copies of the host's committed settings files only, so an engineer's git-ignored
/// <c>shells.Local.json</c> in the host project never reaches a test. User secrets still can (the host adds them through
/// its assembly's <c>UserSecretsId</c>); every setting a test relies on is a command-line override, which wins over them. Each setting override goes in as a command-line argument, the highest configuration source.
/// </summary>
internal sealed class InProcessRun : IAsyncDisposable
{
    private const string Features = "CShells:Shells:default:Features";

    private InProcessRun(string root, WebApplication app, CapturingLoggerProvider logs)
    {
        Root = root;
        App = app;
        Logs = logs;
        Client = app.GetTestClient();
    }

    public string Root { get; }
    public WebApplication App { get; }
    public HttpClient Client { get; }
    public CapturingLoggerProvider Logs { get; }

    public static string WorkflowsFolderKey => $"{Features}:JsonWorkflowReconciliation:Options:FolderPath";
    public static string ActivitiesFileKey => $"{Features}:JsonActivityReconciliation:Options:FilePath";

    /// <summary>A temporary migrated folder: the host's sample, or empty workflows and an empty activities file.</summary>
    public static MigratedFolder NewMigratedFolder(bool withSample)
    {
        var root = NewRoot();
        var migrated = Path.Combine(root, "migrated");
        Directory.CreateDirectory(Path.Combine(migrated, "workflows"));
        Directory.CreateDirectory(Path.Combine(migrated, "activities"));
        if (withSample)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(HostPaths.ProjectFolder, "migrated", "workflows"), "*.json"))
                File.Copy(file, Path.Combine(migrated, "workflows", Path.GetFileName(file)));
        }

        File.WriteAllText(Path.Combine(migrated, "activities", "activities.json"), "[]");
        return new MigratedFolder(root, Path.Combine(migrated, "workflows"), Path.Combine(migrated, "activities", "activities.json"));
    }

    /// <summary>Starts the host on <paramref name="folder"/> with <paramref name="settings"/> as command-line overrides.</summary>
    public static async Task<InProcessRun> StartAsync(MigratedFolder folder, IDictionary<string, string>? settings = null)
    {
        var all = new Dictionary<string, string>
        {
            [WorkflowsFolderKey] = folder.WorkflowsFolder,
            [ActivitiesFileKey] = folder.ActivitiesFile,
            ["ConnectionStrings:Elsa"] = $"Data Source={Path.Combine(folder.Root, "data", "elsa.db")}",
            // The file-system lock folder of this run, so runs never share one.
            [$"{Features}:FileSystemDistributedLocking:LocksFolderPath"] = Path.Combine(folder.Root, "locks"),
            // A loaded build machine commits each Durable step slowly; the sync HTTP response must not time out first.
            [$"{Features}:ActivitiesHttp:SyncRequestTimeout"] = "00:03:00"
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            all[key] = value;

        var args = new List<string> { "--environment=Development", $"--contentRoot={CommittedContentRoot(folder.Root)}" };
        args.AddRange(all.Select(setting => $"--{setting.Key}={setting.Value}"));

        var builder = MigrationHost.CreateBuilder([.. args]);
        builder.WebHost.UseTestServer();
        var logs = new CapturingLoggerProvider();
        builder.Logging.AddProvider(logs);

        var app = MigrationHost.Build(builder);
        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return new InProcessRun(folder.Root, app, logs);
    }

    /// <summary>A content root under <paramref name="root"/> with copies of the host's committed settings files.</summary>
    public static string CommittedContentRoot(string root)
    {
        var contentRoot = Path.Combine(root, "content");
        Directory.CreateDirectory(contentRoot);
        foreach (var file in new[] { "shells.json", "appsettings.json", "appsettings.Development.json" })
            File.Copy(Path.Combine(HostPaths.ProjectFolder, file), Path.Combine(contentRoot, file), overwrite: true);
        return contentRoot;
    }

    /// <summary>A fresh temporary root for one test.</summary>
    public static string NewRoot() => Path.Combine(Path.GetTempPath(), "elsa-migration-host-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Deletes a test's temporary root; a file the runtime still holds open is left for the OS to clean up.</summary>
    public static void DeleteRoot(string root)
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

    /// <summary>
    /// Signs in with the seeded development administrator of the committed shells.json through <c>/_elsa/identity/login</c>,
    /// then exchanges the cookie for a bearer token at <c>/_elsa/identity/token</c>, as the JTest login template does.
    /// </summary>
    public async Task<string> SignInAsync()
    {
        using var login = await Client.PostAsync("/_elsa/identity/login",
            new StringContent("""{"username":"admin","password":"Password123!"}""", System.Text.Encoding.UTF8, "application/json"));
        login.EnsureSuccessStatusCode();
        var cookies = login.Headers.GetValues("Set-Cookie").Select(cookie => cookie.Split(';')[0]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/_elsa/identity/token");
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var token = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return token.RootElement.GetProperty("accessToken").GetString()!;
    }

    public async Task<System.Text.Json.JsonDocument> GetJsonAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET {path} returned {(int)response.StatusCode}: {body}");
        return System.Text.Json.JsonDocument.Parse(body);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        DeleteRoot(Root);
    }
}

/// <summary>A temporary migrated folder and the root it, the data folder and the lock folder live in.</summary>
internal sealed record MigratedFolder(string Root, string WorkflowsFolder, string ActivitiesFile);

/// <summary>Keeps every log line of a run, so a test can assert that the log names a feature, folder or file.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public string Text => string.Join(Environment.NewLine, _lines);

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)}{(exception is null ? "" : " " + exception)}");
        }
    }
}

/// <summary>Where the host project and the repository are, found from the test's output folder.</summary>
internal static class HostPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ProjectFolder { get; } = Path.Combine(RepositoryRoot, "apps", "Elsa.Migration.Host");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"No Elsa.Server.slnx above '{AppContext.BaseDirectory}'.");
    }
}
