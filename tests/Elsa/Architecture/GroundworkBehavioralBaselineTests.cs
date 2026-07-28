using System.Diagnostics;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class GroundworkBehavioralBaselineTests
{
    [Fact]
    public void Candidate_tree_preserves_or_approves_every_exact_baseline_test_case()
    {
        var comparison = new GroundworkBehavioralBaselineScanner(RepoRoot).Compare(
            Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "coverage-ledger.json"),
            Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "research.md"));

        Assert.True(comparison.Violations.Count == 0, string.Join(Environment.NewLine, comparison.Violations));
    }

    [Fact]
    public void Comparison_discovers_exact_test_identities_from_the_immutable_baseline_ref()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Existing_fact()
                {
                }

                [Theory]
                [InlineData("sqlite")]
                public void Existing_theory(string provider)
                {
                }
            }
            """);
        fixture.CommitBaseline();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Existing_fact()
                {
                }

                [Theory]
                [InlineData("sqlite")]
                public void Existing_theory(string provider)
                {
                }

                [Fact]
                public void Additive_candidate_fact()
                {
                }
            }
            """);

        var comparison = fixture.Compare();

        Assert.Equal(
            [
                "tests/SampleTests.cs::FixtureTests.SampleTests.Existing_fact",
                "tests/SampleTests.cs::FixtureTests.SampleTests.Existing_theory[InlineData:\"sqlite\"]"
            ],
            comparison.BaselineTestCases);
        Assert.Equal(
            [
                "tests/SampleTests.cs::FixtureTests.SampleTests.Additive_candidate_fact",
                "tests/SampleTests.cs::FixtureTests.SampleTests.Existing_fact",
                "tests/SampleTests.cs::FixtureTests.SampleTests.Existing_theory[InlineData:\"sqlite\"]"
            ],
            comparison.CandidateTestCases);
        Assert.Empty(comparison.UnapprovedRemovals);
    }

    [Fact]
    public void Comparison_reports_removed_and_renamed_tests_by_their_exact_baseline_identity()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Removed_fact()
                {
                }

                [Fact]
                public void Renamed_fact()
                {
                }

                [Theory]
                [InlineData("sqlite")]
                public void Retained_theory(string provider)
                {
                }
            }
            """);
        fixture.CommitBaseline();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Replacement_name_for_renamed_fact()
                {
                }

                [Theory]
                [InlineData("sqlite")]
                public void Retained_theory(string provider)
                {
                }
            }
            """);

        var comparison = fixture.Compare();

        Assert.Equal(
            [
                "tests/SampleTests.cs::FixtureTests.SampleTests.Removed_fact",
                "tests/SampleTests.cs::FixtureTests.SampleTests.Renamed_fact"
            ],
            comparison.UnapprovedRemovals);
        Assert.DoesNotContain(
            "tests/SampleTests.cs::FixtureTests.SampleTests.Replacement_name_for_renamed_fact",
            comparison.BaselineTestCases);
    }

    [Fact]
    public void Comparison_reports_an_individual_removed_inline_data_case()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Theory]
                [InlineData("sqlite")]
                [InlineData("mongodb")]
                public void Provider_case(string provider)
                {
                }
            }
            """);
        fixture.CommitBaseline();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Theory]
                [InlineData("sqlite")]
                public void Provider_case(string provider)
                {
                }
            }
            """);

        var comparison = fixture.Compare();

        Assert.Equal(
            ["tests/SampleTests.cs::FixtureTests.SampleTests.Provider_case[InlineData:\"mongodb\"]"],
            comparison.UnapprovedRemovals);
    }

    [Fact]
    public void Comparison_reconciles_only_exact_complete_architect_approved_removals()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Approved_removal()
                {
                }

                [Fact]
                public void Unapproved_removal()
                {
                }

                [Fact]
                public void Incompletely_approved_removal()
                {
                }
            }
            """);
        fixture.CommitBaseline();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests;
            """);
        fixture.WriteApprovalLedger("""
            ## Test-removal approval ledger

            | Existing test | Objective | Replacement evidence / rationale | Architect | Decision | Date |
            |---|---|---|---|---|---|
            | tests/SampleTests.cs::FixtureTests.SampleTests.Approved_removal | Preserve the reviewed behavior | tests/ReplacementTests.cs::FixtureTests.ReplacementTests.Replacement | Elsa architect | Approved | 2026-07-15 |
            | tests/SampleTests.cs::FixtureTests.SampleTests.Incompletely_approved_removal | Preserve the reviewed behavior | tests/ReplacementTests.cs::FixtureTests.ReplacementTests.Replacement |  | Approved | 2026-07-15 |
            """);

        var comparison = fixture.Compare();

        Assert.Equal(
            ["tests/SampleTests.cs::FixtureTests.SampleTests.Approved_removal"],
            comparison.ApprovedRemovals);
        Assert.Equal(
            [
                "tests/SampleTests.cs::FixtureTests.SampleTests.Incompletely_approved_removal",
                "tests/SampleTests.cs::FixtureTests.SampleTests.Unapproved_removal"
            ],
            comparison.UnapprovedRemovals);
        Assert.Contains(
            comparison.Violations,
            violation => violation.Contains("Incompletely_approved_removal", StringComparison.Ordinal) &&
                         violation.Contains("architect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Candidate_ledger_path_edits_cannot_hide_a_removed_baseline_test()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Immutable_baseline_fact()
                {
                }
            }
            """);
        fixture.CommitBaseline();
        fixture.WriteTestSource("""
            namespace FixtureTests;

            public sealed class SampleTests;
            """);
        fixture.WriteCoverageLedger("tests/ReplacementTests.cs");

        var comparison = fixture.Compare();

        Assert.Contains(
            "tests/SampleTests.cs::FixtureTests.SampleTests.Immutable_baseline_fact",
            comparison.UnapprovedRemovals);
    }

    [Fact]
    public void Moving_pull_request_base_cannot_erode_immutable_baseline_paths_across_two_prs()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Immutable_baseline_fact()
                {
                }
            }
            """);
        fixture.WriteSource("tests/Neutral.cs", "namespace FixtureTests; public sealed class Neutral;");
        fixture.CommitBaseline();

        fixture.WriteCoverageLedger("tests/Neutral.cs");
        var weakenedPullRequestBase = fixture.CommitCandidate("PR A erodes the baseline path set");

        fixture.WriteTestSource("""
            namespace FixtureTests;

            public sealed class SampleTests;
            """);

        var comparison = fixture.CompareFromPullRequestBase(weakenedPullRequestBase);

        Assert.Contains(
            "tests/SampleTests.cs::FixtureTests.SampleTests.Immutable_baseline_fact",
            comparison.BaselineTestCases);
        Assert.Contains(
            "tests/SampleTests.cs::FixtureTests.SampleTests.Immutable_baseline_fact",
            comparison.UnapprovedRemovals);
    }

    [Fact]
    public void Moving_pull_request_base_cannot_replace_immutable_baseline_ref_across_two_prs()
    {
        using var fixture = new TemporaryGitRepository();
        const string originalSource = """
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Immutable_baseline_fact()
                {
                }
            }
            """;
        fixture.WriteTestSource(originalSource);
        fixture.CommitBaseline();

        fixture.WriteTestSource("""
            namespace FixtureTests;

            public sealed class SampleTests;
            """);
        var emptySourceRef = fixture.CommitCandidate("PR A prepares an empty alternate baseline");
        fixture.WriteTestSource(originalSource);
        fixture.WriteCoverageLedgerForBaseline(emptySourceRef, "tests/SampleTests.cs");
        var weakenedPullRequestBase = fixture.CommitCandidate("PR A moves baselineRef");

        fixture.WriteTestSource("""
            namespace FixtureTests;

            public sealed class SampleTests;
            """);

        var comparison = fixture.CompareFromPullRequestBase(weakenedPullRequestBase);

        Assert.Contains(
            "tests/SampleTests.cs::FixtureTests.SampleTests.Immutable_baseline_fact",
            comparison.BaselineTestCases);
        Assert.Contains(
            "tests/SampleTests.cs::FixtureTests.SampleTests.Immutable_baseline_fact",
            comparison.UnapprovedRemovals);
    }

    [Fact]
    public void Immutable_reviewed_ledger_history_must_be_available()
    {
        using var fixture = new TemporaryGitRepository();
        fixture.WriteTestSource("""
            using Xunit;

            namespace FixtureTests;

            public sealed class SampleTests
            {
                [Fact]
                public void Existing_fact()
                {
                }
            }
            """);
        fixture.CommitBaseline();
        var unavailableRef = new string('a', 40);

        var comparison = fixture.CompareFromImmutableReviewedRef(unavailableRef);

        Assert.Equal(
            [$"Initial reviewed coverage-ledger commit '{unavailableRef}' is unavailable; checkout must fetch immutable baseline history."],
            comparison.Violations);
        Assert.Empty(comparison.BaselineTestCases);
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
        }
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        private const string TestPath = "tests/SampleTests.cs";
        private const string LedgerPath = "coverage-ledger.json";
        private const string ApprovalLedgerPath = "research.md";

        public TemporaryGitRepository()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"elsa-groundwork-baseline-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            Git("init", "--quiet");
            Git("config", "user.name", "Elsa Architecture Tests");
            Git("config", "user.email", "architecture-tests@elsa.invalid");
            WriteApprovalLedger("""
                ## Test-removal approval ledger

                | Existing test | Objective | Replacement evidence / rationale | Architect | Decision | Date |
                |---|---|---|---|---|---|
                | None | No removal requested | Not applicable | Not applicable | No approval | 2026-07-15 |
                """);
        }

        public string Path { get; }

        public string BaselineRef { get; private set; } = string.Empty;

        public string ReviewedLedgerRef { get; private set; } = string.Empty;

        public void WriteTestSource(string content) => Write(TestPath, content);

        public void WriteSource(string relativePath, string content) => Write(relativePath, content);

        public void WriteApprovalLedger(string content) => Write(ApprovalLedgerPath, content);

        public void WriteCoverageLedger(params string[] behavioralBaselinePaths)
            => WriteCoverageLedgerForBaseline(BaselineRef, behavioralBaselinePaths);

        public void WriteCoverageLedgerForBaseline(
            string baselineRef,
            params string[] behavioralBaselinePaths)
        {
            var paths = string.Join(
                $",{Environment.NewLine}",
                behavioralBaselinePaths.Select(path => $"                        \"{path}\""));
            Write(
                LedgerPath,
                $$"""
                  {
                    "baselineRef": "{{baselineRef}}",
                    "entries": [
                      {
                        "behavioralBaseline": [
                  {{paths}}
                        ]
                      }
                    ]
                  }
                  """);
        }

        public void CommitBaseline()
        {
            Git("add", "--all");
            Git("commit", "--quiet", "--no-gpg-sign", "--message", "baseline");
            BaselineRef = Git("rev-parse", "HEAD");
            WriteCoverageLedger(TestPath);
            Git("add", LedgerPath);
            Git("commit", "--quiet", "--no-gpg-sign", "--message", "reviewed ledger");
            ReviewedLedgerRef = Git("rev-parse", "HEAD");
        }

        public string CommitCandidate(string message)
        {
            Git("add", "--all");
            Git("commit", "--quiet", "--no-gpg-sign", "--message", message);
            return Git("rev-parse", "HEAD");
        }

        public GroundworkBehavioralBaselineComparison Compare() =>
            new GroundworkBehavioralBaselineScanner(Path, ReviewedLedgerRef).Compare(
                System.IO.Path.Combine(Path, LedgerPath),
                System.IO.Path.Combine(Path, ApprovalLedgerPath));

        public GroundworkBehavioralBaselineComparison CompareFromPullRequestBase(string pullRequestBaseRef)
        {
            var actualBaseRef = Git("rev-parse", "HEAD");
            if (!StringComparer.Ordinal.Equals(actualBaseRef, pullRequestBaseRef))
            {
                throw new InvalidOperationException(
                    $"Fixture pull-request base is '{actualBaseRef}', not expected ref '{pullRequestBaseRef}'.");
            }

            return Compare();
        }

        public GroundworkBehavioralBaselineComparison CompareFromImmutableReviewedRef(string immutableReviewedRef) =>
            new GroundworkBehavioralBaselineScanner(Path, immutableReviewedRef).Compare(
                System.IO.Path.Combine(Path, LedgerPath),
                System.IO.Path.Combine(Path, ApprovalLedgerPath));

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
