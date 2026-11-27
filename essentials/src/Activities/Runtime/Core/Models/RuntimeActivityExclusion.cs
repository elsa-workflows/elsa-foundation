namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>A named set of activity types the installed runtime cannot execute in this shell.</summary>
public sealed record RuntimeActivityExclusion(string SetName, IReadOnlyCollection<string> ActivityTypeKeys);
