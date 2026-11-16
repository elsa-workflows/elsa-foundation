namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Filled-in output on a design-time canvas. Joins back to an <see cref="OutputDefinition"/> via
/// <see cref="ActivityArgumentState.ReferenceKey"/>.
/// </summary>
public sealed record OutputState(string ReferenceKey, ActivityArgumentValue Value)
    : ActivityArgumentState(ReferenceKey, Value);
