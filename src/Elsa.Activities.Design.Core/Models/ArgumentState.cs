namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Base record for filled-in argument state on a design-time canvas. <see cref="ReferenceKey"/>
/// joins back to the matching <see cref="ArgumentDefinition.ReferenceKey"/>; <see cref="Value"/>
/// carries the literal-or-expression value supplied by the canvas author.
/// </summary>
public record ArgumentState(string ReferenceKey, ArgumentValue Value);
