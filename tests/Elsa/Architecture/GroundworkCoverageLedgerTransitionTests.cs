using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class GroundworkCoverageLedgerTransitionTests
{
    private const string BaseRefEnvironmentVariable = "GROUNDWORK_LEDGER_BASE_REF";

    [Fact]
    public void Pull_request_candidate_obeys_status_transitions_from_the_github_base_sha()
    {
        var baseRef = Environment.GetEnvironmentVariable(BaseRefEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(baseRef))
            return;

        var result = GroundworkCoverageLedgerTransitionGuard.Evaluate(
            RepoRoot,
            LedgerPath,
            baseRef,
            CreateValidator());

        Assert.True(result.Findings.Count == 0, string.Join(Environment.NewLine, result.Findings));
    }

    [Fact]
    public void Repository_guard_rejects_a_forbidden_transition_from_the_reviewed_base_ledger()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteLedger("excluded");
        fixture.CommitBase();
        fixture.WriteLedger("planned");

        var result = GroundworkCoverageLedgerTransitionGuard.Evaluate(
            fixture.Path,
            fixture.LedgerPath,
            fixture.BaseRef,
            fixture.CreateValidator());

        Assert.True(result.BaselineLedgerFound);
        Assert.False(result.IsInitialActivation);
        Assert.Equal(
            ["sample-row: status transition 'excluded' -> 'planned' is not allowed because excluded is terminal."],
            result.Findings);
    }

    [Fact]
    public void Repository_guard_explicitly_allows_initial_activation_when_the_base_has_no_ledger()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.CommitBase();
        fixture.WriteLedger("planned");

        var result = GroundworkCoverageLedgerTransitionGuard.Evaluate(
            fixture.Path,
            fixture.LedgerPath,
            fixture.BaseRef,
            fixture.CreateValidator());

        Assert.False(result.BaselineLedgerFound);
        Assert.True(result.IsInitialActivation);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Repository_guard_does_not_treat_unavailable_history_as_initial_activation()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteLedger("planned");

        var result = GroundworkCoverageLedgerTransitionGuard.Evaluate(
            fixture.Path,
            fixture.LedgerPath,
            new string('a', 40),
            fixture.CreateValidator());

        Assert.False(result.BaselineLedgerFound);
        Assert.False(result.IsInitialActivation);
        Assert.Equal(
            ["Coverage-ledger base commit 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' is unavailable; checkout must fetch immutable baseline history."],
            result.Findings);
    }

    private static GroundworkCoverageLedgerValidator CreateValidator() => new(SchemaPath, []);

    private static string LedgerPath => System.IO.Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "coverage-ledger.json");

    private static string SchemaPath => System.IO.Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "contracts",
        "coverage-ledger.schema.json");

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        private const string LedgerRelativePath = "specs/094-harden-groundwork-stores/coverage-ledger.json";
        private const string SchemaRelativePath = "coverage-ledger.schema.json";

        public TemporaryGitRepository()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"elsa-groundwork-ledger-transition-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            Git("init", "--quiet");
            Git("config", "user.name", "Elsa Architecture Tests");
            Git("config", "user.email", "architecture-tests@elsa.invalid");
            Write(SchemaRelativePath, "{}");
        }

        public string Path { get; }

        public string LedgerPath => System.IO.Path.Combine(Path, LedgerRelativePath);

        public string BaseRef { get; private set; } = string.Empty;

        public GroundworkCoverageLedgerValidator CreateValidator() =>
            new(System.IO.Path.Combine(Path, SchemaRelativePath), []);

        public void WriteLedger(string status) => Write(
            LedgerRelativePath,
            $$"""
              {
                "entries": [
                  {
                    "id": "sample-row",
                    "status": "{{status}}"
                  }
                ]
              }
              """);

        public void CommitBase()
        {
            Write("README.md", "base");
            Git("add", ".");
            Git("commit", "--quiet", "--no-gpg-sign", "--message", "base");
            BaseRef = Git("rev-parse", "HEAD");
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private void Write(string relativePath, string content)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        private string Git(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
            return output.Trim();
        }
    }
}

internal sealed record GroundworkCoverageLedgerTransitionResult(
    bool BaselineLedgerFound,
    bool IsInitialActivation,
    IReadOnlyList<string> Findings);

internal static class GroundworkCoverageLedgerTransitionGuard
{
    public static GroundworkCoverageLedgerTransitionResult Evaluate(
        string repositoryRoot,
        string candidateLedgerPath,
        string baseRef,
        GroundworkCoverageLedgerValidator validator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateLedgerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseRef);
        ArgumentNullException.ThrowIfNull(validator);

        var root = System.IO.Path.GetFullPath(repositoryRoot);
        var ledgerPath = RepositoryRelativePath(root, candidateLedgerPath);
        var commit = GroundworkRepositoryGit.Run(root, "cat-file", "-e", $"{baseRef}^{{commit}}");
        if (commit.ExitCode != 0)
        {
            return new GroundworkCoverageLedgerTransitionResult(
                false,
                false,
                [$"Coverage-ledger base commit '{baseRef}' is unavailable; checkout must fetch immutable baseline history."]);
        }

        var ledgerObject = GroundworkRepositoryGit.Run(root, "cat-file", "-e", $"{baseRef}:{ledgerPath}");
        if (ledgerObject.ExitCode != 0)
            return new GroundworkCoverageLedgerTransitionResult(false, true, []);

        var previousSource = GroundworkRepositoryGit.Run(root, "show", $"{baseRef}:{ledgerPath}");
        if (previousSource.ExitCode != 0)
        {
            return new GroundworkCoverageLedgerTransitionResult(
                true,
                false,
                [$"Coverage ledger '{ledgerPath}' could not be read from base commit '{baseRef}': {previousSource.StandardError.Trim()}"]);
        }

        var previous = JsonNode.Parse(previousSource.StandardOutput)?.AsObject();
        if (previous is null)
        {
            return new GroundworkCoverageLedgerTransitionResult(
                true,
                false,
                [$"Coverage ledger '{ledgerPath}' at base commit '{baseRef}' is empty."]);
        }

        var current = JsonNode.Parse(File.ReadAllText(candidateLedgerPath))?.AsObject()
                      ?? throw new InvalidOperationException($"Coverage ledger '{candidateLedgerPath}' is empty.");
        return new GroundworkCoverageLedgerTransitionResult(
            true,
            false,
            validator.ValidateTransition(previous, current));
    }

    private static string RepositoryRelativePath(string repositoryRoot, string path)
    {
        var relativePath = System.IO.Path.GetRelativePath(repositoryRoot, System.IO.Path.GetFullPath(path))
            .Replace('\\', '/');
        if (relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidOperationException($"Coverage ledger '{path}' is outside repository root '{repositoryRoot}'.");
        return relativePath;
    }
}

internal sealed record GroundworkRepositoryGitResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal static class GroundworkRepositoryGit
{
    public static GroundworkRepositoryGitResult Run(string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start git for coverage-ledger transition validation.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GroundworkRepositoryGitResult(process.ExitCode, standardOutput, standardError);
    }
}
