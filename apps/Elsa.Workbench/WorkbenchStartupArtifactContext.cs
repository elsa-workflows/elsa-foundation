using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Elsa.Workbench;

/// <summary>
/// Private, opt-in Workbench proof of a retained startup configuration lane. This is not a deployment
/// attestation: ASP.NET Core host inputs are read before this context exists, and package identity is separate.
/// </summary>
internal sealed class WorkbenchStartupArtifactContext
{
    private const string ManifestName = "workbench.startup-artifact.json";
    private const string Invalid = "WB-STARTUP-ARTIFACT-INVALID";
    private const string Mismatch = "WB-STARTUP-ARTIFACT-MISMATCH";

    // Keep every declared byte array, including unselected environment siblings, for this process lifetime.
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    private readonly IReadOnlyDictionary<string, int> _winningProviderIndexes;

    private WorkbenchStartupArtifactContext(
        IReadOnlyDictionary<string, byte[]> files,
        IConfigurationRoot configuration,
        IReadOnlyDictionary<string, int> winningProviderIndexes)
    {
        _files = files;
        Configuration = configuration;
        _winningProviderIndexes = winningProviderIndexes;
    }

    public IConfigurationRoot Configuration { get; }

    public static bool TryCapture(string contentRoot, string environment, string[] args,
        out WorkbenchStartupArtifactContext? context, out string refusalCode)
    {
        context = null;
        refusalCode = Invalid;
        try
        {
            var manifestPath = Path.Combine(contentRoot, ManifestName);
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).LinkTarget is not null)
                return false;

            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(manifestPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (manifest is not { SchemaVersion: 1, Files: { Length: > 0 } } ||
                !string.Equals(manifest.Environment, environment, StringComparison.Ordinal))
                return false;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                if (file.Name is null || !IsConfigurationFile(file.Name) || !names.Add(file.Name) ||
                    file.Sha256 is null || file.Sha256.Length != 64 ||
                    file.Sha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                    return false;
            }

            // An explicit manifest is the authority; this check refuses an undeclared top-level sibling.
            // It is not a substitute for a deployer retaining an immutable release outside the process.
            var presentNames = Directory.EnumerateFiles(contentRoot, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name is not null && IsConfigurationFile(name))
                .Select(name => name!);
            if (!names.SetEquals(presentNames))
                return false;

            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                var path = Path.Combine(contentRoot, file.Name!);
                if (!File.Exists(path) || new FileInfo(path).LinkTarget is not null)
                    return false;

                var bytes = File.ReadAllBytes(path);
                var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (!string.Equals(digest, file.Sha256, StringComparison.Ordinal))
                {
                    refusalCode = Mismatch;
                    return false;
                }

                files.Add(file.Name!, bytes);
            }

            var source = new ConfigurationBuilder();
            AddJson(source, files, "appsettings.json");
            AddJson(source, files, $"appsettings.{environment}.json");
            AddJson(source, files, "shells.json");
            AddJson(source, files, $"shells.{environment}.json");
            source.AddEnvironmentVariables().AddCommandLine(args);
            var configuration = source.Build();
            var providers = configuration.Providers.ToArray();
            var winners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, _) in configuration.AsEnumerable())
                for (var i = 0; i < providers.Length; i++)
                    if (providers[i].TryGet(key, out _))
                        winners[key] = i;

            context = new WorkbenchStartupArtifactContext(files, configuration, winners);
            refusalCode = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           JsonException or ArgumentException or InvalidOperationException)
        {
            // Neither a path nor a configuration value may escape in the refusal output.
            return false;
        }
    }

    private static bool IsConfigurationFile(string name) =>
        (name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) ||
         name.Equals("shells.json", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("shells.", StringComparison.OrdinalIgnoreCase)) &&
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        name.IndexOfAny(['/', '\\', ':']) < 0;

    private static void AddJson(ConfigurationBuilder source, IReadOnlyDictionary<string, byte[]> files, string name)
    {
        if (files.TryGetValue(name, out var bytes))
            source.AddJsonStream(new MemoryStream(bytes, writable: false));
    }

    private sealed class Manifest
    {
        public int SchemaVersion { get; set; }
        public string? Environment { get; set; }
        public ManifestFile[]? Files { get; set; }
    }

    private sealed class ManifestFile
    {
        public string? Name { get; set; }
        public string? Sha256 { get; set; }
    }
}
