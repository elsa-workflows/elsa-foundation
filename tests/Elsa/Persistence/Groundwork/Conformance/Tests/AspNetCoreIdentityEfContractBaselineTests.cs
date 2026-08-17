using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class AspNetCoreIdentityEfContractBaselineTests
{
    [Fact]
    public void Ef_oracle_remains_source_only_and_explicitly_frozen()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Elsa",
            "Foundation",
            "Identity",
            "AspNetCoreIdentity",
            "EntityFrameworkCore",
            "Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.csproj");
        var ledgerPath = Path.Combine(
            repositoryRoot,
            "specs",
            "095-groundwork-aspnetcore-identity",
            "contracts",
            "test-objective-ledger.md");

        Assert.True(File.Exists(projectPath), $"The frozen EF oracle project was not found at '{projectPath}'.");
        Assert.True(File.Exists(ledgerPath), $"The Identity objective ledger was not found at '{ledgerPath}'.");
        Assert.Contains(
            "The EF project remains a source-only frozen temporary oracle",
            File.ReadAllText(ledgerPath),
            StringComparison.Ordinal);

        var referencedAssemblies = typeof(AspNetCoreIdentityEfContractBaselineTests)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore", referencedAssemblies);
    }

    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return current.FullName;

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
