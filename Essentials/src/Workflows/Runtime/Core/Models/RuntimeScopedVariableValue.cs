namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A container-scoped variable's current value, captured as completed-scope inspection evidence
/// (ADR 0027, #210) when its owning container execution completes.
/// </summary>
/// <param name="Name">The variable's display name.</param>
/// <param name="ReferenceKey">The variable's stable reference key within its declaring scope.</param>
/// <param name="Value">The current value — the assigned value where present, otherwise the declared default.</param>
/// <param name="Withheld">
/// What stands in for the value when it was withheld. <paramref name="Value"/> is then null and stands for nothing:
/// evidence renders the marker instead of a value.
/// </param>
public sealed record RuntimeScopedVariableValue(string Name, string ReferenceKey, object? Value, WithheldValue? Withheld = null);
