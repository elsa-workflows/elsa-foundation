using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Design.Tests.ClrFixture;

public abstract class FixtureActivity : Activity<ActivityUnit>
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
}

/// <summary>
/// An activity with no <c>[Version]</c> attribute. The scanner must fall back to the declaring
/// assembly's version (pinned to <c>2.1.0</c> in the fixture csproj). Carries a required input to
/// exercise the scanner's <c>[Required]</c> → <c>IsRequired</c> mapping.
/// </summary>
public sealed class UnannotatedFixtureActivity : Activity<UnannotatedFixtureResult>
{
    [ActivityInput]
    [Required]
    public string Message { get; set; } = null!;

    protected override ValueTask<ActivityTransition<UnannotatedFixtureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new UnannotatedFixtureResult(Message)));
}

public sealed record UnannotatedFixtureResult([property: Output] string Result);

/// <summary>An activity that uses the C# required-member syntax instead of Elsa's marker attribute.</summary>
public sealed class RequiredKeywordFixtureActivity : FixtureActivity
{
    [ActivityInput]
    public required string Message { get; set; }

    [ActivityInput]
    public string? OptionalNote { get; set; }

    [ActivityInput]
    [Required]
    public string? RequiredNullableNote { get; set; }

    [ActivityInput]
    public int? OptionalCount { get; set; }
}

/// <summary>
/// An activity whose <c>[Version]</c> attribute overrides the assembly version — the scanner must
/// record <c>3.0.0</c>, not the assembly's <c>2.1.0</c>.
/// </summary>
[Version("3.0.0")]
public sealed class VersionedFixtureActivity : FixtureActivity;

/// <summary>
/// A base activity carrying a class-level <c>[Version]</c>. A reflection-only scan honours it only if
/// the resolver walks the base chain (issue #417 item 3); <see cref="InheritedVersionFixtureActivity"/>
/// declares no <c>[Version]</c> of its own and must inherit this value.
/// </summary>
[Version("4.0.0")]
public abstract class VersionedBaseActivity : FixtureActivity;

/// <summary>
/// An activity with no <c>[Version]</c> of its own; it must inherit <c>4.0.0</c> from
/// <see cref="VersionedBaseActivity"/> rather than falling back to the assembly's <c>2.1.0</c>.
/// </summary>
public sealed class InheritedVersionFixtureActivity : VersionedBaseActivity;

/// <summary>
/// A base activity declaring a <c>[Required]</c> input. <see cref="InheritsRequiredFixtureActivity"/>
/// re-declares this same property with <c>new</c> and no <c>[Required]</c> of its own, so the attribute
/// lives only on this base declaration — the scanner must walk the base-property chain to see it
/// (issue #417 item 3).
/// </summary>
public abstract class RequiredInputBaseActivity : FixtureActivity
{
    [Required]
    [ActivityInput(Order = 42, Category = "Advanced", DefaultValue = "inherited-default", DefaultSyntax = "Literal")]
    public virtual string InheritedRequired { get; set; } = null!;
}

/// <summary>
/// An activity that re-declares its input with <c>new</c> — deliberately WITHOUT re-applying
/// <c>[Required]</c>. The scanner reads this attribute-less derived declaration first; only a
/// base-property-chain walk finds the <c>[Required]</c> on <see cref="RequiredInputBaseActivity"/>
/// and maps <c>IsRequired</c> to <see langword="true"/> (issue #417 item 3).
/// </summary>
public sealed class InheritsRequiredFixtureActivity : RequiredInputBaseActivity
{
    public new string InheritedRequired { get; set; } = null!;
}

/// <summary>An enum used as a complex (non-primitive) activity input value type.</summary>
public enum FixtureMode
{
    Off,
    On,
    Auto
}

/// <summary>A complex (non-primitive) reference type used as an activity input value type.</summary>
public sealed class FixturePayload
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// An activity carrying a complex-typed AND an enum-typed input (plus a primitive control). Exercises the
/// alias-only end-to-end seam: the reflection-only scanner emits <c>CanonicalAlias</c> (the dotted
/// <c>FullName</c>) for these non-primitive element types, and the runtime registration pass registers those
/// same aliases so they resolve back to the real CLR type instead of <c>object</c> (FR-004b).
/// </summary>
public sealed class ComplexInputFixtureActivity : FixtureActivity
{
    [ActivityInput(Order = 20)]
    public FixturePayload Payload { get; set; } = null!;

    [ActivityInput(Order = 10, Category = "Simple", DefaultValue = "Auto", DefaultSyntax = "Literal")]
    public FixtureMode Mode { get; set; }

    [ActivityInput(Order = 30)]
    public string Label { get; set; } = null!;

    [ActivityInput(Order = 40, DefaultValue = "1", DefaultSyntax = "Literal")]
    public int Count { get; set; }
}

/// <summary>
/// Exercises display-name derivation (issue #928): a multi-word input with no authored display name must be
/// humanized (<c>ExpectedStatusCodes</c> → <c>Expected Status Codes</c>), while an explicit
/// <c>[ActivityInput(DisplayName = …)]</c> is respected verbatim. The typed result carries the same rule for outputs.
/// </summary>
public sealed class DisplayNameFixtureActivity : Activity<DisplayNameFixtureResult>
{
    [ActivityInput]
    public int[] ExpectedStatusCodes { get; set; } = [];

