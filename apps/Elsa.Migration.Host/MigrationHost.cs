using System.Data.Common;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Cluster.Readability;
using Elsa.Foundation.DataProtection;
using Elsa.Foundation.Hosting;
using Elsa.Foundation.Identity.OpenIddict.EntityFrameworkCore;

namespace Elsa.Migration.Host;

/// <summary>
/// The temporary Elsa 3 migration host (spec 1236): a plain CShells host that runs the migration CLI's <c>migrated/</c> folder.
/// <see cref="Program"/> and the host tests both build it through <see cref="CreateBuilder"/> and <see cref="Build"/>, so a test
/// can put a test server on the builder before the app is built.
/// </summary>
/// <remarks>
/// Naming exception (constitution §E6 R2): the type repeats its namespace because specs 1236 (T005) and 1237 (T004a, the
/// migration CLI that composes what this host composes) name it <c>MigrationHost</c>. The host is temporary (#5800).
/// </remarks>
public static class MigrationHost
{
    /// <summary>The owner every endpoint this host maps is attributed to.</summary>
    public const string HostOwner = "Elsa.Migration.Host";

    /// <summary>The CORS policy that lets the repository's Studio host call this host from the browser.</summary>
    public const string StudioCorsPolicy = "ElsaStudio";

    /// <summary>The shell every setting and feature of this host lives in.</summary>
    public const string DefaultShell = "default";

    private const string WorkflowsFolderKey = "CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FolderPath";
    private const string ActivitiesFileKey = "CShells:Shells:default:Features:JsonActivityReconciliation:Options:FilePath";

    /// <summary>Creates the host's builder: its configuration layers, then its root services. Builds nothing and creates no folder.</summary>
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // -----------------------------------------------------------------------------------------------
        // Configuration (research R-3), lowest first: appsettings*.json (from CreateBuilder), shells.json,
        // shells.{Environment}.json, then the engineer's local, uncommitted sources: shells.Local.json, user
        // secrets, environment variables and the command line. CreateBuilder already added the last three below
        // the shells files; they are added again so they win over shells.json.
        // -----------------------------------------------------------------------------------------------
        var configuration = builder.Configuration;
        configuration.AddJsonFile("shells.json", optional: false, reloadOnChange: false);
        configuration.AddJsonFile($"shells.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false);
        configuration.AddJsonFile("shells.Local.json", optional: true, reloadOnChange: false);
        // Under a test the entry assembly is the test host, so the secrets are found through this assembly.
        configuration.AddUserSecrets(typeof(MigrationHost).Assembly, optional: true);
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);

        // -----------------------------------------------------------------------------------------------
        // Root services, composed once on the host container; CShells copies them into the shell.
        // -----------------------------------------------------------------------------------------------
        var services = builder.Services;

        // The initiative's JSON baseline (ADR 0091), as the Workbench registers it, so the API serializes as it does there.
        services.AddElsaJsonResponseHeaders();
        services.ConfigureHttpJsonOptions(options =>
        {
            Elsa.Serialization.Core.JsonBaseline.Configure(options.SerializerOptions);
            Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options.SerializerOptions, apiMode: true);
        });

        // The OpenIddict token store every shell issues and validates tokens against (in memory in development mode).
        services.AddOpenIddictEntityFrameworkCoreStore(configuration);
        // The schema readability report the EF modules finalize their schema versions against; without it a module may write
        // only its first schema version and activation is refused. It brings the in-process cluster of one.
        services.AddEfSchemaReadability();
        // The Data Protection key ring the shell protects its cookies and antiforgery tokens with.
        services.AddConfiguredDataProtection(configuration);
        // The shell runs its own ValidateOnStart checks as it activates; CShells runs none. Registered before CShells.
        services.AddShellStartupValidation();
        // CShells activates a shell without a listed feature it can't find; this host refuses it instead, naming the feature.
        services.AddSingleton(new RootConfiguration(configuration));
        services.AddShellInitializer<ComposedFeaturesCheck>(LifecyclePhase.Prepare, ComposedFeaturesCheck.PrepareOrder);

        // Browser access for the repository's Studio host. Content-Disposition carries an exported artifact's download
        // name and Retry-After the retry hint of a refused request; neither is a CORS-safelisted response header.
        var studioOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(StudioCorsPolicy, policy => policy
            .WithOrigins(studioOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("Content-Disposition", "Retry-After")));

        // Plain CShells: the features are found in this host's own referenced assemblies, never loaded from packages. An
        // unknown feature fails the shell's activation, naming it.
        services.AddCShellsAspNetCore(shells => shells
            .WithHostAssemblies()
            // Each shell's identity composition (schemes, permission policies) resolves at request time from the shell scope.
            .WithAuthenticationAndAuthorization()
            .WithWebRouting(options =>
            {
                options.EnablePathRouting = true;
                options.ExcludePaths = ["/health/live", "/health/ready"];
            })
            .WithConfigurationProvider(configuration));

        // /health/ready reads the tracker; the default shell is activated when the host starts.
        services.AddSingleton(new ShellActivationTracker());
        services.AddHostedService<StartupShellActivation>();

        // Root authentication and authorization, after CShells so its shell-delegating providers stay in place.
        services.AddAuthentication();
        services.AddAuthorization();

        return builder;
    }

    /// <summary>Creates the local folders the settings name, logs the paths in use, then builds the app and maps its pipeline.</summary>
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        PrepareLocalFolders(builder.Configuration);

        var app = builder.Build();
        LogPaths(app);

        app.UseCors(StudioCorsPolicy);
        app.MapFoundationHealth(HostOwner);
        app.MapShells();

        // After MapShells, whose middleware swaps the request services to the shell scope first.
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    /// <summary>
    /// Creates the folder of the SQLite file (<c>.db</c>, <c>.sqlite</c>, <c>.sqlite3</c>) <c>ConnectionStrings:Elsa</c> names when it is missing: nothing in the persistence
    /// code creates it. A relative data source resolves against the working directory, as SQLite resolves it.
    /// </summary>
    private static void PrepareLocalFolders(IConfiguration configuration)
    {
        var dataFile = SqliteDataFile(configuration);
        var folder = dataFile is null ? null : Path.GetDirectoryName(dataFile);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
    }

    private static string? SqliteDataFile(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Elsa");
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        var parts = new DbConnectionStringBuilder { ConnectionString = connectionString };
        foreach (var key in new[] { "Data Source", "DataSource", "Filename" })
        {
            if (parts.TryGetValue(key, out var value) && value is string path && IsSqliteFile(path))
                return Path.GetFullPath(path);
        }

        return null;
    }

    /// <summary>A SQLite database file; another engine's data source (a server name, for example) is not a local path.</summary>
    private static bool IsSqliteFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".db" or ".sqlite" or ".sqlite3";

    private static void LogPaths(WebApplication app)
    {
        var configuration = app.Configuration;
        app.Logger.LogInformation(
            "Elsa 3 migration host. Workflows folder: {WorkflowsFolder}. Activities file: {ActivitiesFile}. Data: {DataFile}.",
            FullPathOrUnset(configuration[WorkflowsFolderKey]),
            FullPathOrUnset(configuration[ActivitiesFileKey]),
            SqliteDataFile(configuration) ?? "(not a SQLite file)");
    }

    private static string FullPathOrUnset(string? path) => string.IsNullOrWhiteSpace(path) ? "(not set)" : Path.GetFullPath(path);
}
