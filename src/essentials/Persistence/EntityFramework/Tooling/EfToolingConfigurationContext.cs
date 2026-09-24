using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CShells.Configuration;
using CShells.Features;
using Elsa.Persistence.EntityFramework.ResourceResolution;
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
            if (!string.Equals(hostAssembly.GetName().Name, HostName, StringComparison.Ordinal) ||
                !SameHostAssemblyPath(hostAssembly.Location, HostDirectory, HostName))
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

    /// <summary>Resolves one shell's resource intent from the frozen sources and selected host closure.</summary>
    internal EfPersistencePreparationResult PrepareShell(
        IEfToolingShellDefaults hostDefaults,
        IEnumerable<Assembly> hostAssemblies,
        CancellationToken cancellationToken)
    {
        if (Shell is null)
            throw EfToolingRefusal.Usage("configuration-context-invalid", "One shell is required for this tooling operation.");
        return PrepareShell(Shell, hostDefaults, hostAssemblies, cancellationToken);
    }

    /// <summary>Checks every configured shell before an unselected invocation may use legacy tooling.</summary>
    internal EfToolingContextInspection InspectUnselectedHost(
        IEfToolingShellDefaults? hostDefaults,
        IEnumerable<Assembly> hostAssemblies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostAssemblies);
        var snapshot = Configuration;
        if (ExplicitSelection || Shell is not null)
            throw EfToolingRefusal.Usage("configuration-context-invalid", "An unselected-host inspection requires no explicit shell.");
        cancellationToken.ThrowIfCancellationRequested();

        if (hostDefaults is null)
        {
            if (HasResourceHint(snapshot))
                throw EfToolingRefusal.Resolution("host-composition-unavailable", "This host has persistence resource configuration but no declared shell composer.");
            return new EfToolingContextInspection("legacy-only", ["host-not-enrolled"]);
        }

        var shells = snapshot.GetSection("CShells:Shells").GetChildren()
            .Select(section => section.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (shells.Length == 0)
            throw EfToolingRefusal.Resolution("host-composition-unavailable", "The selected host has no configured shells to inspect.");

        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shell in shells)
        {
            var prepared = PrepareShell(shell, hostDefaults, hostAssemblies, cancellationToken);
            if (prepared.HasApplicableResource)
                throw EfToolingRefusal.Resolution("configuration-context-required", "This host has an enabled persistence consumer with resource intent; select an explicit configuration context and shell.");
            foreach (var code in prepared.UnresolvedCodes)
                unresolved.Add(code);
        }

        return new EfToolingContextInspection("no-resource-applicable", unresolved.Order(StringComparer.Ordinal).ToArray());
    }

    private EfPersistencePreparationResult PrepareShell(
        string shell,
        IEfToolingShellDefaults hostDefaults,
        IEnumerable<Assembly> hostAssemblies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostDefaults);
        ArgumentNullException.ThrowIfNull(hostAssemblies);
        var snapshot = Configuration;
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var closure = hostAssemblies.Where(x => !x.IsDynamic).Distinct().ToArray();
            var descriptors = FeatureDiscovery.DiscoverFeatures(closure)
                .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var builder = new ShellBuilder(shell);
            hostDefaults.Configure(builder, snapshot);
            builder.FromConfiguration(snapshot.GetSection($"CShells:Shells:{shell}"));
            var settings = builder.Build();
            var result = EfPersistencePreparation.Prepare(settings, descriptors, snapshot, closure,
                verifyConnectionValues: false);
            var enabled = result.ActiveFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var usages = EfProviderAgreement.Discover(closure)
                .Where(usage => enabled.Contains(usage.Feature))
                .ToArray();
            var providers = usages.Where(usage => usage.DeclaresProvider)
                .ToDictionary(usage => usage.Feature,
                    usage => settings.ConfigurationData.FirstOrDefault(entry =>
                        StringComparer.OrdinalIgnoreCase.Equals(entry.Key, $"{usage.Feature}:Provider")).Value?.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            cancellationToken.ThrowIfCancellationRequested();
            return result with { HostFeatureUsages = usages, ConfiguredProviders = providers };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (EfToolingHost.IsNonFatal(failure))
        {
            // Composition and discovery exceptions can contain authored values or paths.
            throw EfToolingRefusal.Resolution("host-composition-unavailable", "The selected host shell could not be composed for tooling.");
        }
    }

    private static bool HasResourceHint(IConfiguration snapshot)
    {
        if (snapshot.GetSection("Elsa:Persistence").GetChildren().Any(section =>
                IsKey(section, "Resources") || IsKey(section, "DefaultResource")))
            return true;
        return snapshot.GetSection("CShells:Shells").GetChildren().Any(shell =>
            shell.GetSection("Configuration:Elsa:Persistence").GetChildren().Any(section =>
                IsKey(section, "DefaultResource") || IsKey(section, "Bindings")));

        static bool IsKey(IConfigurationSection section, string key) =>
            StringComparer.OrdinalIgnoreCase.Equals(section.Key, key);
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

    internal static bool SameHostAssemblyPath(string assemblyLocation, string hostDirectory, string hostName)
    {
        // Assembly.Location resolves parent symlinks (for example macOS /var -> /private/var).
        // Resolve the selected directory the same way without changing the configuration source path.
        var expected = Path.Join(ResolvePhysicalDirectory(hostDirectory), $"{hostName}.dll");
        return string.Equals(Path.GetFullPath(assemblyLocation), expected,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string ResolvePhysicalDirectory(string path)
    {
        var root = Path.GetPathRoot(path)!;
        var resolved = root;
        foreach (var segment in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Join(resolved, segment);
            resolved = new DirectoryInfo(resolved).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? resolved;
        }
        return resolved;
    }

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

internal sealed record EfToolingContextInspection(string Outcome, IReadOnlyList<string> UnresolvedCodes);
