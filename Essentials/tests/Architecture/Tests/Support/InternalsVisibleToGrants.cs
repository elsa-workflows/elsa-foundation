using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Finds every <c>InternalsVisibleTo</c> grant under a repository root and judges each against the rule that an
/// assembly opens its internals only to its own domain's test assemblies.
/// </summary>
/// <remarks>
/// <para>
/// The authority for a production project is the assembly the build produced: its <c>InternalsVisibleToAttribute</c>s
/// are what the runtime honors, whatever wrote them. That covers every way to declare a grant: the MSBuild item, an
/// <c>AssemblyAttribute</c> item, an attribute in source behind a using alias or with a computed argument, a linked
/// <c>Compile</c> item from outside the project folder, and an item from an imported <c>.props</c> or <c>.targets</c>
/// anywhere. A production project with no compiled assembly fails rather than passing unchecked.
/// </para>
/// <para>
/// The source scan says where a grant is declared, for the failure message. It is also the check for test projects,
/// which the build does not require here, and there an argument the scan cannot read as a literal fails closed.
/// </para>
/// </remarks>
internal static class InternalsVisibleToGrants
{
    private const string AttributeNamespace = "System.Runtime.CompilerServices";
    private const string AttributeName = "InternalsVisibleToAttribute";
    private const string AttributeTypeName = AttributeNamespace + ".InternalsVisibleTo";

    /// <summary>One grant: the project that opens its internals, the assembly it opens them to, and where it says so.</summary>
    /// <param name="Target">The assembly granted to, or <see langword="null"/> when the source argument is not a literal.</param>
    internal sealed record Grant(string Project, string ProjectPath, string? Target, string DeclaredIn);

    /// <summary>Every grant compiled into the built assembly of each production project under <paramref name="repoRoot"/>.</summary>
    internal static IReadOnlyList<Grant> FindCompiled(string repoRoot, string configuration) =>
        [.. ProductionProjects(repoRoot).SelectMany(project =>
            BuiltAssemblyPath(project, configuration) is { } assembly && File.Exists(assembly)
                ? CompiledTargets(assembly).Select(target => new Grant(AssemblyName(project), Relative(repoRoot, project), target, Relative(repoRoot, assembly)))
                : [])];

