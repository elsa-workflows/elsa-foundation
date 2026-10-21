using System.Text.Json;

namespace Elsa3.Mapping.ActivityMap;

/// <summary>
/// The classification of one Elsa 3 member (input, output, port or serialized property) by a rule of the decision
/// record <c>specs/236-elsa3-activity-map/member-map.md</c>.
/// </summary>
/// <param name="Elsa3Name">The Elsa 3 member name, matched case-insensitively against the camelCase JSON property.</param>
/// <param name="Kind">What kind of member it is.</param>
/// <param name="Rule">How it converts.</param>
/// <param name="Elsa4Key">The exact Elsa 4 input or output key (ordinal), or the slot name for a child slot.</param>
/// <param name="Elsa3Default">The Elsa 3 effective default: emitted by <see cref="Elsa3MemberRuleKind.DefaultFlip"/>, compared by <see cref="Elsa3MemberRuleKind.RemovedInput"/>.</param>
/// <param name="Contract">The narrower Elsa 4 contract a <see cref="Elsa3MemberRuleKind.NarrowedContract"/> value must fit.</param>
/// <param name="Projection">The exact projection a lost output maps to, when one exists.</param>
/// <param name="BehaviourWarning">The <c>ELS3-BEHAVIOUR-*</c> code emitted when the member is bound (for a removed input: unless it equals the default).</param>
/// <param name="WhenBound">For a default flip: emit the default only when this other Elsa 3 member is bound.</param>
public sealed record Elsa3MemberRule(
    string Elsa3Name,
    Elsa3MemberKind Kind,
    Elsa3MemberRuleKind Rule,
    string? Elsa4Key = null,
    JsonElement? Elsa3Default = null,
    Elsa3ValueContract? Contract = null,
    string? Projection = null,
    string? BehaviourWarning = null,
    string? WhenBound = null);

/// <summary>The kind of an Elsa 3 member.</summary>
public enum Elsa3MemberKind
{
    Input,
    Output,
    Port,
    Property,
    CustomProperty,

    /// <summary>An Elsa 4 member with no Elsa 3 source; the Elsa 3 effective behaviour is emitted as its value (R2).</summary>
    Synthetic
}

/// <summary>The member-map rule a member converts by (R1–R9).</summary>
public enum Elsa3MemberRuleKind
{
    /// <summary>R1: same member, or a rename.</summary>
    Carry,

    /// <summary>R1: an Elsa 3 port carried into an Elsa 4 child slot.</summary>
    ChildSlot,

    /// <summary>R2: Elsa 4's default differs; the Elsa 3 effective value is emitted when the node leaves it unset.</summary>
    DefaultFlip,

    /// <summary>R3: no Elsa 4 counterpart; removed only when absent or equal to the default.</summary>
    RemovedInput,

    /// <summary>R4: output with no Elsa 4 counterpart; removed when nothing captures it.</summary>
    LostOutput,

    /// <summary>R5: converted only when the value provably fits the narrower contract.</summary>
    NarrowedContract,

    /// <summary>R7: structure generated deterministically by a node rewrite.</summary>
    Structure,

    /// <summary>R8: graph-level data applied by graph conversion (#2705).</summary>
    GraphLevel,

    /// <summary>R9: display only; removed without a diagnostic.</summary>
    DisplayOnly,

    /// <summary>R6 for a use: the member must equal the default (absent counts only when the default is null), else the use has no Elsa 4 counterpart.</summary>
    UseRestriction,

    /// <summary>A captured output whose Elsa 4 counterpart has different semantics: unconvertible when captured, removed otherwise.</summary>
    SemanticsChanged
}

/// <summary>The narrower Elsa 4 contract an R5 value must fit.</summary>
public enum Elsa3ValueContract
{
    LiteralOnly,
    StringOnly,
    JsonOnly,
    EnumNameToInt,
    SecretReferenceOnly,
    SingleHeaderValue,
    LiteralTimeSpanToString,

    /// <summary>An HTTP method Elsa 4 sends; PATCH and OPTIONS have no counterpart (R6).</summary>
    HttpMethodSupported,

    /// <summary>
    /// A WriteHttpResponse body (spec 256): text (a literal, or a value declared <c>String</c>) fits; any other value fits
    /// when the response content type is unset or JSON, so Elsa 4 sends JSON as Elsa 3 did. A script with a typed
    /// declaration is then refused by value-flow lowering (spec 256 FR-013).
    /// </summary>
    HttpResponseBody,

    /// <summary>A WriteHttpResponse content type (spec 256): an expression with a blank script is unset, as in Elsa 3.</summary>
    HttpResponseContentType,

    /// <summary>
    /// A RunJavaScript body (plan D-16): rewritten in script mode, its Elsa 3 reads bound through the activity's
    /// <c>arguments</c> input and its completion value returned.
    /// </summary>
    ScriptBody
}
