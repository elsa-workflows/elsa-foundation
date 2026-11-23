using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace Elsa.Architecture.Tests;

/// <summary>The hash inventory includes executable hosts and Studio assemblies as well as runtime libraries.</summary>
internal sealed class ShippingHashAssemblies : IDisposable
{
    internal sealed record Project(string Path, string AssemblyPath, string? ImageSha256 = null, Guid? ImageMvid = null);
    internal IReadOnlyList<Project> Projects { get; }
    internal IReadOnlyList<Assembly> Assemblies { get; }
    internal IReadOnlyList<string> Errors { get; }
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> resolve;

    private ShippingHashAssemblies(IReadOnlyList<Project> projects, IReadOnlyList<Assembly> assemblies, List<string> errors,
        Func<AssemblyLoadContext, AssemblyName, Assembly?> resolve)
    {
        Projects = projects; Assemblies = assemblies; Errors = errors; this.resolve = resolve;
    }

    internal static ShippingHashAssemblies Load(string repoRoot, string configuration, Func<Project, IReadOnlyList<string>>? evaluatedInputs = null)
    {
        var projects = Discover(repoRoot, configuration);
        var errors = ValidateInputs(repoRoot, projects, configuration, evaluatedInputs);
        if (errors.Count != 0) return new(projects, [], errors, static (_, _) => null);
        projects = projects.Select(project => project with
        {
            ImageSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(project.AssemblyPath))),
            ImageMvid = ImageMvid(project.AssemblyPath)
        }).ToArray();
        var paths = projects.Where(project => File.Exists(project.AssemblyPath))
            .GroupBy(project => AssemblyName.GetAssemblyName(project.AssemblyPath).Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(project => project.AssemblyPath).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        foreach (var (name, candidates) in paths)
            if (candidates.Length != 1) errors.Add($"Shipping assembly '{name}' has multiple project outputs: {string.Join(", ", candidates.Select(path => Relative(repoRoot, path)))}");
        if (errors.Count != 0) return new(projects, [], errors, static (_, _) => null);
        var resolvers = projects.Where(project => File.Exists(project.AssemblyPath))
            .Select(project => new AssemblyDependencyResolver(project.AssemblyPath)).ToArray();
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            var path = paths.TryGetValue(name.Name!, out var candidates) && candidates.Length == 1 ? candidates[0]
                : resolvers.Select(resolver => resolver.ResolveAssemblyToPath(name)).FirstOrDefault(path => path is not null);
            return path is null ? null : context.LoadFromAssemblyPath(path);
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        var loaded = new List<Assembly>();
        try
        {
            foreach (var project in projects.Where(project => File.Exists(project.AssemblyPath)))
            {
                try
                {
                    var name = AssemblyName.GetAssemblyName(project.AssemblyPath);
                    var existing = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == name.Name);
                    if (existing is not null && !MatchesImage(existing, project.AssemblyPath))
                    {
                        errors.Add($"Loaded assembly '{name.Name}' differs from its shipping output; rebuild the Architecture project.");
                        continue;
                    }
                    loaded.Add(existing ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(project.AssemblyPath));
                }
                catch (Exception error) when (error is FileLoadException or FileNotFoundException or BadImageFormatException)
                {
                    errors.Add($"Cannot load shipping output for {Relative(repoRoot, project.Path)}: {error.Message}");
                }
            }
            return new(projects, loaded.Distinct().ToArray(), errors, Resolve);
        }
        catch
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
            throw;
        }
    }

    internal static IReadOnlyList<Project> Discover(string repoRoot, string configuration) =>
        ModuleRoots.Resolve(repoRoot, [.. ModuleRoots.Production, "Studio/src", "Extensions/*/studio"])
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path) && ModuleRoots.IsNotTestFile(repoRoot, path))
            .Distinct(StringComparer.Ordinal)
            .Select(path => new Project(path, InternalsVisibleToGrants.BuiltAssemblyPath(path, configuration)!))
            .OrderBy(project => Relative(repoRoot, project.Path), StringComparer.Ordinal).ToArray();

    internal static List<string> ValidateInputs(string repoRoot, IReadOnlyList<Project> projects, string configuration,
        Func<Project, IReadOnlyList<string>>? evaluatedInputs = null)
    {
        var errors = new List<string>();
        var settings = evaluatedInputs is null ? EvaluatedHashInputs.BuildSettings(repoRoot) : [];
        foreach (var project in projects)
        {
            if (!File.Exists(project.AssemblyPath))
            {
                errors.Add($"Missing shipping assembly for {Relative(repoRoot, project.Path)}: {Relative(repoRoot, project.AssemblyPath)}");
                continue;
            }
            IReadOnlyList<string> inputs;
            try { inputs = evaluatedInputs?.Invoke(project) ?? EvaluatedHashInputs.Read(project.Path, configuration, settings); }
            catch (Exception error) when (error is InvalidOperationException or System.Text.Json.JsonException or IOException)
            {
                errors.Add($"Cannot evaluate compile inputs for {Relative(repoRoot, project.Path)}: {error.Message}");
                continue;
            }
            var missing = inputs.Where(path => !File.Exists(path)).ToArray();
            if (missing.Length != 0)
            {
                errors.Add($"Missing evaluated compile inputs for {Relative(repoRoot, project.Path)}: {string.Join(", ", missing)}");
                continue;
            }
            var newest = inputs.MaxBy(File.GetLastWriteTimeUtc)!;
            if (File.GetLastWriteTimeUtc(newest) > File.GetLastWriteTimeUtc(project.AssemblyPath))
                errors.Add($"Stale shipping assembly for {Relative(repoRoot, project.Path)}; newer input: {Relative(repoRoot, newest)}");
        }
        return errors;
    }

    internal static bool MatchesImage(Assembly loaded, string path)
    {
        if (loaded.IsDynamic || loaded.GetName().FullName != AssemblyName.GetAssemblyName(path).FullName) return false;
        return loaded.ManifestModule.ModuleVersionId == ImageMvid(path);
    }

    private static Guid ImageMvid(string path)
    {
        using var stream = File.OpenRead(path);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }

    public void Dispose() => AssemblyLoadContext.Default.Resolving -= resolve;
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
