using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class AspNetCoreIdentityMutationGuardTests
{
    [Fact]
    public void Current_provider_neutral_and_groundwork_identity_tree_has_no_forbidden_mutations()
    {
        var diagnostics = AspNetCoreIdentityMutationScanner.Scan(RepoRoot);

        Assert.True(
            diagnostics.Count == 0,
            string.Join(Environment.NewLine, diagnostics.Select(Format)));
    }

    [Theory]
    [MemberData(nameof(MutationCases))]
    public void Intentional_mutation_reports_its_exact_path_and_line(
        string relativePath,
        string source,
        string expectedCode,
        int expectedLine)
    {
        using var fixture = new TemporaryDirectory();
        fixture.Write(relativePath, source);

        var diagnostic = Assert.Single(AspNetCoreIdentityMutationScanner.Scan(fixture.Path));

        Assert.Equal(expectedCode, diagnostic.Code);
        Assert.Equal(relativePath, diagnostic.Path);
        Assert.Equal(expectedLine, diagnostic.Line);
    }

    [Fact]
    public void Reviewed_bounded_cursor_pager_is_the_only_QueryAllAsync_surface_allowed()
    {
        using var fixture = new TemporaryDirectory();
        fixture.Write(
            "src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/ReviewedPager.cs",
            """
            namespace Injected;

            public sealed class ReviewedPager
            {
                public Task LoadAsync() =>
                    BoundedDocumentQueryPager.QueryAllAsync();
            }
            """);
        fixture.Write(
            "src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/UnboundedPager.cs",
            """
            namespace Injected;

            public sealed class UnboundedPager
            {
                public Task LoadAsync() =>
                    QueryAllAsync();
            }
            """);
        fixture.Write(
            "src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/MixedPager.cs",
            """
            namespace Injected;

            public sealed class MixedPager
            {
                public async Task LoadAsync()
                {
                    await BoundedDocumentQueryPager.QueryAllAsync(); await store.QueryAllAsync();
                }
            }
            """);

        var diagnostics = AspNetCoreIdentityMutationScanner.Scan(fixture.Path);

        Assert.Collection(
            diagnostics,
            diagnostic =>
            {
                Assert.Equal("IDENTITY-UNBOUNDED-QUERY", diagnostic.Code);
                Assert.Equal(
                    "src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/MixedPager.cs",
                    diagnostic.Path);
                Assert.Equal(7, diagnostic.Line);
            },
            diagnostic =>
            {
                Assert.Equal("IDENTITY-UNBOUNDED-QUERY", diagnostic.Code);
                Assert.Equal(
                    "src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/UnboundedPager.cs",
                    diagnostic.Path);
                Assert.Equal(6, diagnostic.Line);
            });
    }

    public static TheoryData<string, string, string, int> MutationCases => new()
    {
        {
            "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Injected.csproj",
            """
            <Project>
              <ItemGroup>
                <ProjectReference Include="../EntityFrameworkCore/Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.csproj" />
              </ItemGroup>
            </Project>
            """,
            "IDENTITY-EF-REFERENCE",
            3
        },
        {
            "src/Elsa/Foundation/Identity/Persistence/Groundwork/Documents/DuplicateIdentityUserDocument.cs",
            """
            namespace Injected;

            public sealed record IdentityUserDocument(string Id);
            """,
            "IDENTITY-DUPLICATE-AUTHORITY-DOCUMENT",
            3
        },
        {
            "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/Stores/LoadAllUsers.cs",
            """
            namespace Injected;

            public sealed class LoadAllUsers
            {
                public Task LoadAllAsync() => Task.CompletedTask;
            }
            """,
            "IDENTITY-UNBOUNDED-QUERY",
            5
        },
        {
            "src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/OffsetIdentityQuery.cs",
            """
            namespace Injected;

            public sealed class OffsetIdentityQuery
            {
                public object Create() => new QueryRequest(null!, null!, [], null!, Paging.OffsetLimit(1, 1));
            }
            """,
            "IDENTITY-OFFSET-QUERY",
            5
        },
        {
            "src/Elsa/Foundation/Identity/Persistence/Groundwork/Stores/PositionalOffsetIdentityQuery.cs",
            """
            namespace Injected;

            public sealed class PositionalOffsetIdentityQuery
            {
                public object Create() => new DocumentQuery("kind", "route", null, 1, 1);
            }
            """,
            "IDENTITY-OFFSET-QUERY",
            5
        },
        {
            "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/DependencyInjection/UnsupportedCapability.cs",
            """
            namespace Injected;

            public static class UnsupportedCapability
            {
                public static void Register(IServiceCollection services) =>
                    services.AddScoped<IQueryableUserStore<AspNetCoreIdentityUser>, Store>();
            }
            """,
            "IDENTITY-UNSUPPORTED-CAPABILITY",
            6
        },
        {
            "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/DependencyInjection/SecondAuthority.cs",
            """
            namespace Injected;

            public static class SecondAuthority
            {
                public static void Register(IServiceCollection services) =>
                    services.AddScoped<IUserStore<AspNetCoreIdentityUser>, OtherStore>();
            }
            """,
            "IDENTITY-DUPLICATE-AUTHORITY-REGISTRATION",
            6
        }
    };

    private static string Format(AspNetCoreIdentityMutationDiagnostic diagnostic) =>
        $"{diagnostic.Code}: {diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}";

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() => Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"elsa-identity-mutation-{Guid.NewGuid():N}");

        public string Path { get; }

        public void Write(string relativePath, string content)
        {
            var fullPath = System.IO.Path.Combine(
                Path,
                relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed record AspNetCoreIdentityMutationDiagnostic(
    string Code,
    string Path,
    int Line,
    string Message);

internal static partial class AspNetCoreIdentityMutationScanner
{
    private static readonly string[] IdentityRoots =
    [
        "src/Elsa/Foundation/Identity/AspNetCoreIdentity/",
        "src/Elsa/Foundation/Identity/Persistence/Groundwork/"
    ];
    private const string CanonicalDocumentsPath =
        "src/Elsa/Foundation/Identity/Persistence/Groundwork/Documents/IdentityAuthorityDocuments.cs";
    private const string CanonicalRegistrationPath =
        "src/Elsa/Foundation/Identity/AspNetCoreIdentity/Groundwork/DependencyInjection/AspNetCoreIdentityGroundworkRegistration.cs";

    private static readonly string[] UnsupportedCapabilities =
    [
        "IQueryableUserStore<",
        "IQueryableRoleStore<",
        "IUserPasskeyStore<",
        "IProtectedUserStore<"
    ];

    public static IReadOnlyList<AspNetCoreIdentityMutationDiagnostic> Scan(string repositoryRoot)
    {
        var sourceRoot = Path.Combine(repositoryRoot, "src", "Elsa", "Foundation", "Identity");
        if (!Directory.Exists(sourceRoot))
            return [];

        var diagnostics = new List<AspNetCoreIdentityMutationDiagnostic>();
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (!IdentityRoots.Any(root => relativePath.StartsWith(root, StringComparison.Ordinal)) ||
                relativePath.Contains("/EntityFrameworkCore/", StringComparison.Ordinal))
                continue;

            ScanFile(relativePath, File.ReadAllLines(file), diagnostics);
        }

        return diagnostics
            .OrderBy(diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Line)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ScanFile(
        string relativePath,
        IReadOnlyList<string> lines,
        ICollection<AspNetCoreIdentityMutationDiagnostic> diagnostics)
    {
        if (!StringComparer.Ordinal.Equals(relativePath, CanonicalRegistrationPath))
        {
            var source = string.Join('\n', lines.Select(StripLineComment));
            foreach (Match match in DuplicateAuthorityRegistrationRegex().Matches(source))
            {
                Add(diagnostics, "IDENTITY-DUPLICATE-AUTHORITY-REGISTRATION", relativePath,
                    1 + source[..match.Index].Count(character => character == '\n'),
                    "ASP.NET Core Identity user/role authority registration is owned by the canonical Groundwork registration.");
            }
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index];
            var code = StripLineComment(line);

            if (relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) &&
                ProjectReferenceRegex().IsMatch(code))
            {
                Add(diagnostics, "IDENTITY-EF-REFERENCE", relativePath, lineNumber,
                    "Provider-neutral or Groundwork Identity introduced an EF project/package reference.");
            }
            else if (relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                     EfUsingRegex().IsMatch(code))
            {
                Add(diagnostics, "IDENTITY-EF-REFERENCE", relativePath, lineNumber,
                    "Provider-neutral or Groundwork Identity introduced an EF source dependency.");
            }

            if (!StringComparer.Ordinal.Equals(relativePath, CanonicalDocumentsPath) &&
                AuthorityDocumentRegex().IsMatch(code))
            {
                Add(diagnostics, "IDENTITY-DUPLICATE-AUTHORITY-DOCUMENT", relativePath, lineNumber,
                    "Identity user/role authority documents have exactly one canonical definition.");
            }

            var codeWithoutReviewedPager = ReviewedBoundedPagerRegex().Replace(code, string.Empty);
            if (relativePath.Contains("/Groundwork/", StringComparison.Ordinal) &&
                UnboundedQueryRegex().IsMatch(codeWithoutReviewedPager))
            {
                Add(diagnostics, "IDENTITY-UNBOUNDED-QUERY", relativePath, lineNumber,
                    "Groundwork Identity may use only declared, finite bounded-query routes.");
            }

            if (relativePath.Contains("/Groundwork/", StringComparison.Ordinal) &&
                OffsetQueryRegex().IsMatch(code))
            {
                Add(diagnostics, "IDENTITY-OFFSET-QUERY", relativePath, lineNumber,
                    "Groundwork Identity scale-bearing routes must use finite cursor paging rather than offsets.");
            }

            if (relativePath.Contains("/Groundwork/", StringComparison.Ordinal) &&
                UnsupportedCapabilities.Any(capability => code.Contains(capability, StringComparison.Ordinal)))
            {
                Add(diagnostics, "IDENTITY-UNSUPPORTED-CAPABILITY", relativePath, lineNumber,
                    "Groundwork Identity registered an explicitly unsupported optional framework capability.");
            }

        }
    }

    private static string StripLineComment(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    private static void Add(
        ICollection<AspNetCoreIdentityMutationDiagnostic> diagnostics,
        string code,
        string path,
        int line,
        string message) =>
        diagnostics.Add(new AspNetCoreIdentityMutationDiagnostic(code, path, line, message));

    [GeneratedRegex(@"\busing\s+(?:global::)?Microsoft\.EntityFrameworkCore\b", RegexOptions.CultureInvariant)]
    private static partial Regex EfUsingRegex();

    [GeneratedRegex(@"<(?:PackageReference|ProjectReference)\b[^>]*EntityFrameworkCore", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectReferenceRegex();

    [GeneratedRegex(@"\b(?:record|class)\s+(?:IdentityUserDocument|IdentityRoleDocument)\b", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorityDocumentRegex();

    [GeneratedRegex(@"\b(?:LoadAllAsync|QueryAllAsync)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex UnboundedQueryRegex();

    [GeneratedRegex(@"\bBoundedDocumentQueryPager\.QueryAllAsync\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex ReviewedBoundedPagerRegex();

    [GeneratedRegex(@"\b(?:Paging\.OffsetLimit|for\s*\(\s*var\s+skip\b)|\bnew\s+DocumentQuery\s*\([^)]*,\s*[^,]+,\s*[^,]+,\s*\d+\s*,\s*\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OffsetQueryRegex();

    [GeneratedRegex(@"\b(?:Add|TryAdd|Replace)Scoped\s*<\s*(?:IUserStore\s*<\s*AspNetCoreIdentityUser\s*>|IRoleStore\s*<\s*IdentityRole\s*>)", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateAuthorityRegistrationRegex();
}
