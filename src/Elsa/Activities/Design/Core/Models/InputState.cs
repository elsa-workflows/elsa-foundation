namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Filled-in input on a design-time canvas. Joins back to an <see cref="InputDefinition"/> via
/// <see cref="ActivityArgumentState.ReferenceKey"/>.
/// </summary>
public sealed record InputState(string ReferenceKey, ActivityArgumentValue Value)
    : ActivityArgumentState(ReferenceKey, Value);
