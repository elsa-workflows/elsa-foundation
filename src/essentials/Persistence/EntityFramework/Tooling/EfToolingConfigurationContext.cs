using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal EfToolingConfigurationContext(
        string source,
        string hostDirectory,
        string hostName,
        string environment,
        string? shell,
        bool explicitSelection,
        IConfigurationRoot configuration)
    {
        Source = source;
        HostDirectory = hostDirectory;
        HostName = hostName;
        Environment = environment;
        Shell = shell;
        ExplicitSelection = explicitSelection;
        this.configuration = configuration;
    }

    internal string Source { get; }
    internal string HostDirectory { get; }
    internal string HostName { get; }
    internal string Environment { get; }
    internal string? Shell { get; }
    internal bool ExplicitSelection { get; }

    internal IConfiguration Configuration => disposed
        ? throw EfToolingRefusal.Resolution("configuration-context-disposed", "The selected configuration context is no longer available.")
        : configuration;

    /// <summary>Uses only the assembly at the selected host layout, sharing runtime's composer declaration rule.</summary>
    internal IEfToolingShellDefaults? CreateHostDefaults(Assembly hostAssembly)
    {
        _ = Configuration;
        ArgumentNullException.ThrowIfNull(hostAssembly);
        try
        {
            var expected = Path.Join(HostDirectory, $"{HostName}.dll");
            var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(hostAssembly.GetName().Name, HostName, StringComparison.Ordinal) ||
                !string.Equals(Path.GetFullPath(hostAssembly.Location), expected, pathComparison))
                throw EfToolingRefusal.Resolution("configuration-context-invalid", "The selected host assembly does not match the configuration context.");

            var composerType = EfToolingShellDefaultsDeclaration.ResolveComposerType(hostAssembly, required: false);
            if (composerType is null)
            {
                if (ExplicitSelection)
                    throw EfToolingRefusal.Resolution("host-not-enrolled", "The selected host does not declare an EF shell-default composer.");
                return null;
            }
            return EfToolingShellDefaultsDeclaration.Construct(composerType);
        }
        catch (EfToolingRefusal)
        {
            throw;
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw EfToolingRefusal.Resolution("host-composition-unavailable", "The selected host composer could not be verified.");
        }
    }

    /// <summary>Parses the worker's closed metadata-only descriptor before reading the host sources.</summary>
    internal static EfToolingConfigurationContext CreateFromRequest(Stream request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = JsonDocument.Parse(request);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw EfToolingRefusal.Usage("configuration-context-invalid", "The configuration context request must be an object.");

            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in document.RootElement.EnumerateObject())
                if (!fields.Add(field.Name))
                    throw EfToolingRefusal.Usage("configuration-context-invalid", "The configuration context request repeats a field.");

            var descriptor = document.RootElement.Deserialize<ContextRequest>(RequestJson)
                ?? throw EfToolingRefusal.Usage("configuration-context-invalid", "The configuration context request is empty.");
            if (descriptor.ContextVersion != Version || descriptor.ExplicitSelection is null)
                throw EfToolingRefusal.Usage("configuration-context-invalid", "The configuration context version or selection is not supported.");

            cancellationToken.ThrowIfCancellationRequested();
            return Create(descriptor.Source!, descriptor.HostDirectory!, descriptor.HostName!,
                descriptor.Environment!, descriptor.Shell, descriptor.ExplicitSelection.Value, cancellationToken);
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
            // Parser failures may include the original JSON text or path. Emit neither.
            throw EfToolingRefusal.Usage("configuration-context-invalid", "The configuration context request is not valid.");
        }
    }

    /// <summary>
    /// Checks one selected resource's named target against the separately supplied live connection.
    /// Offline operations must not call this method. Neither value is returned or included in a refusal.
    /// </summary>
    internal void VerifyExpectedConnection(string connectionName, string? actualConnection)
    {
        var snapshot = Configuration;
        if (string.IsNullOrWhiteSpace(connectionName))
            throw EfToolingRefusal.Resolution("expected-connection-unresolved", "The selected connection reference is not valid.");
        if (string.IsNullOrWhiteSpace(actualConnection))
            throw EfToolingRefusal.Usage("invalid-request", "A live operation needs a separately supplied connection.");

        string? expected;
        try
        {
            expected = snapshot.GetConnectionString(connectionName);
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            throw EfToolingRefusal.Resolution("configuration-context-invalid", "The selected connection reference could not be read.");
        }

        if (string.IsNullOrWhiteSpace(expected))
            throw EfToolingRefusal.Resolution("expected-connection-unresolved", "The selected named connection is absent or empty in this configuration context.");
        if (!string.Equals(expected, actualConnection, StringComparison.Ordinal))
            throw EfToolingRefusal.Resolution("connection-target-mismatch", "The supplied connection does not match the selected named connection in this configuration context.");
    }

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
            return new EfToolingConfigurationContext(source, hostDirectory, hostName, environment, shell, explicitSelection, snapshot);
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

    private sealed class ContextRequest
    {
        public int? ContextVersion { get; init; }
        public string? Source { get; init; }
        public string? HostDirectory { get; init; }
        public string? HostName { get; init; }
        public string? Environment { get; init; }
        public string? Shell { get; init; }
        public bool? ExplicitSelection { get; init; }
    }
}
