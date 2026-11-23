namespace Elsa3.Activities.Design.Import.Contracts;

/// <summary>
/// Tells the analyzer which Elsa 3 activity types are triggers, so a reusable workflow is refused only for a real start
/// trigger (spec 246 D-6). On a non-trigger (a WriteLine, an If, a Flowchart, …) Elsa 3's <c>canStartWorkflow</c> only picks a
/// flowchart's start node, which conversion keeps (spec 248). The Elsa3Mapping feature implements it over the activity
/// map; without an implementation every flagged activity counts as a trigger.
/// </summary>
public interface IReusableActivityTriggerClassifier
{
    /// <summary>
    /// Whether <paramref name="elsa3ActivityType"/> is an Elsa 3 trigger: true or false for a type the classifier knows,
    /// null for one it does not (a custom activity), which the analyzer treats as a trigger.
    /// </summary>
    bool? IsTrigger(string elsa3ActivityType);
}
