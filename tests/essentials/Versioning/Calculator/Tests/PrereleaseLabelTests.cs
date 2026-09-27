namespace Elsa.Versioning.Calculator.Tests;

/// <summary>Spec 150 FR-008: main's label while the lines are unreleased and after, and a branch-scoped label everywhere else.</summary>
public sealed class PrereleaseLabelTests
{
    [Theory]
    [InlineData("main", "preview", "preview")]
    [InlineData("refs/heads/main", "preview", "preview")]
    [InlineData("main", "rc.1", "rc.1")]
    public void Main_carries_the_label_version_lines_names(string branch, string mainLabel, string expected) =>
        Assert.Equal(expected, PrereleaseLabel.For(branch, mainLabel));

    /// <summary>Once <c>ElsaPrereleaseLabel</c> is empty the lines are released, and main's packages carry no label.</summary>
    [Fact]
    public void Main_carries_no_label_once_the_lines_are_released() =>
        Assert.Null(PrereleaseLabel.For("main", string.Empty));

    /// <summary>US4 scenario 2: every other branch is scoped to itself, with the same label before and after release.</summary>
    [Theory]
    [InlineData("feat/Issue_2080", "branch-feat-issue-2080")]
    [InlineData("refs/heads/feat/Issue_2080", "branch-feat-issue-2080")]
    [InlineData("release/4.1", "branch-release-4-1")]
    [InlineData("--fix//double__separators--", "branch-fix-double-separators")]
    [InlineData("007", "branch-007")]
    [InlineData("Main", "branch-main")]
    [InlineData("preview", "branch-preview")]
    [InlineData("claude/2080-pack-time-changes", "branch-claude-2080-pack-time-changes")]
    public void Any_other_branch_carries_a_label_scoped_to_it(string branch, string expected)
    {
        Assert.Equal(expected, PrereleaseLabel.For(branch, "preview"));
        Assert.Equal(expected, PrereleaseLabel.For(branch, string.Empty));
    }

    /// <summary>A long name keeps its first 40 characters, without a trailing '-', so a whole version stays within 64.</summary>
    [Fact]
    public void A_long_branch_name_is_cut_to_forty_characters()
    {
        var label = PrereleaseLabel.For("feature/" + new string('x', 31) + "-yyyyyyyy", "preview");

        Assert.Equal("branch-feature-" + new string('x', 31), label);
        Assert.True($"4.0.123456-{label}".Length <= 64);
    }

    /// <summary>A branch label can never be main's: it always leads with its prefix, which sorts below <c>preview</c>.</summary>
    [Fact]
    public void A_branch_label_sorts_below_mains_and_never_equals_it() =>
        Assert.True(string.CompareOrdinal(PrereleaseLabel.For("zzz", "preview"), "preview") < 0);

    [Theory]
    [InlineData("feat", "not a label!")]
    [InlineData("feat", "preview..1")]
    [InlineData("feat", "branch-lookalike")]
    [InlineData("///", "preview")]
    [InlineData("refs/heads/", "preview")]
    public void A_label_that_cannot_be_made_is_refused(string branch, string mainLabel) =>
        Assert.Throws<InvalidOperationException>(() => PrereleaseLabel.For(branch, mainLabel));
}