    [ActivityInput(DisplayName = "Custom Label", Description = "A hand-authored description.")]
    public string ContentType { get; set; } = null!;

    protected override ValueTask<ActivityTransition<DisplayNameFixtureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new DisplayNameFixtureResult(0)));
}

public sealed record DisplayNameFixtureResult([property: Output] int ResponseStatusCode);

/// <summary>Valid option declarations used by the reflection-only scanner contract tests.</summary>
public sealed class InputOptionsFixtureActivity : FixtureActivity
{
    [ActivityInput(UIHint = "dropdown", Options = ["red", "green"])]
    public string Color { get; set; } = null!;

    [ActivityInput(UIHint = "dropdown")]
    [ActivityInputOption("Low", 1)]
    [ActivityInputOption("High", 10)]
    public int Priority { get; set; }

    [ActivityInput]
    [ActivityInputOption("Automatic", FixtureMode.Auto)]
    [ActivityInputOption("Disabled", FixtureMode.Off)]
    public FixtureMode Mode { get; set; }

    [ActivityInput]
    [ActivityInputOption("Enabled", true)]
    [ActivityInputOption("Disabled", false)]
    public bool Enabled { get; set; }

    [ActivityInput]
    [ActivityInputOption("Minimum JS-safe integer", -9007199254740991L)]
    [ActivityInputOption("Maximum JS-safe integer", 9007199254740991L)]
    public long JsSafeIntegerBoundary { get; set; }

    [ActivityInput(OptionsProvider = "fixture.fields", OptionsProviderDependencies = [nameof(Entity)])]
    public string Field { get; set; } = null!;

    [ActivityInput]
    public string Entity { get; set; } = null!;
}

public abstract class InheritedInputOptionsBaseActivity : FixtureActivity
{
    [ActivityInput(UIHint = "dropdown", Options = ["base-a", "base-b"])]
    public virtual string Choice { get; set; } = null!;
}

public sealed class InheritedInputOptionsFixtureActivity : InheritedInputOptionsBaseActivity
{
    public override string Choice { get; set; } = null!;
}

public abstract class ReplacedInputOptionsBaseActivity : FixtureActivity
{
    [ActivityInput(UIHint = "dropdown")]
    [ActivityInputOption("Base", 1)]
    public virtual int Choice { get; set; }
}

public sealed class ReplacedInputOptionsFixtureActivity : ReplacedInputOptionsBaseActivity
{
    [ActivityInputOption("Derived first", 2)]
    [ActivityInputOption("Derived second", 3)]
    public override int Choice { get; set; }
}

/// <summary>
/// A composite fixture whose child-structure attributes must be projected into design facets by the
/// reflection-only scanner.
/// </summary>
[ActivityStructure("fixture.structure", "1.0.0", Mode = "sequence", SupportsScopedVariables = true)]
[ActivityChildSlot("Fixture.Activities", "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityChildSlot("Fixture.Body", "body", "Body", ActivityChildSlotCardinalities.Single)]
public sealed class StructuredFixtureActivity : Activity<StructuredFixtureResult>
{
    protected override ValueTask<ActivityTransition<StructuredFixtureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new StructuredFixtureResult(string.Empty)));
}

public sealed record StructuredFixtureResult([property: Output(Key = "summary")] string Summary);

/// <summary>A trigger fixture that declares both its execution shape and stable catalog key.</summary>
[TriggerActivity]
public sealed class TriggerFixtureActivity : FixtureActivity
{
    public const string ActivityType = "Elsa.Fixture.Trigger";
}

/// <summary>
/// An activity declaring multiple <c>[ActivityOutcome]</c> attributes. The scanner must project these
/// into a design facet so the studio can render outcome ports.
/// </summary>
[ActivityOutcome("Done")]
[ActivityOutcome("Success")]
[ActivityOutcome("Failed")]
public sealed class OutcomeFixtureActivity : FixtureActivity;

/// <summary>
/// A base activity with an <c>[ActivityOutcome]</c> attribute to verify the scanner walks the type chain.
/// </summary>
[ActivityOutcome("Done")]
[ActivityOutcome("BaseOutcome")]
public abstract class OutcomeBaseActivity : FixtureActivity;

/// <summary>
/// A derived activity that adds its own outcomes on top of the base. The scanner must collect all of them.
/// </summary>
[ActivityOutcome("DerivedOutcome")]
public sealed class InheritedOutcomeFixtureActivity : OutcomeBaseActivity;

/// <summary>A typed activity used to verify stable plain CLR contract discovery keys.</summary>
public sealed class PlainFixtureActivity : Activity<PlainFixtureResult>
{
    [ActivityInput(Key = "message")]
    [Required]
    public string Message { get; set; } = null!;

    protected override ValueTask<ActivityTransition<PlainFixtureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new PlainFixtureResult(Message.Length)));
}

public sealed record PlainFixtureResult(
    [property: Output(Key = "length", Path = "length")] int Length);
