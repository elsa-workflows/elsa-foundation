using System.Text.Json;

namespace Elsa.Maps.Generator;

/// <summary>
/// Dependency-free contract tests for the solution-filter generator. Keeping these beside the tool
/// lets CI exercise path and graph behavior without introducing another project into the solution.
/// </summary>
public static class SolutionFilterGeneratorContractTests
{
    public static void Run()
    {
        var root = Path.Join(Path.GetTempPath(), $"elsa-solution-filter-tests-{Environment.ProcessId}-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(root);
            WriteFixture(root);
            var repo = RepoContext.Discover(root);

            SolutionFilterGenerator.Generate(repo);
            AssertProjects(root, "Feature.slnf", ["src/Feature/Feature.csproj", "src/Shared/Shared.csproj"]);
            AssertProjects(root, "Integration.slnf", ["tests/Container.csproj"]);
            Assert(SolutionFilterGenerator.GetRoots(repo, "Integration.slnf")
                    .SequenceEqual(["tests/Container.csproj"], StringComparer.Ordinal),
                "Root listing must use parsed PackageReference elements and ignore comment text.");

            var first = File.ReadAllBytes(Path.Join(root, "Feature.slnf"));
            SolutionFilterGenerator.Generate(repo);
            Assert(first.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Join(root, "Feature.slnf"))),
                "Generating the same graph twice must be byte deterministic.");

            WriteManifest(root, [], duplicateOutput: true);
            AssertThrows(
                () => SolutionFilterGenerator.Generate(repo),
                "more than once",
                "Output paths that differ only by casing must be rejected.");
            WriteManifest(root, []);

            var solutionPath = Path.Join(root, "Elsa.Server.slnx");
            var solution = File.ReadAllText(solutionPath);
            File.WriteAllText(solutionPath, solution.Replace(
                "</Solution>",
                "  <Project Path=\"src/feature/feature.csproj\" />\n</Solution>",
                StringComparison.Ordinal));
            AssertThrows(
                () => SolutionFilterGenerator.Generate(repo),
                "more than once",
                "Solution paths that differ only by casing must be rejected.");
            File.WriteAllText(solutionPath, solution);

            File.WriteAllText(Path.Join(root, "src/Feature/Feature.csproj"),
                "<Project><ItemGroup><ProjectReference Include=\"../Missing/Missing.csproj\" /></ItemGroup></Project>");
            AssertThrows(
                () => SolutionFilterGenerator.Generate(repo),
                "references a project outside",
                "A missing ProjectReference must fail closed.");

            Directory.CreateDirectory(Path.Join(root, "tools"));
            File.WriteAllText(Path.Join(root, "tools/External.csproj"), "<Project />");
            File.WriteAllText(Path.Join(root, "src/Feature/Feature.csproj"),
                "<Project><ItemGroup><ProjectReference Include=\"../../tools/External.csproj\" /></ItemGroup></Project>");
            WriteManifest(root, ["tools\\External.csproj"]);
            SolutionFilterGenerator.Generate(repo);
            AssertProjects(root, "Feature.slnf", ["src/Feature/Feature.csproj"]);

            Console.WriteLine("Solution filter generator contract tests passed.");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private static void WriteFixture(string root)
    {
        Directory.CreateDirectory(Path.Join(root, "src/Feature"));
        Directory.CreateDirectory(Path.Join(root, "src/Shared"));
        Directory.CreateDirectory(Path.Join(root, "tests"));
        Directory.CreateDirectory(Path.Join(root, "benchmarks/Feature"));
        Directory.CreateDirectory(Path.Join(root, "tools/solution-filters"));

        File.WriteAllText(Path.Join(root, "Elsa.Server.slnx"),
            """
            <Solution>
              <Project Path="src\Feature\Feature.csproj" />
              <Project Path="src/Shared/Shared.csproj" />
              <Project Path="tests/Comment.csproj" />
              <Project Path="tests/Container.csproj" />
              <Project Path="benchmarks/Feature/Feature.Benchmarks.csproj" />
            </Solution>
            """);
        File.WriteAllText(Path.Join(root, "src/Feature/Feature.csproj"),
            "<Project><ItemGroup><ProjectReference Include=\"..\\Shared\\Shared.csproj\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Join(root, "src/Shared/Shared.csproj"), "<Project />");
        File.WriteAllText(Path.Join(root, "tests/Comment.csproj"),
            "<Project><!-- <PackageReference Include=\"Testcontainers.CommentOnly\" /> --></Project>");
        File.WriteAllText(Path.Join(root, "tests/Container.csproj"),
            "<Project><ItemGroup><PackageReference Include=\"Testcontainers.Real\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Join(root, "benchmarks/Feature/Feature.Benchmarks.csproj"), "<Project />");
        WriteManifest(root, []);
    }

    private static void WriteManifest(
        string root,
        IReadOnlyList<string> allowedExternalReferences,
        bool duplicateOutput = false)
    {
        var profiles = new List<object>
        {
            new
            {
                outputPath = "Feature.slnf",
                includeProjectPathContains = new[] { "Feature" },
                requiredRootPaths = new[] { "src\\Feature\\Feature.csproj" }
            },
            new
            {
                outputPath = "Integration.slnf",
                includeProjectPathPrefixes = new[] { "tests\\" },
                requirePackageReferencePrefixes = new[] { "Testcontainers." }
            }
        };
        if (duplicateOutput)
            profiles.Add(new { outputPath = "feature.slnf", includeProjectNames = new[] { "Feature" } });

        var manifest = new
        {
            solutionPath = "Elsa.Server.slnx",
            excludeRootPathPrefixes = new[] { "benchmarks\\" },
            allowedExternalProjectReferences = allowedExternalReferences,
            profiles
        };

        File.WriteAllText(
            Path.Join(root, "tools/solution-filters/profiles.json"),
            JsonSerializer.Serialize(manifest));
    }

    private static void AssertProjects(string root, string filter, IReadOnlyList<string> expected)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Join(root, filter)));
        var actual = document.RootElement.GetProperty("solution").GetProperty("projects")
            .EnumerateArray()
            .Select(element => element.GetString()!)
            .ToArray();
        Assert(actual.SequenceEqual(expected, StringComparer.Ordinal),
            $"{filter} projects differed. Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    private static void AssertThrows(Action action, string expectedMessage, string failureMessage)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(failureMessage);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
