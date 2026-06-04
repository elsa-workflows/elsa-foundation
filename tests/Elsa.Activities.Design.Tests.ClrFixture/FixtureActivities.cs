using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Abstractions;
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

    public ActivityOutput<string> Result { get; set; } = null!;
}

/// <summary>
/// An activity whose <c>[Version]</c> attribute overrides the assembly version — the scanner must
/// record <c>3.0.0</c>, not the assembly's <c>2.1.0</c>.
/// </summary>
[Version("3.0.0")]
public sealed class VersionedFixtureActivity : ActivityBase;
