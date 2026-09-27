using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Elsa.Versioning.Calculator.Tests.Support;

/// <summary>A project in a <see cref="SyntheticRepository"/>'s model.</summary>
internal sealed class SyntheticProject
{
    /// <summary>Repository-relative path of the <c>.csproj</c>.</summary>
    public required string Path { get; set; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The package id; the project name unless set.</summary>
    public string? PackageId { get; set; }

    public string Line { get; set; } = "B";

    public bool Packable { get; set; } = true;

    /// <summary>Extra XML placed in the project file's body.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Referenced projects, by <c>.csproj</c> path.</summary>
    public List<string> References { get; } = [];

    /// <summary>Referenced external packages, by id.</summary>
    public List<string> Packages { get; } = [];

    public string Directory => Path[..(Path.LastIndexOf('/') + 1)];

    public string Identity => PackageId ?? Name;
}

/// <summary>
/// A throwaway git repository shaped like this one, for building synthetic histories: projects under <c>src/</c> and
/// <c>tests/</c>, a dependency map committed beside them, and the root build files the calculator reads.
/// </summary>
/// <remarks>
/// The model stands in for the maps generator: each <see cref="Commit"/> writes every project file,
/// <c>VersionLines.props</c>, <c>Directory.Packages.props</c> and <c>docs/maps/dependency-map.json</c> from it, so the
/// committed map always describes the committed tree unless a test deliberately makes it lie.
/// </remarks>
internal sealed class SyntheticRepository : IDisposable
{
    private static readonly JsonSerializerOptions MapJson = new() { WriteIndented = true };

