using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace Elsa.Architecture.Tests;

/// <summary>The hash inventory includes executable hosts and Studio assemblies as well as runtime libraries.</summary>
internal sealed class ShippingHashAssemblies : IDisposable
{
    internal sealed record Project(string Path, string AssemblyPath, string? ImageSha256 = null, Guid? ImageMvid = null,
        IReadOnlySet<string>? CompileSources = null, string? IntermediateRoot = null, IReadOnlyDictionary<string, string>? PackageSources = null,
        IReadOnlyDictionary<string, SdkCompileReferences.Asset>? CompileReferences = null);
    internal IReadOnlyList<Project> Projects { get; }
    internal IReadOnlyList<Assembly> Assemblies { get; }
    internal IReadOnlyList<string> Errors { get; }
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> resolve;
    private readonly AssemblyLoadContext? studioContext;

    private ShippingHashAssemblies(IReadOnlyList<Project> projects, IReadOnlyList<Assembly> assemblies, List<string> errors,
        Func<AssemblyLoadContext, AssemblyName, Assembly?> resolve, AssemblyLoadContext? studioContext = null)
    {
        Projects = projects; Assemblies = assemblies; Errors = errors; this.resolve = resolve; this.studioContext = studioContext;
    }

    internal static ShippingHashAssemblies Load(string repoRoot, string configuration, Func<Project, IReadOnlyList<string>>? evaluatedInputs = null)
    {
        var projects = Discover(repoRoot, configuration);
        var evaluator = new EvaluatedHashInputs(configuration);
        var errors = ValidateInputs(repoRoot, projects, configuration, evaluatedInputs ?? (project => evaluator.Read(project.Path)));
        if (errors.Count != 0) return new(projects, [], errors, static (_, _) => null);
        projects = projects.Select(project => project with
        {
            ImageSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(project.AssemblyPath))),
            ImageMvid = ImageMvid(project.AssemblyPath),
            CompileSources = evaluatedInputs is null ? evaluator.CompileSources(project.Path) : null,
            IntermediateRoot = evaluatedInputs is null ? evaluator.IntermediateRoot(project.Path) : null,
            PackageSources = evaluatedInputs is null ? evaluator.PackageSources(project.Path) : null,
            CompileReferences = evaluatedInputs is null ? evaluator.CompileReferences(project.Path) : null
        }).ToArray();
        var paths = projects.Where(project => File.Exists(project.AssemblyPath))
            .GroupBy(project => AssemblyName.GetAssemblyName(project.AssemblyPath).Name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(project => project.AssemblyPath).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        foreach (var (name, candidates) in paths)
            if (candidates.Length != 1) errors.Add($"Shipping assembly '{name}' has multiple project outputs: {string.Join(", ", candidates.Select(path => Relative(repoRoot, path)))}");
        if (errors.Count != 0) return new(projects, [], errors, static (_, _) => null);
        bool Studio(Project project) => IsStudioProject(repoRoot, project.Path);
        var backend = projects.Where(project => !Studio(project)).ToArray();
        var studio = projects.Where(Studio).OrderByDescending(project => Path.GetFileName(project.AssemblyPath) == "Elsa.Studio.Web.dll").ToArray();
        var resolvers = backend
            .Select(project => new AssemblyDependencyResolver(project.AssemblyPath)).ToArray();
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            var path = backend.FirstOrDefault(project => Path.GetFileNameWithoutExtension(project.AssemblyPath) == name.Name)?.AssemblyPath
                ?? resolvers.Select(resolver => resolver.ResolveAssemblyToPath(name)).FirstOrDefault(path => path is not null);
            return path is null ? null : context.LoadFromAssemblyPath(path);
        }
        var studioResolvers = studio.Select(project => new AssemblyDependencyResolver(project.AssemblyPath)).ToArray();
        var frameworkDirectories = new[] { typeof(object).Assembly.Location, typeof(Microsoft.AspNetCore.Builder.WebApplication).Assembly.Location }
            .Select(path => Path.GetDirectoryName(path)!).ToHashSet(StringComparer.Ordinal);
        Assembly? ResolveStudio(AssemblyLoadContext context, AssemblyName name)
        {
            var existing = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == name.Name);
            if (existing is not null && frameworkDirectories.Contains(Path.GetDirectoryName(existing.Location)!)) return existing;
            var candidate = studioResolvers.Select(resolver => resolver.ResolveAssemblyToPath(name)).FirstOrDefault(path => path is not null);
            if (candidate is null && paths.TryGetValue(name.Name!, out var known)) candidate = known.Single();
            if (candidate is null)
            {
                var framework = frameworkDirectories.Select(directory => Path.Join(directory, name.Name + ".dll")).FirstOrDefault(File.Exists);
                if (framework is not null) return AssemblyLoadContext.Default.LoadFromAssemblyPath(framework);
                throw new FileNotFoundException($"Unresolved Studio inspection dependency '{name.FullName}'.");
            }
            var owned = projects.FirstOrDefault(project => Path.GetFileNameWithoutExtension(project.AssemblyPath) == name.Name);
            if (owned is not null && (ImageMvid(candidate) != owned.ImageMvid || AssemblyName.GetAssemblyName(candidate).FullName != AssemblyName.GetAssemblyName(owned.AssemblyPath).FullName))
                throw new FileLoadException($"Studio dependency copy '{name.Name}' differs from its shipping project output.");
            // Share contract identity only for the exact same image. ABI-different third-party
            // versions stay private to Studio instead of borrowing the testhost's backend version.
            if (existing is not null && (owned is null || !Studio(owned)) && MatchesImage(existing, candidate)) return existing;
            var backendCandidate = resolvers.Select(resolver => resolver.ResolveAssemblyToPath(name)).FirstOrDefault(path => path is not null);
            if (owned is null && backendCandidate is not null && ImageMvid(backendCandidate) == ImageMvid(candidate) &&
                AssemblyName.GetAssemblyName(backendCandidate).FullName == AssemblyName.GetAssemblyName(candidate).FullName)
            {
                if (existing is not null) throw new FileLoadException($"Shared dependency '{name.Name}' differs from its loaded backend image.");
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(backendCandidate);
            }
            if (owned is not null && !Studio(owned))
            {
                if (existing is not null) throw new FileLoadException($"Backend contract '{name.Name}' differs from Studio's resolved image.");
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }
            return context.LoadFromAssemblyPath(candidate);
        }
        var studioLoader = new StudioInspectionContext(ResolveStudio);
        AssemblyLoadContext.Default.Resolving += Resolve;
        var loaded = new List<Assembly>();
        try
        {
            foreach (var project in backend.Concat(studio))
            {
                try
                {
                    var name = AssemblyName.GetAssemblyName(project.AssemblyPath);
                    var context = Studio(project) ? studioLoader : AssemblyLoadContext.Default;
                    var existing = context.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == name.Name);
                    if (existing is not null && !MatchesImage(existing, project.AssemblyPath))
                    {
                        errors.Add($"Loaded assembly '{name.Name}' differs from its shipping output; rebuild the Architecture project.");
                        continue;
                    }
                    loaded.Add(existing ?? context.LoadFromAssemblyPath(project.AssemblyPath));
                }
                catch (Exception error) when (error is FileLoadException or FileNotFoundException or BadImageFormatException)
                {
                    errors.Add($"Cannot load shipping output for {Relative(repoRoot, project.Path)}: {error.Message}");
                }
            }
            return new(projects, loaded.Distinct().ToArray(), errors, Resolve, studioLoader);
        }
        catch
        {
            AssemblyLoadContext.Default.Resolving -= Resolve;
            studioLoader.Unload();
            throw;
        }
    }

    internal static IReadOnlyList<Project> Discover(string repoRoot, string configuration) =>
        ModuleRoots.Resolve(repoRoot, [.. ModuleRoots.Production, "Studio/src", "extensions/*/studio"])
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path) && ModuleRoots.IsNotTestFile(repoRoot, path))
            .Distinct(StringComparer.Ordinal)
            .Select(path => new Project(path, InternalsVisibleToGrants.BuiltAssemblyPath(path, configuration)!))
            .OrderBy(project => Relative(repoRoot, project.Path), StringComparer.Ordinal).ToArray();

    internal static bool IsStudioProject(string repoRoot, string project)
    {
        var parts = Relative(repoRoot, project).Split('/');
        return parts.Length > 2 && parts[0] == "Studio" && parts[1] == "src" ||
            parts.Length > 3 && parts[0] == "extensions" && parts[2] == "studio";
    }

    internal static List<string> ValidateInputs(string repoRoot, IReadOnlyList<Project> projects, string configuration,
        Func<Project, IReadOnlyList<string>>? evaluatedInputs = null)
    {
        var errors = new List<string>();
        var evaluator = new EvaluatedHashInputs(configuration);
        foreach (var project in projects)
        {
            if (!File.Exists(project.AssemblyPath))
            {
                errors.Add($"Missing shipping assembly for {Relative(repoRoot, project.Path)}: {Relative(repoRoot, project.AssemblyPath)}");
                continue;
            }
            IReadOnlyList<string> inputs;
            try { inputs = evaluatedInputs?.Invoke(project) ?? evaluator.Read(project.Path); }
            catch (Exception error) when (error is InvalidOperationException or System.Text.Json.JsonException or System.Xml.XmlException or IOException or TimeoutException)
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

    private sealed class StudioInspectionContext(Func<AssemblyLoadContext, AssemblyName, Assembly?> resolve)
        : AssemblyLoadContext("S9 shipping Studio inspection " + Guid.NewGuid().ToString("N"), isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) => resolve(this, name);
    }

    public void Dispose()
    {
        AssemblyLoadContext.Default.Resolving -= resolve;
        studioContext?.Unload();
    }
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
