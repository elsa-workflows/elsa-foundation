namespace Elsa.Activities.Design.Reconciliation.Clr.Exceptions;

/// <summary>
/// Raised (§2.23.5) when an activity's <c>[Version]</c> attribute carries a value that is not a
/// valid SemVer 2.0.0 string (FR-011). Carries the offending activity type and value so the
/// fault is actionable; no raw parse exception escapes the scanner.
/// </summary>
public sealed class InvalidActivityVersionException(string activityTypeFullName, string offendingValue)
    : Exception($"Activity '{activityTypeFullName}' declares an invalid SemVer 2.0.0 version: '{offendingValue}'.")
{
    public string ActivityTypeFullName { get; } = activityTypeFullName;

    public string OffendingValue { get; } = offendingValue;
}
