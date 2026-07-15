using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Elsa.Architecture.Tests;

internal sealed record GroundworkBehavioralBaselineComparison(
    IReadOnlyList<string> BaselineTestCases,
    IReadOnlyList<string> CandidateTestCases,
    IReadOnlyList<string> ApprovedRemovals,
    IReadOnlyList<string> UnapprovedRemovals,
    IReadOnlyList<string> Violations);

internal sealed class GroundworkBehavioralBaselineScanner(
    string repositoryRoot,
    string? initialReviewedLedgerRef = null)
{
    // Baseline identity is permanently rooted here; a moving PR base may not redefine it across merges.
    private const string InitialReviewedLedgerRef = "dec0b88bc21db15aa3c22181648ab201c483b01a";
    private static readonly Regex BaselineRefPattern = new("^[0-9a-f]{40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NamespacePattern = new(
        @"\bnamespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*[;{]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ClassPattern = new(
        @"\b(?:class|record\s+class)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TestMethodPattern = new(
        @"(?<attributes>(?:\s*\[[^\]]+\]\s*)+)(?:public|internal|protected|private)?\s*(?:static\s+)?(?:async\s+)?(?:void|Task|ValueTask|[A-Za-z_][A-Za-z0-9_<>?.]*)\s+(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex XunitAttributePattern = new(
        @"\[\s*(?:Xunit\.)?(?:Fact|Theory)(?:Attribute)?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TheoryAttributePattern = new(
        @"\[\s*(?:Xunit\.)?Theory(?:Attribute)?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InlineDataAttributePattern = new(
        @"\[\s*(?:Xunit\.)?InlineData(?:Attribute)?\s*\((?<arguments>.*?)\)\s*\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private readonly string _repositoryRoot = Path.GetFullPath(repositoryRoot);
    private readonly string _initialReviewedLedgerRef = initialReviewedLedgerRef ?? InitialReviewedLedgerRef;

    public GroundworkBehavioralBaselineComparison Compare(
        string coverageLedgerPath,
        string researchApprovalLedgerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coverageLedgerPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(researchApprovalLedgerPath);

        var violations = new List<string>();
        var ledgerPath = RepositoryRelativePath(coverageLedgerPath);
        var ledger = ReadReviewedLedger(ledgerPath, violations);
        if (ledger is null)
        {
            return new GroundworkBehavioralBaselineComparison(
                [],
                [],
                [],
                [],
                violations.Order(StringComparer.Ordinal).ToArray());
        }

        var baselineRef = ledger["baselineRef"]?.GetValue<string>()
                          ?? throw new InvalidOperationException("Coverage ledger has no baselineRef.");
        if (!BaselineRefPattern.IsMatch(baselineRef))
            throw new InvalidOperationException($"Coverage ledger baselineRef '{baselineRef}' is not an immutable 40-character commit.");

        var baselinePaths = ledger["entries"]?.AsArray()
            .OfType<JsonObject>()
            .SelectMany(entry => entry["behavioralBaseline"]?.AsArray() ?? [])
            .OfType<JsonValue>()
            .Select(value => NormalizePath(value.GetValue<string>()))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()
            ?? throw new InvalidOperationException("Coverage ledger has no entries array.");

        var baselineTestCases = new SortedSet<string>(StringComparer.Ordinal);
        var candidateTestCases = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in baselinePaths)
        {
            var baselineSource = GitShow(baselineRef, path, violations);
            if (baselineSource is not null)
            {
                foreach (var identity in DiscoverTestCases(path, baselineSource))
                    baselineTestCases.Add(identity);
            }

            var candidatePath = Path.Combine(_repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(candidatePath))
                continue;

            foreach (var identity in DiscoverTestCases(path, File.ReadAllText(candidatePath)))
                candidateTestCases.Add(identity);
        }

        var removed = baselineTestCases.Except(candidateTestCases, StringComparer.Ordinal).ToArray();
        var approvals = ReadApprovals(researchApprovalLedgerPath);
        var approvedRemovals = new List<string>();
        var unapprovedRemovals = new List<string>();

        foreach (var identity in removed)
        {
            if (!approvals.TryGetValue(identity, out var approval))
            {
                unapprovedRemovals.Add(identity);
                violations.Add($"Baseline test case '{identity}' was removed or renamed without an architect-approved test-removal entry.");
                continue;
            }

            var approvalProblems = approval.Problems();
            if (approvalProblems.Count == 0)
            {
                approvedRemovals.Add(identity);
                continue;
            }

            unapprovedRemovals.Add(identity);
            violations.Add(
                $"Test-removal approval for '{identity}' is incomplete: {string.Join(", ", approvalProblems)}.");
        }

        return new GroundworkBehavioralBaselineComparison(
            baselineTestCases.ToArray(),
            candidateTestCases.ToArray(),
            approvedRemovals.Order(StringComparer.Ordinal).ToArray(),
            unapprovedRemovals.Order(StringComparer.Ordinal).ToArray(),
            violations.Order(StringComparer.Ordinal).ToArray());
    }

    private JsonObject? ReadReviewedLedger(string ledgerPath, ICollection<string> violations)
    {
        if (!BaselineRefPattern.IsMatch(_initialReviewedLedgerRef))
        {
            violations.Add(
                $"Initial reviewed coverage-ledger ref '{_initialReviewedLedgerRef}' is not an immutable 40-character commit.");
            return null;
        }

        if (!GitObjectExists($"{_initialReviewedLedgerRef}^{{commit}}"))
        {
            violations.Add(
                $"Initial reviewed coverage-ledger commit '{_initialReviewedLedgerRef}' is unavailable; checkout must fetch immutable baseline history.");
            return null;
        }

        if (!GitObjectExists($"{_initialReviewedLedgerRef}:{ledgerPath}"))
        {
            violations.Add(
                $"Initial reviewed coverage ledger '{ledgerPath}' does not exist at immutable commit '{_initialReviewedLedgerRef}'.");
            return null;
        }

        return ParseLedger(
            GitShowRequired(_initialReviewedLedgerRef, ledgerPath),
            _initialReviewedLedgerRef,
            ledgerPath);
    }

    private static JsonObject ParseLedger(string source, string sourceRef, string path) =>
        JsonNode.Parse(source)?.AsObject()
        ?? throw new InvalidOperationException($"Reviewed coverage ledger '{path}' at '{sourceRef}' is empty.");

    private string RepositoryRelativePath(string path)
    {
        var relativePath = NormalizePath(Path.GetRelativePath(_repositoryRoot, Path.GetFullPath(path)));
        if (relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidOperationException($"Coverage ledger '{path}' is outside repository root '{_repositoryRoot}'.");
        return relativePath;
    }

    private bool GitObjectExists(string objectName) => RunGit("cat-file", "-e", objectName).ExitCode == 0;

    private string GitShowRequired(string sourceRef, string path)
    {
        var result = RunGit("show", $"{sourceRef}:{path}");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not read reviewed coverage ledger '{path}' from '{sourceRef}': {result.StandardError.Trim()}");
        }

        return result.StandardOutput;
    }

    private string? GitShow(string baselineRef, string path, ICollection<string> violations)
    {
        var result = RunGit("show", $"{baselineRef}:{path}");
        if (result.ExitCode == 0)
            return result.StandardOutput;

        violations.Add(
            $"Behavioral baseline path '{path}' cannot be read from immutable commit '{baselineRef}': {result.StandardError.Trim()}".TrimEnd());
        return null;
    }

    private GroundworkRepositoryGitResult RunGit(params string[] arguments) =>
        GroundworkRepositoryGit.Run(_repositoryRoot, arguments);

    private static IReadOnlyList<string> DiscoverTestCases(string path, string source)
    {
        var namespaceName = NamespacePattern.Match(source) is { Success: true } namespaceMatch
            ? namespaceMatch.Groups["name"].Value
            : string.Empty;
        var identities = new List<string>();

        foreach (Match methodMatch in TestMethodPattern.Matches(source))
        {
            if (!XunitAttributePattern.IsMatch(methodMatch.Groups["attributes"].Value))
                continue;

            var classMatches = ClassPattern.Matches(source[..methodMatch.Index]);
            if (classMatches.Count == 0)
                continue;

            var className = classMatches[^1].Groups["name"].Value;
            var qualifiedType = string.IsNullOrWhiteSpace(namespaceName)
                ? className
                : $"{namespaceName}.{className}";
            var methodIdentity = $"{path}::{qualifiedType}.{methodMatch.Groups["method"].Value}";
            var attributes = methodMatch.Groups["attributes"].Value;
            var inlineData = TheoryAttributePattern.IsMatch(attributes)
                ? InlineDataAttributePattern.Matches(attributes)
                    .Select(match => CanonicalizeInlineDataArguments(match.Groups["arguments"].Value))
                    .ToArray()
                : [];

            if (inlineData.Length == 0)
                identities.Add(methodIdentity);
            else
                identities.AddRange(inlineData.Select(arguments => $"{methodIdentity}[InlineData:{arguments}]"));
        }

        return identities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static string CanonicalizeInlineDataArguments(string arguments)
    {
        var result = new StringBuilder(arguments.Length);
        var index = 0;
        while (index < arguments.Length)
        {
            var current = arguments[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '/' && index + 1 < arguments.Length && arguments[index + 1] == '/')
            {
                index += 2;
                while (index < arguments.Length && arguments[index] != '\n')
                    index++;
                continue;
            }

            if (current == '/' && index + 1 < arguments.Length && arguments[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < arguments.Length && (arguments[index] != '*' || arguments[index + 1] != '/'))
                    index++;
                index = Math.Min(arguments.Length, index + 2);
                continue;
            }

            if (current == '@' && index + 1 < arguments.Length && arguments[index + 1] == '"')
            {
                result.Append('@');
                index++;
                CopyQuoted(arguments, result, ref index, verbatim: true);
                continue;
            }

            if (current == '"')
            {
                var quoteCount = CountRun(arguments, index, '"');
                if (quoteCount >= 3)
                    CopyRawString(arguments, result, ref index, quoteCount);
                else
                    CopyQuoted(arguments, result, ref index, verbatim: false);
                continue;
            }

            if (current == '\'')
            {
                CopyCharacter(arguments, result, ref index);
                continue;
            }

            result.Append(current);
            index++;
        }

        return result.ToString();
    }

    private static void CopyQuoted(string source, StringBuilder destination, ref int index, bool verbatim)
    {
        destination.Append(source[index++]);
        while (index < source.Length)
        {
            var current = source[index++];
            destination.Append(current);
            if (!verbatim && current == '\\' && index < source.Length)
            {
                destination.Append(source[index++]);
                continue;
            }

            if (current != '"')
                continue;
            if (verbatim && index < source.Length && source[index] == '"')
            {
                destination.Append(source[index++]);
                continue;
            }

            return;
        }
    }

    private static void CopyRawString(string source, StringBuilder destination, ref int index, int quoteCount)
    {
        destination.Append(source, index, quoteCount);
        index += quoteCount;
        while (index < source.Length)
        {
            var currentRun = source[index] == '"' ? CountRun(source, index, '"') : 0;
            if (currentRun >= quoteCount)
            {
                destination.Append(source, index, quoteCount);
                index += quoteCount;
                return;
            }

            destination.Append(source[index++]);
        }
    }

    private static void CopyCharacter(string source, StringBuilder destination, ref int index)
    {
        destination.Append(source[index++]);
        while (index < source.Length)
        {
            var current = source[index++];
            destination.Append(current);
            if (current == '\\' && index < source.Length)
            {
                destination.Append(source[index++]);
                continue;
            }

            if (current == '\'')
                return;
        }
    }

    private static int CountRun(string source, int start, char value)
    {
        var end = start;
        while (end < source.Length && source[end] == value)
            end++;
        return end - start;
    }

    private static IReadOnlyDictionary<string, RemovalApproval> ReadApprovals(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, RemovalApproval>(StringComparer.Ordinal);

        const string heading = "## Test-removal approval ledger";
        var lines = File.ReadAllLines(path);
        var headingIndex = Array.FindIndex(lines, line => line.Trim().Equals(heading, StringComparison.OrdinalIgnoreCase));
        if (headingIndex < 0)
            return new Dictionary<string, RemovalApproval>(StringComparer.Ordinal);

        var approvals = new Dictionary<string, RemovalApproval>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(headingIndex + 1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (approvals.Count != 0)
                    break;
                continue;
            }

            if (!line.TrimStart().StartsWith('|'))
            {
                if (approvals.Count != 0)
                    break;
                continue;
            }

            var columns = line.Trim().Trim('|').Split('|').Select(column => column.Trim()).ToArray();
            if (columns.Length != 6 ||
                columns[0].Equals("Existing test", StringComparison.OrdinalIgnoreCase) ||
                columns.All(column => column.All(character => character is '-' or ':' or ' ')))
            {
                continue;
            }

            var identity = columns[0];
            if (identity.Equals("None", StringComparison.OrdinalIgnoreCase))
                continue;

            approvals[identity] = new RemovalApproval(
                columns[1],
                columns[2],
                columns[3],
                columns[4],
                columns[5]);
        }

        return approvals;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private sealed record RemovalApproval(
        string Objective,
        string ReplacementOrRationale,
        string Architect,
        string Decision,
        string Date)
    {
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(Objective))
                problems.Add("objective is missing");
            if (string.IsNullOrWhiteSpace(ReplacementOrRationale))
                problems.Add("replacement evidence or rationale is missing");
            if (string.IsNullOrWhiteSpace(Architect))
                problems.Add("architect is missing");
            if (!Decision.Equals("Approved", StringComparison.OrdinalIgnoreCase))
                problems.Add("decision is not Approved");
            if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", out _))
                problems.Add("approval date is invalid");
            return problems;
        }
    }
}