    public SyntheticRepository()
    {
        Root = System.IO.Directory.CreateTempSubdirectory("elsa-versioning-").FullName;
        Git("init", "--quiet", "--initial-branch=main");
        Calculator = new GitRepository(Root);

        Write("Directory.Build.props",
            """
            <Project>
              <Import Project="$(MSBuildThisFileDirectory)VersionLines.props" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        Write("NuGet.config",
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
    }

    public string Root { get; }

    /// <summary>The repository as the calculator reads it.</summary>
    public GitRepository Calculator { get; }

    public List<SyntheticProject> Projects { get; } = [];

    /// <summary>Central package versions, by id, written to <c>Directory.Packages.props</c>.</summary>
    public SortedDictionary<string, string> PackageVersions { get; } = new(StringComparer.Ordinal);

    public string CentralPackagesComment { get; set; } = "Versions every project shares.";

    public bool TransitivePinning { get; set; } = true;

    public string ElsaVersion { get; set; } = "4.0";

    public string ElsaContractsVersion { get; set; } = "4.0";

    public int MapSchemaVersion { get; set; } = 1;

    /// <summary>When false, <see cref="Commit"/> leaves the dependency map as it was, as a stale map would be.</summary>
    public bool RegenerateMap { get; set; } = true;

    public SyntheticProject Project(string packageIdOrName) =>
        Projects.Single(project => project.Identity == packageIdOrName || project.Name == packageIdOrName);

    public SyntheticProject AddProject(string path, string line = "B", string? packageId = null, bool packable = true, string[]? references = null, string[]? packages = null, string body = "")
    {
        var project = new SyntheticProject { Path = path, Line = line, PackageId = packageId, Packable = packable, Body = body };
        project.References.AddRange(references ?? []);
        project.Packages.AddRange(packages ?? []);
        Projects.Add(project);
        return project;
    }

    public void Write(string path, string content)
    {
        var file = Absolute(path);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content.ReplaceLineEndings("\n"));
    }

    public string Read(string path) => File.ReadAllText(Absolute(path));

    /// <summary>Deletes a file, or a directory and everything in it, along with any project under it.</summary>
    public void Delete(string path)
    {
        if (File.Exists(Absolute(path)))
            File.Delete(Absolute(path));
        else
            System.IO.Directory.Delete(Absolute(path), recursive: true);
        Projects.RemoveAll(project => project.Path == path || project.Path.StartsWith(path + "/", StringComparison.Ordinal));
    }

    /// <summary>Moves a directory and every project under it, rewriting references to them as a real move would.</summary>
    public void MoveDirectory(string from, string to)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Absolute(to))!);
        System.IO.Directory.Move(Absolute(from), Absolute(to));

        string Moved(string path) => path.StartsWith(from + "/", StringComparison.Ordinal) ? to + path[from.Length..] : path;
        foreach (var project in Projects)
        {
            project.Path = Moved(project.Path);
            for (var index = 0; index < project.References.Count; index++)
                project.References[index] = Moved(project.References[index]);
        }
    }

    /// <summary>Writes the model's files, commits everything, and returns the new commit's id.</summary>
    public string Commit(string message = "change")
    {
        foreach (var project in Projects)
            Write(project.Path, ProjectFile(project));
        Write("VersionLines.props", VersionLines());
        Write("Directory.Packages.props", CentralPackages());
        if (RegenerateMap)
            Write("docs/maps/dependency-map.json", DependencyMap());

        return CommitAsIs(message);
    }

    /// <summary>Commits the working tree exactly as it is, hand edits to generated files included.</summary>
    public string CommitAsIs(string message = "change")
    {
        Git("add", "--all");
        Git("commit", "--quiet", "--allow-empty", "-m", message);
        return Head;
    }

    public string Head => Git("rev-parse", "HEAD");

    /// <summary>
    /// Stores a record revision on the <c>publish-state</c> branch using plumbing only, as a write-back would: neither
    /// <c>main</c> nor the working tree moves.
    /// </summary>
    public string WriteBack(PublishedVersions record)
    {
        var blob = Run(record.Serialize(), ["hash-object", "-w", "--stdin"]);
        var tree = Run($"100644 blob {blob}\t{PublishedVersions.DefaultPath}\n", ["mktree"]);
        var parent = Run(null, ["rev-parse", "--verify", "--quiet", "refs/heads/publish-state^{commit}"], allowFailure: true);
        var commit = Git(["commit-tree", tree, .. parent.Length > 0 ? ["-p", parent] : Array.Empty<string>(), "-m", "Record publish"]);
        Git("update-ref", "refs/heads/publish-state", commit);
        return commit;
    }

    public string Git(params string[] arguments) => Run(null, arguments);

    public void Dispose() => System.IO.Directory.Delete(Root, recursive: true);

    private string Run(string? input, string[] arguments, bool allowFailure = false)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // Isolated from the machine's git configuration: no signing, no hooks, a fixed identity.
        foreach (var argument in (string[])["-C", Root, "-c", "commit.gpgsign=false", "-c", "core.hooksPath=.git/no-hooks", "-c", "core.autocrlf=false",
                     "-c", "user.name=Synthetic History", "-c", "user.email=synthetic@example.invalid", .. arguments])
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)!;
        process.StandardInput.Write(input ?? string.Empty);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error.Result}");

        return output.Result.Trim();
    }

    private string Absolute(string path) => System.IO.Path.Join(Root, path);

    private string ProjectFile(SyntheticProject project)
    {
        var properties = new StringBuilder();
        if (project.PackageId is not null)
            properties.Append($"    <PackageId>{project.PackageId}</PackageId>\n");
        if (!project.Packable)
            properties.Append("    <IsPackable>false</IsPackable>\n");

        var items = new StringBuilder();
        foreach (var reference in project.References)
            items.Append($"    <ProjectReference Include=\"{System.IO.Path.GetRelativePath("/" + project.Directory, "/" + reference).Replace('/', '\\')}\" />\n");
        foreach (var package in project.Packages)
            items.Append($"    <PackageReference Include=\"{package}\" />\n");

        return $"""
                <Project Sdk="Microsoft.NET.Sdk">

                  <PropertyGroup>
                {properties}  </PropertyGroup>
                {project.Body}
                  <ItemGroup>
                {items}  </ItemGroup>

                </Project>

                """;
    }

    private string VersionLines()
    {
        var members = string.Concat(Projects.Where(project => project.Packable && project.Line == "A").Select(project => project.Name + ";"));
        return $"""
                <Project>
                  <PropertyGroup>
                    <ElsaVersionLineAMembers>;{members}</ElsaVersionLineAMembers>
                  </PropertyGroup>
                  <PropertyGroup>
                    <ElsaVersion>{ElsaVersion}</ElsaVersion>
                    <ElsaContractsVersion>{ElsaContractsVersion}</ElsaContractsVersion>
                  </PropertyGroup>
                </Project>

                """;
    }

    private string CentralPackages()
    {
        var entries = string.Concat(PackageVersions.Select(entry => $"    <PackageVersion Include=\"{entry.Key}\" Version=\"{entry.Value}\" />\n"));
        return $"""
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                    <CentralPackageTransitivePinningEnabled>{(TransitivePinning ? "true" : "false")}</CentralPackageTransitivePinningEnabled>
                  </PropertyGroup>
                  <ItemGroup>
                    <!-- {CentralPackagesComment} -->
                {entries}  </ItemGroup>
                </Project>

                """;
    }

    private string DependencyMap()
    {
        var byPath = Projects.ToDictionary(project => project.Path);
        var nodes = Projects.OrderBy(project => project.Path, StringComparer.Ordinal).Select(project => new Dictionary<string, object?>
        {
            ["path"] = project.Path,
            ["name"] = project.Name,
            ["kind"] = project.Path.StartsWith("tests/", StringComparison.Ordinal) ? "test" : "source",
            ["packable"] = project.Packable,
            ["package_id"] = project.Packable ? project.Identity : null,
            ["line"] = project.Packable ? project.Line : null,
            ["edges"] = project.References.Select(reference => (object)new Dictionary<string, object?>
                {
                    ["type"] = "internal",
                    ["id"] = byPath[reference].Identity,
                    ["path"] = reference
                })
                .Concat(project.Packages.Select(package => new Dictionary<string, object?>
                {
                    ["type"] = "external",
                    ["id"] = package,
                    ["version"] = PackageVersions.GetValueOrDefault(package)
                }))
                .ToArray()
        });

        return JsonSerializer.Serialize(new Dictionary<string, object> { ["schema_version"] = MapSchemaVersion, ["generator"] = "synthetic", ["nodes"] = nodes }, MapJson) + "\n";
    }
}
