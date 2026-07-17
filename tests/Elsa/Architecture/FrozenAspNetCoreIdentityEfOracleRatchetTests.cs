using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class FrozenAspNetCoreIdentityEfOracleRatchetTests
{
    private const string OracleRoot = "src/Elsa/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore";

    [Fact]
    public void Frozen_identity_ef_oracle_matches_its_reviewed_content_fingerprint()
    {
        var actual = FrozenEfOracleFingerprint.Capture(RepoRoot, OracleRoot);
        var expected = JsonSerializer.Deserialize<FrozenEfOracleSnapshot>(
                           File.ReadAllText(BaselinePath),
                           JsonOptions)
                       ?? throw new InvalidOperationException($"Frozen EF oracle baseline '{BaselinePath}' is empty.");

        var matches = expected.SchemaVersion == actual.SchemaVersion &&
                      string.Equals(expected.Root, actual.Root, StringComparison.Ordinal) &&
                      string.Equals(expected.TreeSha256, actual.TreeSha256, StringComparison.Ordinal) &&
                      expected.Files.SequenceEqual(actual.Files);

        Assert.True(matches,
            "The frozen ASP.NET Core Identity EF oracle changed. No source, project, migration, registration, " +
            "or test-objective change is permitted before the approved removal work unit. " +
            "Actual deterministic fingerprint:" + Environment.NewLine +
            JsonSerializer.Serialize(actual, JsonOptions));
    }

    [Fact]
    public void Fingerprint_detects_added_and_modified_files_deterministically()
    {
        using var fixture = new TemporaryDirectory();
        fixture.Write("Oracle/A.cs", "public sealed class A;\n");
        var original = FrozenEfOracleFingerprint.Capture(fixture.Path, "Oracle");

        fixture.Write("Oracle/A.cs", "public sealed class Changed;\n");
        fixture.Write("Oracle/B.cs", "public sealed class B;\n");
        var changed = FrozenEfOracleFingerprint.Capture(fixture.Path, "Oracle");

        Assert.NotEqual(original.TreeSha256, changed.TreeSha256);
        Assert.Equal(["A.cs", "B.cs"], changed.Files.Select(file => file.Path));
    }

    [Fact]
    public void Fingerprint_normalizes_line_endings_for_cross_platform_checkouts()
    {
        using var fixture = new TemporaryDirectory();
        fixture.Write("Oracle/A.cs", "line one\r\nline two\r\n");
        var windows = FrozenEfOracleFingerprint.Capture(fixture.Path, "Oracle");

        fixture.Write("Oracle/A.cs", "line one\nline two\n");
        var unix = FrozenEfOracleFingerprint.Capture(fixture.Path, "Oracle");

        Assert.Equal(windows.TreeSha256, unix.TreeSha256);
        Assert.True(windows.Files.SequenceEqual(unix.Files));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string BaselinePath => Path.Combine(
        RepoRoot,
        "tests",
        "Elsa",
        "Architecture",
        "Baselines",
        "frozen-aspnetcore-identity-ef-oracle.json");

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
            $"elsa-frozen-identity-ef-{Guid.NewGuid():N}");

        public string Path { get; }

        public void Write(string relativePath, string content)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
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

internal sealed record FrozenEfOracleFileFingerprint(string Path, string Sha256);

internal sealed record FrozenEfOracleSnapshot(
    int SchemaVersion,
    string Root,
    string TreeSha256,
    IReadOnlyList<FrozenEfOracleFileFingerprint> Files);

internal static class FrozenEfOracleFingerprint
{
    public static FrozenEfOracleSnapshot Capture(string repositoryRoot, string oracleRoot)
    {
        var fullRoot = System.IO.Path.Combine(
            repositoryRoot,
            oracleRoot.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var files = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(path))
            .Select(path => new FrozenEfOracleFileFingerprint(
                System.IO.Path.GetRelativePath(fullRoot, path).Replace(System.IO.Path.DirectorySeparatorChar, '/'),
                Hash(NormalizeText(File.ReadAllText(path)))))
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        var canonicalTree = string.Join(
            '\n',
            files.Select(file => $"{file.Path}\0{file.Sha256}"));

        return new FrozenEfOracleSnapshot(
            1,
            oracleRoot.Replace(System.IO.Path.DirectorySeparatorChar, '/'),
            Hash(canonicalTree),
            files);
    }

    private static bool IsBuildArtifact(string path)
    {
        var relativeSegments = path.Split(System.IO.Path.DirectorySeparatorChar);
        return relativeSegments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
               relativeSegments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
