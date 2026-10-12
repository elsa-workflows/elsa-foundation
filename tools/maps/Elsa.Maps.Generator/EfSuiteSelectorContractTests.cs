using System.Text.Json;

namespace Elsa.Maps.Generator;

/// <summary>Inventory cases for the nightly EF container matrix; CI runs them before trusting the matrix.</summary>
public static class EfSuiteSelectorContractTests
{
    /// <summary>Checks inventory rejection cases and verifies that the real manifest's suites and requirements reach the matrix.</summary>
    public static void Run()
    {
        EfSuite[] suites =
        [
            new("publishing", "tests/Publishing/Publishing.Tests.csproj", ""),
            new("runtime", "tests/Runtime/Runtime.Tests.csproj", "ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX")
        ];
        string[] solutionProjects = ["src/Shared/Shared.csproj", .. suites.Select(suite => suite.Project)];
        const string separateSecrets = "Essentials/tests/Secrets/Persistence/EntityFrameworkCore/PostgreSql/Tests/Elsa.Secrets.Persistence.EntityFrameworkCore.PostgreSql.Tests.csproj";
        var roots = suites.Select(suite => suite.Project).Append(separateSecrets).ToArray();
        var testProjects = suites.Select(suite => suite.Project).ToArray();

        EfSuiteSelector.ValidateCoverage(suites, roots, testProjects, solutionProjects);
        // A Testcontainers root the manifest does not name would run in neither the fast lane nor the matrix.
        ExpectInvalid(() => EfSuiteSelector.ValidateCoverage(suites,
            roots.Append("tests/New/New.Tests.csproj").ToArray(),
            testProjects.Append("tests/New/New.Tests.csproj").ToArray(),
            solutionProjects.Append("tests/New/New.Tests.csproj").ToArray()));
        // A manifest entry that is no longer a Testcontainers root is obsolete.
        ExpectInvalid(() => EfSuiteSelector.ValidateCoverage(suites, roots[1..], testProjects, solutionProjects));
        // A test project outside the solution is invisible to the root scan.
        ExpectInvalid(() => EfSuiteSelector.ValidateCoverage(suites, roots,
            testProjects.Append("tests/New/New.Tests.csproj").ToArray(), solutionProjects));
        // The Secrets PostgreSQL host proof runs in its own job and must not also be a matrix leg.
        ExpectInvalid(() => EfSuiteSelector.ValidateCoverage(
            [.. suites, new EfSuite("secrets-postgresql", separateSecrets, "")], roots, testProjects, solutionProjects));

        // The manifest is the inventory: every suite in it becomes a leg, with its required-provider mode, and there is
        // no count to keep in step here.
        var repo = RepoContext.Discover();
        var manifest = JsonDocument.Parse(File.ReadAllText(repo.Absolute("tools/ci/ef-suites.json")))
            .RootElement.GetProperty("suites").EnumerateArray()
            .ToDictionary(suite => suite.GetProperty("suite").GetString()!, suite => suite.GetProperty("require").GetString()!);
        var include = JsonDocument.Parse(EfSuiteSelector.Select(repo).ToJson()).RootElement
            .GetProperty("matrix").GetProperty("include").EnumerateArray()
            .ToDictionary(leg => leg.GetProperty("suite").GetString()!, leg => leg.GetProperty("require").GetString()!);
        if (include.Count != manifest.Count || manifest.Any(suite => !include.TryGetValue(suite.Key, out var require) || require != suite.Value))
            throw new InvalidOperationException(
                $"The EF matrix must carry all {manifest.Count} manifest suites with their required-provider modes; got {include.Count}.");
        Console.WriteLine("EF suite selector contract cases passed.");
    }

    /// <summary>Fails the contract test unless the supplied action throws an InvalidOperationException.</summary>
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
