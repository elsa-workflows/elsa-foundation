using Elsa.Activities.Testing;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// The positive pin on <see cref="DesignTimeAssemblies"/>, the classifier every runtime-only guard in this
/// repository is built on (spec 151, SC-B-001 / SC-B-005).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class has to exist.</b> Every other assertion about the design/publish boundary has the shape "no
/// forbidden assembly is present", and a matcher that answers <c>false</c> to everything satisfies all of them
/// vacuously — the whole suite stayed green with the classifier stubbed out. The two theories below are the only
/// assertions in the repository that fail when the matcher stops matching, so a broken classifier is a red build
/// rather than a silently disabled boundary.
/// </para>
/// <para>
/// <b>Why the negative theory is not padding.</b> The rule is segment-exact, and the reason is
/// <c>Elsa.Persistence.Groundwork.DesignConformance</c>: it contains the letters "Design" but is a persistence
/// conformance suite that legitimately belongs in a runtime-only closure, so a <c>Contains("Design")</c>
/// shortcut would start failing runtime-only guards on an assembly that is not a design assembly at all. The four
/// <c>.Runtime</c> halves T128 created are pinned for the mirror-image reason — they are precisely the names a
/// too-eager rule would be tempted to sweep up alongside their <c>.Design</c> siblings.
/// </para>
/// </remarks>
public sealed class DesignTimeAssemblyClassifierTests
{
    /// <summary>
    /// The design/publish family, named exhaustively rather than by the rule that recognizes it.
    /// </summary>
    /// <remarks>
    /// The six <c>Elsa.Activities.*.Design</c> entries are the point. They match none of the classifier's three
    /// prefixes; before the <c>.Design</c>-segment rule they were caught only because each one happens to
    /// reference a <c>*.Design.Core</c> that a prefix does match, so the boundary held transitively and by
    /// coincidence. The shared cores are pinned alongside them so the prefix arm cannot be dropped either.
    /// </remarks>
    [Theory]
    // T128's six per-package design halves — the family the prefix list never named.
    [InlineData("Elsa.Activities.Bpmn.Design")]
    [InlineData("Elsa.Activities.ControlFlow.Design")]
    [InlineData("Elsa.Activities.DispatchWorkflow.Design")]
    [InlineData("Elsa.Activities.Flowchart.Design")]
    [InlineData("Elsa.Activities.Graph.Design")]
    [InlineData("Elsa.Activities.Sequence.Design")]
    // The shared cores and their sub-packages — the prefix arm.
    [InlineData("Elsa.Workflows.Design.Core")]
    [InlineData("Elsa.Workflows.Design.Api")]
    [InlineData("Elsa.Workflows.Design.Persistence.Core")]
    [InlineData("Elsa.Workflows.Design.Validations")]
    [InlineData("Elsa.Activities.Design.Core")]
    [InlineData("Elsa.Activities.Design.Api")]
    [InlineData("Elsa.Activities.Design.Persistence.Core")]
    // Publishing has no trailing dot in the prefix: the engine assembly itself is in the family.
    [InlineData("Elsa.Workflows.Publishing")]
    [InlineData("Elsa.Workflows.Publishing.Core")]
    [InlineData("Elsa.Workflows.Publishing.Api")]
    public void A_design_or_publishing_assembly_name_is_classified_as_design_time(string assemblyName) =>
        Assert.True(
            DesignTimeAssemblies.IsDesignOrPublishing(assemblyName),
            $"{assemblyName} belongs to the design/publish family and must be excluded from a runtime-only closure.");

    /// <summary>
    /// Names a runtime-only closure legitimately contains — the false half, without which the rule could be
    /// tightened into a <c>Contains("Design")</c> that fails honest runtime compositions.
    /// </summary>
    [Theory]
    // T128's four new runtime halves: Bpmn, ControlFlow, Flowchart and Sequence.
    [InlineData("Elsa.Activities.Bpmn.Runtime")]
    [InlineData("Elsa.Activities.ControlFlow.Runtime")]
    [InlineData("Elsa.Activities.Flowchart.Runtime")]
    [InlineData("Elsa.Activities.Sequence.Runtime")]
    // The pre-existing runtime halves of the same naming family.
    [InlineData("Elsa.Activities.DispatchWorkflow.Runtime")]
    [InlineData("Elsa.Activities.Graph.Runtime")]
    [InlineData("Elsa.Activities.Runtime")]
    [InlineData("Elsa.Activities.Runtime.Core")]
    // "Design" as part of a longer segment, not as a segment: a conformance suite, not a design assembly.
    [InlineData("Elsa.Persistence.Groundwork.DesignConformance")]
    [InlineData("Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests")]
    // The runtime spine the runtime-only compositions are actually built from.
    [InlineData("Elsa.Workflows.Runtime")]
    [InlineData("Elsa.Workflows.Runtime.Core")]
    [InlineData("Elsa.Workflows.Runtime.Reconciliation")]
    [InlineData("Elsa.Workflows.Runtime.Reconciliation.Core")]
    [InlineData("Elsa.Workflows.Runtime.Api")]
    [InlineData("Elsa.Serialization.SystemText")]
    [InlineData("Elsa.Primitives")]
    public void A_runtime_assembly_name_is_not_classified_as_design_time(string assemblyName) =>
        Assert.False(
            DesignTimeAssemblies.IsDesignOrPublishing(assemblyName),
            $"{assemblyName} belongs in a runtime-only closure; classifying it as design-time would fail honest compositions.");
}
