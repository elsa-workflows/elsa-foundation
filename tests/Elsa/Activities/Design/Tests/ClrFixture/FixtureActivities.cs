using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Design.Tests.ClrFixture;

/// <summary>
/// An activity with no <c>[Version]</c> attribute. The scanner must fall back to the declaring
/// assembly's version (pinned to <c>2.1.0</c> in the fixture csproj). Carries a required input to
/// exercise the scanner's <c>[Required]</c> → <c>IsRequired</c> mapping.
/// </summary>
public sealed class UnannotatedFixtureActivity : ActivityBase
{
    [Required]
    public InputArgument<string> Message { get; set; } = null!;

    public OutputArgument<string> Result { get; set; } = null!;
}

/// <summary>
/// An activity whose <c>[Version]</c> attribute overrides the assembly version — the scanner must
/// record <c>3.0.0</c>, not the assembly's <c>2.1.0</c>.
/// </summary>
[Version("3.0.0")]
public sealed class VersionedFixtureActivity : ActivityBase;

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
public sealed class ComplexInputFixtureActivity : ActivityBase
{
    public InputArgument<FixturePayload> Payload { get; set; } = null!;

    public InputArgument<FixtureMode> Mode { get; set; } = null!;

    public InputArgument<string> Label { get; set; } = null!;
}
