namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that every successful completion of the activity selects at least one of its outcomes, so its outcome set
/// needs no <c>Done</c> (the <c>Done</c> rule, ADR 0045 §6; spec 1208 FR-002b). Publication reports a node whose pinned
/// outcome set has no <c>Done</c> and whose activity carries no such guarantee (<c>VF-AUTH-013</c>); an activity with
/// <see cref="ActivityValueOutcomesAttribute.SelectsOnEveryCompletion"/> carries it once items are configured. An
/// activity that declares <c>Done</c> must not carry it: <c>Done</c> is never selected together with a branch.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SelectsOutcomeOnEveryCompletionAttribute : Attribute;
