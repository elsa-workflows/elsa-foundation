namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Opts an activity type in to reading and writing workflow variables by free name, through
/// <c>context.Variables.Get&lt;T&gt;("name")</c> and <c>context.Variables.Set("name", value)</c> (ADR 0090 decision 4,
/// spec 1231 FR-025). A name resolves at run time to the nearest visible declaration; an undeclared name faults the step.
/// The marker is pinned into the activity contract and shown in the descriptor, so publication, inspection and Studio
/// can show that the activity may touch any visible variable: the concurrent-write rule counts it as touching every
/// visible variable, and a background placement is refused (ADR 0093 C9).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class AccessesUndeclaredVariablesAttribute : Attribute;
