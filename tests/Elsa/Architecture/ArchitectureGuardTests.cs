using System.Text.Json.Nodes;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class ArchitectureGuardTests
{
    private static readonly string[] AllowedCorePackageReferences =
    [
        "Microsoft.Extensions.Primitives",
        "Microsoft.Extensions.Options"
    ];

    private static readonly HashSet<(string Project, string Reference)> DeferredRuntimeDesignReferences =
    [
        ("Elsa.Workflows.Runtime.JavaScript", "Elsa.Workflows.Design.Core")
    ];

    [Fact]
    public void Solution_has_no_global_layer_marker_folders()
    {
        var solution = XDocument.Load(Path.Combine(RepoRoot, "Elsa.Server.slnx"));
        var folders = solution.Descendants("Folder")
            .Select(x => x.Attribute("Name")?.Value)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("/core/", folders);
        Assert.DoesNotContain("/modules/", folders);
    }

    [Fact]
    public void Project_paths_match_domain_tree_convention()
    {
        var mismatches = ProjectFiles()
            .Select(project => (Project: project, Expected: ExpectedProjectPath(project)))
            .Where(x => x.Project.RelativePath != x.Expected)
            .Select(x => $"{x.Project.Name}: expected {x.Expected}, actual {x.Project.RelativePath}")
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Solution_folders_collapse_leaf_project_segments()
    {
        var projectDirectories = ProjectFiles()
            .Select(project => Path.GetDirectoryName(project.RelativePath)!)
            .ToHashSet(StringComparer.Ordinal);
        var expectedFolders = ProjectFiles()
            .ToDictionary(project => project.RelativePath, project => ExpectedSolutionFolder(project, projectDirectories), StringComparer.OrdinalIgnoreCase);
        var actualFolders = SolutionProjects()
            .ToDictionary(project => project.Path, project => project.Folder, StringComparer.OrdinalIgnoreCase);
        var mismatches = expectedFolders
            .Where(expected => !actualFolders.TryGetValue(expected.Key, out var actual) || actual != expected.Value)
            .Select(expected =>
            {
                actualFolders.TryGetValue(expected.Key, out var actual);
                return $"{expected.Key}: expected {expected.Value}, actual {actual ?? "<missing>"}";
            })
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Core_projects_do_not_reference_implementation_projects()
    {
        var violations = ProjectFiles()
            .Where(project => project.Name.EndsWith(".Core", StringComparison.Ordinal))
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => !IsCoreSafeReference(reference.Name))
                .Select(reference => $"{project.Name} -> {reference.Name}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Core_projects_do_not_reference_heavy_packages()
    {
        var violations = ProjectFiles()
            .Where(project => project.Name.EndsWith(".Core", StringComparison.Ordinal))
            .SelectMany(project => PackageReferences(project)
                .Where(package => !IsCoreSafePackage(package))
                .Select(package => $"{project.Name} -> {package}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Elsa_primitives_has_no_external_package_references()
    {
        var primitives = ProjectFiles().Single(x => x.Name == "Elsa.Primitives");

        Assert.Empty(PackageReferences(primitives));
    }

    [Fact]
    public void Runtime_projects_do_not_add_design_references()
    {
        var violations = ProjectFiles()
            .Where(IsRuntimeProject)
            .SelectMany(project => ProjectReferences(project)
                .Where(reference =>
                    reference.Name.StartsWith("Elsa.Workflows.Design.", StringComparison.Ordinal) ||
                    reference.Name.StartsWith("Elsa.Activities.Design.", StringComparison.Ordinal))
                .Where(reference => !DeferredRuntimeDesignReferences.Contains((project.Name, reference.Name)))
                .Select(reference => $"{project.Name} -> {reference.Name}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Runtime_projects_do_not_reference_elsa3_compatibility_projects()
    {
        var violations = ProjectFiles()
            .Where(IsRuntimeProject)
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference.Name.StartsWith("Elsa3.", StringComparison.Ordinal))
                .Select(reference => $"{project.Name} -> {reference.Name}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("shells.json")]
    [InlineData("shells.baseline.json")]
    public void Server_default_shell_enables_flowchart_runtime_feature(string fileName)
    {
        var features = ReadDefaultShellFeatures(Path.Combine(RepoRoot, "src", "Apps", "Elsa.Server", fileName));

        Assert.True(
            features.ContainsKey("ActivitiesFlowchart"),
            $"{fileName} must enable ActivitiesFlowchart so Flowchart root activities can resolve runtime services.");
    }

    [Fact]
    public void Workflows_runtime_core_does_not_use_authored_workflow_models()
    {
        string[] forbiddenPatterns =
        [
            "Elsa.Workflows.Design",
            "WorkflowDefinitionState",
            "ActivityNode"
        ];
        var runtimeCoreDirectory = Path.Combine(RepoRoot, "src", "Elsa", "Workflows", "Runtime", "Core");
        var violations = Directory.EnumerateFiles(runtimeCoreDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file =>
            {
                var text = StripCommentsAndStringLiterals(File.ReadAllText(file));
                return forbiddenPatterns
                    .Where(pattern => text.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => $"{Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/')}: {pattern}");
            })
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static bool IsRuntimeProject(ProjectInfo project) =>
        project.Name == "Elsa.Workflows.Runtime"
        || project.Name.StartsWith("Elsa.Workflows.Runtime.", StringComparison.Ordinal)
        || project.Name == "Elsa.Activities.Runtime"
        || project.Name.StartsWith("Elsa.Activities.Runtime.", StringComparison.Ordinal);

    [Fact]
    public void Source_scan_strips_interpolated_string_text_but_preserves_interpolation_code()
    {
        const string text = "var message = $\"ActivityNode literal {typeof(ActivityNode).Name}\";";
        var sanitized = StripCommentsAndStringLiterals(text);

        Assert.DoesNotContain("ActivityNode literal", sanitized, StringComparison.Ordinal);
        Assert.Contains("typeof(ActivityNode)", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_scan_strips_interpolated_raw_string_text_but_preserves_interpolation_code()
    {
        const string text = "var message = $\"\"\"ActivityNode literal {typeof(ActivityNode).Name}\"\"\";";
        var sanitized = StripCommentsAndStringLiterals(text);

        Assert.DoesNotContain("ActivityNode literal", sanitized, StringComparison.Ordinal);
        Assert.Contains("typeof(ActivityNode)", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_scan_preserves_multi_dollar_raw_interpolation_code_with_nested_braces()
    {
        const string text = "var message = $$\"\"\"ActivityNode literal {{ new { Name = typeof(ActivityNode).Name } }}\"\"\";";
        var sanitized = StripCommentsAndStringLiterals(text);

        Assert.DoesNotContain("ActivityNode literal", sanitized, StringComparison.Ordinal);
        Assert.Contains("typeof(ActivityNode)", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_scan_strips_raw_string_text()
    {
        const string text = "\"\"\"ActivityNode literal\"\"\"";
        var sanitized = StripCommentsAndStringLiterals(text);

        Assert.DoesNotContain("ActivityNode", sanitized, StringComparison.Ordinal);
    }

    private static bool IsCoreSafeReference(string referenceName) =>
        referenceName.EndsWith(".Core", StringComparison.Ordinal) ||
        referenceName == "Elsa.Primitives" ||
        referenceName.EndsWith(".Primitives", StringComparison.Ordinal);

    private static bool IsCoreSafePackage(string packageName) =>
        AllowedCorePackageReferences.Contains(packageName) ||
        packageName.EndsWith(".Abstractions", StringComparison.Ordinal);

    private static IEnumerable<ProjectInfo> ProjectFiles()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            var project = ProjectInfo.From(RepoRoot, file);
            if (IsGeneratedScratchProject(project))
                continue;

            yield return project;
        }

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "tests"), "*.csproj", SearchOption.AllDirectories))
            yield return ProjectInfo.From(RepoRoot, file);
    }

    // The extension-builder feature writes runtime-generated scratch projects under guid-named
    // project/snapshot folders (gitignored, never part of the solution). Exclude them from the
    // domain-tree convention checks so generated artifacts are not enshrined in the slnx.
    private static bool IsGeneratedScratchProject(ProjectInfo project) =>
        project.RelativePath.Contains("/extension-builder/projects/", StringComparison.Ordinal);

    private static IEnumerable<SolutionProjectInfo> SolutionProjects()
    {
        var solution = XDocument.Load(Path.Combine(RepoRoot, "Elsa.Server.slnx"));
        foreach (var folder in solution.Descendants("Folder"))
        {
            var folderName = folder.Attribute("Name")?.Value;
            if (folderName is null)
                continue;

            foreach (var project in folder.Elements("Project"))
            {
                var path = project.Attribute("Path")?.Value;
                if (path is not null)
                    yield return new SolutionProjectInfo(folderName, path.Replace('\\', '/'));
            }
        }
    }

    private static IEnumerable<ProjectInfo> ProjectReferences(ProjectInfo project)
    {
        var document = XDocument.Load(project.FullPath);
        foreach (var include in document.Descendants("ProjectReference").Select(x => x.Attribute("Include")?.Value).OfType<string>())
        {
            var normalizedInclude = include.Replace('\\', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.FullPath)!, normalizedInclude));
            yield return ProjectInfo.From(RepoRoot, path);
        }
    }

    private static IEnumerable<string> PackageReferences(ProjectInfo project)
    {
        var document = XDocument.Load(project.FullPath);
        return document.Descendants("PackageReference")
            .Where(x => !IsBuildTimeOnlyReference(x))
            .Select(x => x.Attribute("Include")?.Value)
            .OfType<string>();
    }

    private static bool IsBuildTimeOnlyReference(XElement reference)
    {
        var attribute = reference.Attribute("PrivateAssets")?.Value;
        if (string.Equals(attribute, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        var child = reference.Elements("PrivateAssets").FirstOrDefault()?.Value;
        return string.Equals(child, "all", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject ReadDefaultShellFeatures(string path)
    {
        var document = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException($"{Path.GetFileName(path)} is not a JSON object.");

        return document["CShells"]?["Shells"]?["default"]?["Features"] as JsonObject
            ?? throw new InvalidOperationException($"{Path.GetFileName(path)} must contain CShells.Shells.default.Features.");
    }

    private static string StripCommentsAndStringLiterals(string text)
    {
        var sanitized = new char[text.Length];
        var state = SourceScanState.Code;
        var interpolationReturnState = SourceScanState.Code;
        var interpolationCloseBraceCount = 1;
        var interpolationDepth = 0;
        var rawStringDollarCount = 0;
        var rawStringQuoteCount = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            switch (state)
            {
                case SourceScanState.Code when current == '/' && next == '/':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.LineComment;
                    break;
                case SourceScanState.Code when current == '/' && next == '*':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.BlockComment;
                    break;
                case SourceScanState.Code when TryReadRawStringStart(text, i, out var rawStringPrefixLength, out rawStringQuoteCount, out var detectedRawStringDollarCount):
                    rawStringDollarCount = detectedRawStringDollarCount;
                    for (var j = 0; j < rawStringPrefixLength; j++)
                        sanitized[i + j] = ' ';
                    i += rawStringPrefixLength - 1;
                    state = rawStringDollarCount == 0 ? SourceScanState.RawString : SourceScanState.InterpolatedRawString;
                    break;
                case SourceScanState.Code when current == '$' && next == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.InterpolatedString;
                    break;
                case SourceScanState.Code when current == '$' && next == '@' && i + 2 < text.Length && text[i + 2] == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.InterpolatedVerbatimString;
                    break;
                case SourceScanState.Code when current == '@' && next == '$' && i + 2 < text.Length && text[i + 2] == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.InterpolatedVerbatimString;
                    break;
                case SourceScanState.Code when current == '@' && next == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.VerbatimString;
                    break;
                case SourceScanState.Code when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.String;
                    break;
                case SourceScanState.Code when current == '\'':
                    sanitized[i] = ' ';
                    state = SourceScanState.Character;
                    break;
                case SourceScanState.Code:
                    sanitized[i] = current;
                    break;
                case SourceScanState.LineComment when current is '\r' or '\n':
                    sanitized[i] = current;
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.BlockComment when current == '*' && next == '/':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.String when current == '\\' && next != '\0':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.String when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.VerbatimString when current == '"' && next == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.VerbatimString when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.RawString when HasRun(text, i, '"', rawStringQuoteCount):
                    for (var j = 0; j < rawStringQuoteCount; j++)
                        sanitized[i + j] = ' ';
                    i += rawStringQuoteCount - 1;
                    rawStringQuoteCount = 0;
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.InterpolatedRawString when HasRun(text, i, '"', rawStringQuoteCount):
                    for (var j = 0; j < rawStringQuoteCount; j++)
                        sanitized[i + j] = ' ';
                    i += rawStringQuoteCount - 1;
                    rawStringDollarCount = 0;
                    rawStringQuoteCount = 0;
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.InterpolatedRawString when HasRun(text, i, '{', rawStringDollarCount):
                    for (var j = 0; j < rawStringDollarCount; j++)
                        sanitized[i + j] = '{';
                    i += rawStringDollarCount - 1;
                    interpolationDepth = 1;
                    interpolationCloseBraceCount = rawStringDollarCount;
                    interpolationReturnState = state;
                    state = SourceScanState.InterpolationExpression;
                    break;
                case SourceScanState.InterpolatedRawString:
                    sanitized[i] = current is '\r' or '\n' ? current : ' ';
                    break;
                case SourceScanState.InterpolatedString when current == '\\' && next != '\0':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedString when current == '{' && next == '{':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedString when current == '{':
                    sanitized[i] = current;
                    interpolationDepth = 1;
                    interpolationCloseBraceCount = 1;
                    interpolationReturnState = state;
                    state = SourceScanState.InterpolationExpression;
                    break;
                case SourceScanState.InterpolatedString when current == '}':
                    sanitized[i] = next == '}' ? ' ' : current;
                    if (next == '}')
                        sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedString when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '"' && next == '"':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '{' && next == '{':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '{':
                    sanitized[i] = current;
                    interpolationDepth = 1;
                    interpolationCloseBraceCount = 1;
                    interpolationReturnState = state;
                    state = SourceScanState.InterpolationExpression;
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '}':
                    sanitized[i] = next == '}' ? ' ' : current;
                    if (next == '}')
                        sanitized[++i] = ' ';
                    break;
                case SourceScanState.InterpolatedVerbatimString when current == '"':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                case SourceScanState.InterpolationExpression when current == '{':
                    sanitized[i] = current;
                    interpolationDepth++;
                    break;
                case SourceScanState.InterpolationExpression when current == '}' && interpolationDepth > 1:
                    sanitized[i] = current;
                    interpolationDepth--;
                    break;
                case SourceScanState.InterpolationExpression when HasRun(text, i, '}', interpolationCloseBraceCount):
                    for (var j = 0; j < interpolationCloseBraceCount; j++)
                        sanitized[i + j] = '}';
                    i += interpolationCloseBraceCount - 1;
                    interpolationDepth--;
                    if (interpolationDepth == 0)
                    {
                        interpolationCloseBraceCount = 1;
                        state = interpolationReturnState;
                    }
                    break;
                case SourceScanState.InterpolationExpression when current == '}':
                    sanitized[i] = current;
                    interpolationDepth--;
                    if (interpolationDepth == 0)
                        state = interpolationReturnState;
                    break;
                case SourceScanState.InterpolationExpression:
                    sanitized[i] = current;
                    break;
                case SourceScanState.Character when current == '\\' && next != '\0':
                    sanitized[i] = ' ';
                    sanitized[++i] = ' ';
                    break;
                case SourceScanState.Character when current == '\'':
                    sanitized[i] = ' ';
                    state = SourceScanState.Code;
                    break;
                default:
                    sanitized[i] = current is '\r' or '\n' ? current : ' ';
                    break;
            }
        }

        return new string(sanitized);
    }

    private static bool TryReadRawStringStart(string text, int index, out int prefixLength, out int quoteCount, out int dollarCount)
    {
        prefixLength = 0;
        quoteCount = 0;
        dollarCount = 0;

        var quoteIndex = index;
        while (quoteIndex < text.Length && text[quoteIndex] == '$')
            quoteIndex++;
        dollarCount = quoteIndex - index;

        if (quoteIndex == index && text[index] != '"')
            return false;

        quoteCount = CountRun(text, quoteIndex, '"');
        if (quoteCount < 3)
        {
            quoteCount = 0;
            return false;
        }

        prefixLength = quoteIndex - index + quoteCount;
        return true;
    }

    private static bool HasRun(string text, int index, char value, int count) => CountRun(text, index, value) >= count;

    private static int CountRun(string text, int index, char value)
    {
        var count = 0;
        while (index + count < text.Length && text[index + count] == value)
            count++;

        return count;
    }

    private static string ExpectedProjectPath(ProjectInfo project)
    {
        if (project.Name == "Elsa.Server")
            return "src/Apps/Elsa.Server/Elsa.Server.csproj";

        if (project.Name == "Elsa.Architecture.Tests")
            return "tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj";

        if (project.Name == "Elsa.Primitives")
            return "src/Elsa/Primitives/Primitives/Elsa.Primitives.csproj";

        if (project.Name.StartsWith("Elsa3.", StringComparison.Ordinal) && project.RelativePath.StartsWith("tests/", StringComparison.Ordinal))
            return $"tests/Elsa3/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        if (project.Name.StartsWith("Elsa3.", StringComparison.Ordinal))
            return $"src/Elsa3/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        if (project.Name.StartsWith("Elsa.", StringComparison.Ordinal) && project.RelativePath.StartsWith("src/", StringComparison.Ordinal))
            return $"src/Elsa/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        if (project.Name.StartsWith("Elsa.", StringComparison.Ordinal) && project.RelativePath.StartsWith("tests/", StringComparison.Ordinal))
            return $"tests/Elsa/{string.Join('/', project.Name.Split('.')[1..])}/{project.Name}.csproj";

        return project.RelativePath;
    }

    private static string ExpectedSolutionFolder(ProjectInfo project, HashSet<string> projectDirectories)
    {
        if (project.Name == "Elsa.Server")
            return "/src/Apps/";

        var directory = Path.GetDirectoryName(project.RelativePath)!.Replace('\\', '/');
        var lastProjectSegment = project.Name.Split('.')[^1];
        var lastDirectorySegment = directory.Split('/')[^1];
        var hasChildProject = projectDirectories.Any(other =>
            other.Length > directory.Length &&
            other.StartsWith(directory + "/", StringComparison.Ordinal));
        var keepLeafFolder = project.Name is "Elsa.Primitives" or "Elsa.Primitives.Hosting";

        if (!keepLeafFolder && lastDirectorySegment == lastProjectSegment && !hasChildProject)
            directory = Path.GetDirectoryName(directory)!.Replace('\\', '/');

        return $"/{directory}/";
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }

    private sealed record ProjectInfo(string Name, string FullPath, string RelativePath)
    {
        public static ProjectInfo From(string repoRoot, string fullPath)
        {
            var normalizedFullPath = Path.GetFullPath(fullPath);
            return new ProjectInfo(
                Path.GetFileNameWithoutExtension(normalizedFullPath),
                normalizedFullPath,
                Path.GetRelativePath(repoRoot, normalizedFullPath).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }

    private enum SourceScanState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        VerbatimString,
        RawString,
        InterpolatedString,
        InterpolatedVerbatimString,
        InterpolatedRawString,
        InterpolationExpression,
        Character
    }

    private sealed record SolutionProjectInfo(string Folder, string Path);
}
