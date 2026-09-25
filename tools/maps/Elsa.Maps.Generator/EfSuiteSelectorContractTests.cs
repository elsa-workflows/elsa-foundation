namespace Elsa.Maps.Generator;

/// <summary>Small deterministic impact cases; CI runs them before trusting a PR selection.</summary>
public static class EfSuiteSelectorContractTests
{
    public static void Run()
    {
        var projects = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/Shared/Shared.csproj"] = [],
            ["src/Publishing/Publishing.csproj"] = ["src/Shared/Shared.csproj"],
            ["src/Runtime/Runtime.csproj"] = ["src/Shared/Shared.csproj"],
            ["src/Unrelated/Unrelated.csproj"] = [],
            ["tests/Publishing/Publishing.Tests.csproj"] = ["src/Publishing/Publishing.csproj"],
            ["tests/Runtime/Helper/Helper.csproj"] = ["src/Runtime/Runtime.csproj"],
            ["tests/Runtime/Runtime.Tests.csproj"] = ["tests/Runtime/Helper/Helper.csproj"]
        };
        EfSuite[] suites =
        [
            new("publishing", "tests/Publishing/Publishing.Tests.csproj", ""),
            new("runtime", "tests/Runtime/Runtime.Tests.csproj", "ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX")
        ];

        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["docs/report.md"]), "none");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["specs/123-idea/spec.md"]), "none");
        Assert(EfSuiteSelector.SelectPaths(suites, projects,
            [".specify/feature.json", "specs/176-composition-file-bridge/spec.md", "docs/maps/spec-status-map.md"]), "none");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, [".specify/other.json"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, [".specify/scripts/bash/common.sh"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/Publishing/Publish.cs"]), "selected", "publishing");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["tests/Publishing/Case.cs"]), "selected", "publishing");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["tests/Publishing/Publishing.Tests.csproj"]), "selected", "publishing");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/Shared/State.cs"]), "selected", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/Runtime/Run.cs"]), "selected", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["tests/Runtime/Helper/Case.cs"]), "selected", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/Unrelated/Other.cs"]), "none");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/New/Other.cs"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/New/New.csproj"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["Directory.Packages.props"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, [".github/workflows/ci.yml"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["tools/ci/ef-suites.json"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/essentials/Persistence/EntityFrameworkCore/Shared.cs"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/essentials/Modularity/Core/FeatureCatalog.cs"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/essentials/Modularity/Api/ModularityApi.cs"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/essentials/Modularity/EntityFramework/ActivationGuard.cs"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/essentials/Modularity/Nuplane/FeatureManagementService.cs"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["src/essentials/Modularity/Unknown/Unowned.cs"]), "full", "publishing", "runtime");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["docs/report.md", "src/Publishing/Publish.cs"]), "selected", "publishing");
        Assert(EfSuiteSelector.SelectPaths(suites, projects, ["tests/Shared/Guard.cs"],
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["tests/Shared/Guard.cs"] = ["tests/Publishing/Publishing.Tests.csproj"]
            }), "selected", "publishing");

        const string separateSecrets = "tests/essentials/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj";
        var roots = suites.Select(suite => suite.Project).Append(separateSecrets).ToArray();
        EfSuiteSelector.ValidateCoverage(suites, roots, suites.Select(suite => suite.Project).ToArray(), projects.Keys.ToArray());
        ExpectInvalid(() => EfSuiteSelector.ValidateCoverage(suites,
            roots.Append("tests/New/New.Tests.csproj").ToArray(),
            suites.Select(suite => suite.Project).Append("tests/New/New.Tests.csproj").ToArray(),
            projects.Keys.Append("tests/New/New.Tests.csproj").ToArray()));
        ExpectInvalid(() => EfSuiteSelector.ValidateCoverage(suites, roots,
            suites.Select(suite => suite.Project).Append("tests/New/New.Tests.csproj").ToArray(),
            projects.Keys.ToArray()));

        var repo = RepoContext.Discover();
        var full = EfSuiteSelector.Select(repo, "workflow_dispatch", null, null);
        if (full.Mode != "full" || full.Suites.Count != 18)
            throw new InvalidOperationException("Manual dispatch must select all 18 current EF suites.");
        if (EfSuiteSelector.Select(repo, "push", null, null).Suites.Count != 18 ||
            EfSuiteSelector.Select(repo, "pull_request", "missing", "missing").Mode != "full")
            throw new InvalidOperationException("Main and an unavailable PR diff must fail closed to the full matrix.");

        var actualProjects = SolutionFilterGenerator.GetProjectReferences(repo);
        var cliAcceptance = new EfSuite("cli-acceptance",
            "tests/essentials/Persistence/EntityFrameworkCore/CliAcceptance/ProviderTests/Elsa.Persistence.EntityFrameworkCore.CliAcceptance.ProviderTests.csproj", "");
        Assert(EfSuiteSelector.SelectPaths([cliAcceptance], actualProjects,
            ["src/essentials/Modularity/Planning/Bridge/CompositionImporter.cs"]), "selected", "cli-acceptance");
        Assert(EfSuiteSelector.SelectPaths([cliAcceptance], actualProjects,
            ["src/essentials/Modularity/Planning/Elsa.Modularity.Planning.csproj"]), "selected", "cli-acceptance");
        Assert(EfSuiteSelector.SelectPaths([cliAcceptance], actualProjects,
            ["tests/essentials/Modularity/Planning/Tests/CompositionImportTests.cs"]), "none");
        Assert(EfSuiteSelector.SelectPaths([cliAcceptance], actualProjects,
            ["src/essentials/Cli/Program.cs"]), "selected", "cli-acceptance");
        Console.WriteLine("EF suite selector contract cases passed.");
    }

    private static void Assert(EfSuiteSelection selection, string mode, params string[] expectedSuites)
    {
        var actual = selection.Suites.Select(suite => suite.Suite).Order(StringComparer.Ordinal).ToArray();
        var expected = expectedSuites.Order(StringComparer.Ordinal).ToArray();
        if (selection.Mode != mode || !actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Expected {mode}: {string.Join(", ", expected)}; got {selection.Mode}: {string.Join(", ", actual)}.");
    }

    private static void ExpectInvalid(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Expected the EF suite inventory to fail closed.");
    }
}
