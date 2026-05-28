namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Filled-in output on a design-time canvas. Joins back to an <see cref="OutputDefinition"/> via
/// <see cref="ArgumentState.ReferenceKey"/>.
/// </summary>
public sealed record OutputState(string ReferenceKey, ArgumentValue Value)
    : ArgumentState(ReferenceKey, Value);
