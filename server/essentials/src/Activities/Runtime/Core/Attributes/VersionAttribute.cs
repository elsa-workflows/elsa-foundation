namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Optionally annotates a CLR activity with an author-controlled SemVer 2.0.0 version.
/// When present, the assembly reconciliation source uses this value verbatim; when absent,
/// the source falls back to the declaring assembly's version.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class VersionAttribute(string version) : Attribute
{
    /// <summary>The author-declared SemVer 2.0.0 version string.</summary>
    public string Version { get; } = version;
}
