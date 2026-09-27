using Elsa.Versioning.Calculator.Tests.Support;

namespace Elsa.Versioning.Calculator.Tests;

/// <summary>
/// Spec 150 FR-003's force-advance: packages named at publish time advance although no input changed, each recording
/// why, and every rule and gate holds for them as for a changed package.
/// </summary>
public sealed class ForceAdvanceTests : SyntheticHistory
{
    private const string Reason = "republish with the patched Microsoft.OpenApi floor (GHSA-0000-0000-0000)";

    private static ForcedAdvance Forcing(params string[] packageIds) => new(packageIds, Reason);

    [Fact]
    public void A_forced_package_advances_once_with_its_reason_and_nothing_else_moves()
    {
        var computation = Compute(forced: Forcing("Elsa.Tasks"));

        Assert.Equal(["Elsa.Tasks"], computation.Affected);
        Assert.Equal("4.0.8", VersionOf(computation, "Elsa.Tasks"));
        Assert.Equal([ForcedAdvance.ReasonPrefix + Reason], computation["Elsa.Tasks"].Reasons);
    }

    /// <summary>A forced package that changed as well advances once, and records both.</summary>
    [Fact]
    public void A_forced_package_that_also_changed_advances_once_and_records_both_reasons()
    {
        Edit("src/Tasks/Scheduler.cs");

        var computation = Compute(Repo.Commit(), forced: Forcing("elsa.tasks"));

        Assert.Equal(["Elsa.Tasks"], computation.Affected);
        Assert.Equal("4.0.8", VersionOf(computation, "Elsa.Tasks"));
        Assert.Equal(["src/Tasks/Scheduler.cs (changed)", ForcedAdvance.ReasonPrefix + Reason], computation["Elsa.Tasks"].Reasons);
    }

    /// <summary>Forcing a Line A member moves the line as one (FR-002), and a tool carrying a forced package moves with it.</summary>
    [Fact]
    public void A_forced_package_moves_with_the_rules_a_changed_one_does()
    {
        var computation = Compute(forced: Forcing("Elsa.Primitives", "Elsa.Http"));

        Assert.Equal(["Elsa.Events.Core", "Elsa.Http", "Elsa.Primitives", "dotnet-elsa"], computation.Affected);
        Assert.Equal("4.0.4", VersionOf(computation, "Elsa.Events.Core"));
        Assert.Equal(["carries Elsa.Http, which advances; a tool package holds the builds of what it references"], computation["dotnet-elsa"].Reasons);
    }

    /// <summary>FR-012: forcing a package never lets it publish at or below its last published version.</summary>
    [Fact]
    public void The_monotonicity_gate_holds_for_a_forced_package()
    {
        var ahead = RecordWith("Elsa.Tasks", "4.1.2-preview");

        var exception = Assert.Throws<VersionGateException>(() => Compute(record: ahead, forced: Forcing("Elsa.Tasks")));

        Assert.Contains("Elsa.Tasks: computed 4.0.0, last published 4.1.2-preview", exception.Message, StringComparison.Ordinal);
        Assert.Contains(ForcedAdvance.ReasonPrefix + Reason, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The direction that would look like success: a misspelt id must not advance nothing quietly.</summary>
    [Fact]
    public void A_force_advance_naming_a_package_the_commit_lacks_is_refused()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Compute(forced: Forcing("Elsa.Tasks", "Elsa.Taks")));

        Assert.Contains("Elsa.Taks", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", Reason)]
    [InlineData("Elsa.Tasks,elsa.tasks", Reason)]
    [InlineData("Elsa.Tasks", " ")]
    public void A_force_advance_names_each_package_once_and_states_a_reason(string packageIds, string reason) =>
        Assert.Throws<ArgumentException>(() => ForcedAdvance.Parse(packageIds, reason));
}
