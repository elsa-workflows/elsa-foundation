using Microsoft.Extensions.Configuration;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>
/// One host-owned configuration snapshot shared by every tooling operation in an invocation.
/// Configuration and connection values never leave the host persistence assembly.
/// </summary>
public sealed class EfToolingConfigurationContext : IDisposable
{
    public const int Version = 1;
    public const string WorkbenchJson = "workbench-json-v1";
    public const string WorkbenchJsonEnvironment = "workbench-json-environment-v1";

    private readonly IConfigurationRoot configuration;
    private bool disposed;

    internal EfToolingConfigurationContext(
        string source,
        string hostName,
        string environment,
        string? shell,
        bool explicitSelection,
        IConfigurationRoot configuration)
    {
        Source = source;
        HostName = hostName;
        Environment = environment;
        Shell = shell;
        ExplicitSelection = explicitSelection;
        this.configuration = configuration;
    }

    internal string Source { get; }
    internal string HostName { get; }
    internal string Environment { get; }
    internal string? Shell { get; }
    internal bool ExplicitSelection { get; }

    internal IConfiguration Configuration => disposed
        ? throw EfToolingRefusal.Resolution("configuration-context-disposed", "The selected configuration context is no longer available.")
        : configuration;

    /// <summary>Reads the selected files once, without reload or a public configuration projection.</summary>
    internal static EfToolingConfigurationContext Create(
        string source,
        string hostDirectory,
        string hostName,
        string environment,
        string? shell,
        bool explicitSelection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source is not (WorkbenchJson or WorkbenchJsonEnvironment))
            throw EfToolingRefusal.Usage("configuration-context-invalid", "The configuration source is not supported.");
        try
        {
            if (string.IsNullOrWhiteSpace(hostDirectory) || !Path.IsPathFullyQualified(hostDirectory) ||
                Path.GetFullPath(hostDirectory) != hostDirectory || !Directory.Exists(hostDirectory) ||
                !IsFileName(hostName) || !IsFileName(environment) ||
                (explicitSelection && string.IsNullOrWhiteSpace(shell)) ||
                (shell is not null && !IsFileName(shell)))
                throw EfToolingRefusal.Usage("configuration-context-invalid", "The configuration context selectors are not valid.");

            var builder = new ConfigurationBuilder()
                .AddJsonFile(Path.Join(hostDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Join(hostDirectory, $"appsettings.{environment}.json"), optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Join(hostDirectory, "shells.json"), optional: true, reloadOnChange: false)
                .AddJsonFile(Path.Join(hostDirectory, $"shells.{environment}.json"), optional: true, reloadOnChange: false);
            if (source == WorkbenchJsonEnvironment)
                builder.AddEnvironmentVariables();

            var snapshot = builder.Build();
            if (cancellationToken.IsCancellationRequested)
            {
                (snapshot as IDisposable)?.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new EfToolingConfigurationContext(source, hostName, environment, shell, explicitSelection, snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EfToolingRefusal)
        {
            throw;
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            // JSON/provider failures can embed source text or file paths. Neither reaches the worker.
            throw EfToolingRefusal.Resolution("configuration-context-invalid", "The selected host configuration could not be read.");
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            (configuration as IDisposable)?.Dispose();
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw EfToolingRefusal.Resolution("configuration-context-invalid", "The selected host configuration could not be released.");
        }
    }

    private static bool IsFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value != "." && value != ".." &&
        value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0 &&
        !value.Contains(':', StringComparison.Ordinal);
}