    /// <summary>
    /// Every grant that breaks the rule, every production project with no compiled assembly to read, and every shared build
    /// file that declares a grant for the projects importing it, as human-readable lines.
    /// </summary>
    internal static IReadOnlyList<string> Violations(string repoRoot, string configuration, IReadOnlySet<(string Project, string Target)> exceptions)
    {
        var assemblies = Projects(repoRoot)
            .GroupBy(project => AssemblyName(project), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Relative(repoRoot, group.First()), StringComparer.Ordinal);
        var declared = Projects(repoRoot).SelectMany(project => SourceGrantsOf(repoRoot, project)).ToArray();

        var unbuilt = ProductionProjects(repoRoot)
            .Where(project => BuiltAssemblyPath(project, configuration) is not { } path || !File.Exists(path))
            .Select(project => $"{AssemblyName(project)}: no compiled assembly at {Relative(repoRoot, BuiltAssemblyPath(project, configuration)!)}; " +
                               "build it first (the Architecture tests build every production project), because its grants are read from it");

        var compiled = FindCompiled(repoRoot, configuration)
            .Where(grant => !exceptions.Contains((grant.Project, grant.Target!)))
            .Select(grant => Judge(grant with { DeclaredIn = DeclarationOf(grant, declared) }, assemblies))
            .OfType<string>();

        var testProjectGrants = declared
            .Where(grant => ModuleRoots.IsTestPath(grant.ProjectPath))
            .Select(grant => grant.Target is null
                ? $"{grant.Project} ({grant.DeclaredIn}): an InternalsVisibleTo argument that is not a string literal cannot be judged; write the assembly name as a literal"
                : exceptions.Contains((grant.Project, grant.Target)) ? null : Judge(grant, assemblies))
            .OfType<string>();

        var sharedViolations = SharedBuildFiles(repoRoot)
            .Where(path => MsBuildGrantTargets(XDocument.Load(path)).Any())
            .Select(path => $"{Relative(repoRoot, path)} declares InternalsVisibleTo for every project that imports it; declare it in the granting project instead");

        return [.. unbuilt.Concat(compiled).Concat(testProjectGrants).Concat(sharedViolations).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>The assemblies a built assembly opens its internals to, read from its metadata without loading it.</summary>
    internal static IReadOnlyList<string> CompiledTargets(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var image = new PEReader(stream);
        var reader = image.GetMetadataReader();

        return
        [
            .. reader.GetAssemblyDefinition().GetCustomAttributes()
                .Select(reader.GetCustomAttribute)
                .Where(attribute => IsInternalsVisibleTo(reader, attribute.Constructor))
                .Select(attribute =>
                {
                    var value = reader.GetBlobReader(attribute.Value);
                    value.ReadUInt16(); // the custom-attribute prolog
                    return (value.ReadSerializedString() ?? string.Empty).Split(',')[0].Trim();
                })
                .Where(target => target.Length > 0)
        ];
    }

    private static bool IsInternalsVisibleTo(MetadataReader reader, EntityHandle constructor)
    {
        var type = constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default(EntityHandle)
        };

        return type.Kind switch
        {
            HandleKind.TypeReference => reader.GetTypeReference((TypeReferenceHandle)type) is var reference &&
                                        reader.StringComparer.Equals(reference.Name, AttributeName) &&
                                        reader.StringComparer.Equals(reference.Namespace, AttributeNamespace),
            HandleKind.TypeDefinition => reader.GetTypeDefinition((TypeDefinitionHandle)type) is var definition &&
                                         reader.StringComparer.Equals(definition.Name, AttributeName) &&
                                         reader.StringComparer.Equals(definition.Namespace, AttributeNamespace),
            _ => false
        };
    }

    /// <summary>
    /// The production project's built assembly: <c>bin/&lt;configuration&gt;/&lt;target framework&gt;/&lt;assembly&gt;.dll</c>
    /// beside its project file, the framework from the project file or the repository default.
    /// </summary>
    internal static string? BuiltAssemblyPath(string projectPath, string configuration)
    {
        var project = XDocument.Load(projectPath);
        var framework = project.Descendants().LastOrDefault(element => element.Name.LocalName == "TargetFramework")?.Value.Trim() is { Length: > 0 } declared
            ? declared
            : "net10.0";
        return Path.Join(Path.GetDirectoryName(projectPath), "bin", configuration, framework, $"{AssemblyName(projectPath)}.dll");
    }

    private static string? Judge(Grant grant, IReadOnlyDictionary<string, string> assemblies)
    {
        var granting = $"{grant.Project} -> {grant.Target} ({grant.DeclaredIn})";

        if (!assemblies.TryGetValue(grant.Target!, out var targetPath))
            return $"{granting}: names no project of this repository";

        if (!ModuleRoots.IsTestPath(targetPath))
            return $"{granting}: {targetPath} is not a test project; a project never opens its internals to another production project";

        var ownDomain = ElsaDomains.Of(grant.ProjectPath);
        if (ownDomain is null)
            return $"{granting}: {grant.ProjectPath} belongs to no domain, so it has no own test assemblies to grant to";

        var targetDomain = ElsaDomains.Of(targetPath);
        return targetDomain == ownDomain
            ? null
            : $"{granting}: {targetPath} tests domain '{targetDomain}', not the granting project's domain '{ownDomain}'";
    }

    /// <summary>Where a compiled grant is declared, as far as the source scan can tell.</summary>
    private static string DeclarationOf(Grant compiled, IReadOnlyList<Grant> declared)
    {
        if (declared.FirstOrDefault(grant => grant.Project == compiled.Project && grant.Target == compiled.Target) is { } literal)
            return literal.DeclaredIn;

        if (declared.FirstOrDefault(grant => grant.Project == compiled.Project && grant.Target is null) is { } computed)
            return $"{computed.DeclaredIn}, computed argument";

        return $"compiled into {compiled.DeclaredIn}; not declared in the project file or its own sources, " +
               "so a linked file, an imported build file or a computed argument";
    }

    private static IEnumerable<Grant> SourceGrantsOf(string repoRoot, string projectPath)
    {
        var project = AssemblyName(projectPath);
        var relativeProject = Relative(repoRoot, projectPath);

        foreach (var target in MsBuildGrantTargets(XDocument.Load(projectPath)))
            yield return new Grant(project, relativeProject, target, relativeProject);

        foreach (var source in SourceFiles(repoRoot, projectPath))
        {
            foreach (var target in AssemblyAttributeTargets(File.ReadAllText(source)))
                yield return new Grant(project, relativeProject, target, Relative(repoRoot, source));
        }
    }

    /// <summary>
    /// The assemblies a source file grants to through an assembly-level <c>InternalsVisibleTo</c> attribute, read from the
    /// parsed syntax so the same text in a comment or a string literal is no grant. The attribute is recognised by its own
    /// names and by any using alias of it; an argument that is not a string literal yields <see langword="null"/>.
    /// </summary>
    private static IEnumerable<string?> AssemblyAttributeTargets(string source)
    {
        if (!source.Contains("InternalsVisibleTo", StringComparison.Ordinal))
            return [];

        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var names = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(directive => directive.Alias is not null && directive.NamespaceOrType is NameSyntax target && IsAttributeName(RightmostName(target)))
            .SelectMany(directive => new[] { directive.Alias!.Name.Identifier.ValueText, directive.Alias.Name.Identifier.ValueText + "Attribute" })
            .Concat(["InternalsVisibleTo", "InternalsVisibleToAttribute"])
            .ToHashSet(StringComparer.Ordinal);

        return root.DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<AttributeListSyntax>()
            .Where(list => list.Target?.Identifier.ValueText == "assembly")
            .SelectMany(list => list.Attributes)
            .Where(attribute => names.Contains(RightmostName(attribute.Name)))
            .Select(attribute => attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
                ? literal.Token.ValueText.Split(',')[0].Trim()
                : null);
    }

    private static bool IsAttributeName(string name) => name is "InternalsVisibleTo" or "InternalsVisibleToAttribute";

    private static string RightmostName(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax alias => alias.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => name.ToString()
    };

    /// <summary>The assemblies an MSBuild file grants to through either item form.</summary>
    private static IEnumerable<string> MsBuildGrantTargets(XDocument document)
    {
        var items = document.Descendants()
            .Where(element => element.Name.LocalName == "InternalsVisibleTo")
            .SelectMany(element => (element.Attribute("Include")?.Value ?? string.Empty).Split(';'));

        var attributes = document.Descendants()
            .Where(element => element.Name.LocalName == "AssemblyAttribute" &&
                              (element.Attribute("Include")?.Value.Trim() is AttributeTypeName or AttributeTypeName + "Attribute"))
            .Select(element => element.Attribute("_Parameter1")?.Value ??
                               element.Elements().FirstOrDefault(child => child.Name.LocalName == "_Parameter1")?.Value ??
                               string.Empty);

        return items.Concat(attributes)
            .Select(target => target.Split(',')[0].Trim())
            .Where(target => target.Length > 0);
    }

    /// <summary>The project's own source files: its directory tree, without build output or a nested project's tree.</summary>
    private static IEnumerable<string> SourceFiles(string repoRoot, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var nested = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Where(directory => !string.Equals(directory, projectDirectory, StringComparison.Ordinal))
            .ToArray();

        return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path))
            .Where(path => !nested.Any(directory => path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    /// <summary>Every project file under <see cref="ModuleRoots.All"/>, build output excluded.</summary>
    private static IEnumerable<string> Projects(string repoRoot) =>
        ModuleRoots.Resolve(repoRoot, ModuleRoots.All)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path));

    private static IEnumerable<string> ProductionProjects(string repoRoot) =>
        Projects(repoRoot).Where(project => ModuleRoots.IsProductionPath(Relative(repoRoot, project)));

    /// <summary>The repository-root build files and every <c>.props</c> and <c>.targets</c> file under the module roots.</summary>
    private static IEnumerable<string> SharedBuildFiles(string repoRoot) =>
        Directory.EnumerateFiles(repoRoot, "*.props").Concat(Directory.EnumerateFiles(repoRoot, "*.targets"))
            .Concat(ModuleRoots.Resolve(repoRoot, ModuleRoots.All)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                .Where(path => path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                               path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
            .Where(path => !RepoPaths.IsBuildOutput(repoRoot, path))
            .Distinct(StringComparer.Ordinal);

    private static string AssemblyName(string projectPath) =>
        XDocument.Load(projectPath).Descendants().LastOrDefault(element => element.Name.LocalName == "AssemblyName")?.Value.Trim() is { Length: > 0 } name
            ? name
            : Path.GetFileNameWithoutExtension(projectPath);

    private static string Relative(string repoRoot, string path) => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
}
