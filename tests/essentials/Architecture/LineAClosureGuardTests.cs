using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps Line A closed under <c>ProjectReference</c> (ADR 0067, Consequences and Decision): a Line A
/// package may reference only Line A packages, because a Line B dependency's floor moves with every
/// release and would drag the shared baseline with it.
/// </summary>
/// <remarks>
/// Line A membership is read from the repository's one reviewed list, <c>VersionLines.props</c>, rather
/// than restated here — the same file <c>Directory.Build.props</c> imports to compute each project's
/// <c>$(ElsaVersionLine)</c>. A member that is a typo — one that names no existing project — would make
/// the closure check vacuous for it, so that is caught too.
/// </remarks>
public sealed class LineAClosureGuardTests
{
    private static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>Line A membership, in the order <c>VersionLines.props</c> lists it.</summary>
    private static IReadOnlyList<string> LineAMembers { get; } = VersionLines.LineAMembers(RepoRoot);

    /// <summary>Every Elsa project under <c>src/</c>, by name, with the Elsa projects it directly references.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ElsaReferences { get; } = ProjectGraph.LoadElsaReferences(RepoRoot);

    [Fact]
    public void Line_a_members_reference_only_line_a_members()
    {
        var violations = LineBReferences(LineAMembers, ElsaReferences);

        Assert.True(violations.Count == 0, Report(violations));
    }

    /// <summary>
    /// Pins that the scan is not vacuous: the list is non-empty, and a typo — a name with no matching
    /// entry in <see cref="ElsaReferences"/> — is caught rather than silently contributing nothing to check.
    /// </summary>
    [Fact]
    public void Every_line_a_member_names_an_existing_project()
    {
        Assert.NotEmpty(LineAMembers);

        var missing = LineAMembers.Where(member => !ElsaReferences.ContainsKey(member)).ToArray();
        Assert.True(missing.Length == 0, $"VersionLines.props names Line A project(s) that do not exist: {string.Join(", ", missing)}.");
    }

    /// <summary>The detector itself, so the theory above's green means "closed" rather than "nothing checked".</summary>
    [Fact]
    public void A_line_a_reference_to_a_line_b_project_is_flagged()
    {
        var references = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Primitives"] = [],
            ["Elsa.Pipelines.Core"] = [],
            ["Elsa.Events.Core"] = ["Elsa.Pipelines.Core", "Elsa.Workflows.Runtime.Core"],
        };

        Assert.Equal(
            ["Elsa.Events.Core references Line B project Elsa.Workflows.Runtime.Core"],
            LineBReferences(["Elsa.Primitives", "Elsa.Pipelines.Core", "Elsa.Events.Core"], references));

        Assert.Empty(LineBReferences(
            ["Elsa.Primitives", "Elsa.Pipelines.Core"],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Elsa.Primitives"] = [],
                ["Elsa.Pipelines.Core"] = []
            }));
    }

    /// <summary>Every reference a Line A member has to a project outside Line A, by member.</summary>
    private static IReadOnlyList<string> LineBReferences(
        IReadOnlyList<string> lineA,
        IReadOnlyDictionary<string, IReadOnlyList<string>> elsaReferences)
    {
        var lineASet = lineA.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. lineA
                .Where(elsaReferences.ContainsKey)
                .SelectMany(member => elsaReferences[member]
                    .Where(reference => !lineASet.Contains(reference))
                    .Select(reference => $"{member} references Line B project {reference}"))
                .Order(StringComparer.Ordinal)
        ];
    }

    private static string Report(IReadOnlyList<string> violations) =>
        "Line A is not closed under ProjectReference (ADR 0067). Either add the referenced project to " +
        "VersionLines.props or remove the reference:" +
        string.Concat(violations.Select(violation => $"{Environment.NewLine}  {violation}"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
