namespace Elsa.Activities.Design.Reconciliation.Clr.Exceptions;

/// <summary>
/// Raised (§2.23.5) when an activity carries no <c>[Version]</c> attribute and its declaring
/// assembly's version cannot be resolved to a valid SemVer 2.0.0 string (FR-020). Carries the
/// activity type and the assembly identity that failed to yield a version.
/// </summary>
public sealed class UnresolvableAssemblyVersionException(string activityTypeFullName, string assemblyName)
    : Exception($"Activity '{activityTypeFullName}' has no [Version] attribute and assembly '{assemblyName}' does not expose a resolvable SemVer 2.0.0 version.")
{
    public string ActivityTypeFullName { get; } = activityTypeFullName;

    public string AssemblyName { get; } = assemblyName;
}
