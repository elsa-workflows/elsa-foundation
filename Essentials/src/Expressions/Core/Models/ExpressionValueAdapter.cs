namespace Elsa.Expressions.Core.Models;

/// <summary>
/// The value adapter version an expression language runs with (ADR 0046 V12, spec 1228 FR-028). A language feature
/// registers one as a singleton. Publication pins the version into each executable that uses the language, as part
/// of its hashed content; the runtime refuses an executable whose pin is missing or differs, and asks to republish it.
/// </summary>
/// <param name="Language">The expression language, compared case-insensitively, such as <c>JavaScript</c>.</param>
/// <param name="Version">The adapter version, such as <c>json-1</c>. A change to an adapter rule is a new version.</param>
public sealed record ExpressionValueAdapter(string Language, string Version);

/// <summary>
/// Declares that an activity type runs a script body in <paramref name="Language"/>, so an executable with one needs that language's
/// value adapter pin even when no input is an expression (spec 1228 FR-028). Registered as a singleton by the feature
/// that provides the activity.
/// </summary>
public sealed record ScriptActivityLanguage(string ActivityType, string Language);
