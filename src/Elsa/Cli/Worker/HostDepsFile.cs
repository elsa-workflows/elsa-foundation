using System.Text.Json;

namespace Elsa.Cli.Worker;

/// <summary>Where a package id and version were determined from (FR-049).</summary>
public static class PackageSource
{
    public const string HostDepsFile = "host-deps-file";
    public const string ResolvedNupkg = "resolved-nupkg";
}

/// <summary>One package's identity as the manifest records it, and where that identity was read from.</summary>
public sealed record PackageFacts(string Id, string Version, string Source);

/// <summary>
/// The host's own <c>.deps.json</c>, read as what it is: the one statement of what this host pins. The
/// worker cannot ask the runtime "which package did this assembly come from" — that mapping exists only
/// here — and <c>EfToolingHost</c> deliberately refuses to invent it (FR-049), so this reader is what turns
/// a loaded assembly into a manifest entry.
/// </summary>
/// <remarks>
/// Only the active runtime target is read. A published, runtime-identifier-specific app carries both a
/// portable and a RID-specific target, and <c>runtimeTarget.name</c> is how the runtime itself picks
/// between them; picking differently here would report versions the host does not actually load.
/// </remarks>
public sealed class HostDepsFile
{
    private readonly Dictionary<string, PackageFacts> byAssembly = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PackageFacts> byPackageId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every managed assembly name the active target lists a runtime asset for.</summary>
    public IReadOnlyCollection<string> AssemblyNames => byAssembly.Keys;

    public static HostDepsFile Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw WorkerRefusal.Resolution("host-deps-file-unreadable", $"The host's dependency file '{path}' could not be read: {failure.Message}");
        }

        try
        {
            return Parse(text);
        }
        catch (JsonException failure)
        {
            throw WorkerRefusal.Resolution("host-deps-file-invalid", $"The host's dependency file '{path}' is not valid JSON: {failure.Message}");
        }
    }

    /// <summary>The package facts for the assembly with this simple name, or <c>null</c> when the host's deps file does not list it.</summary>
    public PackageFacts? ForAssembly(string assemblyName) => byAssembly.GetValueOrDefault(assemblyName);

    /// <summary>The package facts for this package id, or <c>null</c> when the host does not pin it at all.</summary>
    public PackageFacts? ForPackage(string packageId) => byPackageId.GetValueOrDefault(packageId);

    private static HostDepsFile Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var deps = new HostDepsFile();
        if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
            throw WorkerRefusal.Resolution("host-deps-file-invalid", "The host's dependency file declares no 'targets'.");

        var target = ActiveTarget(root, targets);
        foreach (var library in target.EnumerateObject())
        {
            var separator = library.Name.IndexOf('/');
            if (separator < 0)
                continue;
            var facts = new PackageFacts(library.Name[..separator], library.Name[(separator + 1)..], PackageSource.HostDepsFile);
            deps.byPackageId.TryAdd(facts.Id, facts);
            if (library.Value.ValueKind != JsonValueKind.Object || !library.Value.TryGetProperty("runtime", out var assets) || assets.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var asset in assets.EnumerateObject())
            {
                var name = Path.GetFileNameWithoutExtension(asset.Name.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(name))
                    deps.byAssembly[name] = facts;
            }
        }

        return deps;
    }

    private static JsonElement ActiveTarget(JsonElement root, JsonElement targets)
    {
        if (root.TryGetProperty("runtimeTarget", out var runtimeTarget) &&
            runtimeTarget.ValueKind == JsonValueKind.Object &&
            runtimeTarget.TryGetProperty("name", out var name) &&
            name.ValueKind == JsonValueKind.String &&
            targets.TryGetProperty(name.GetString()!, out var named))
        {
            return named;
        }

        foreach (var target in targets.EnumerateObject())
            return target.Value;

        throw WorkerRefusal.Resolution("host-deps-file-invalid", "The host's dependency file lists no target to resolve assemblies from.");
    }
}
