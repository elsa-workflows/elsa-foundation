namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Declares the host's reviewed explicit environment-input policy for candidate inspection.</summary>
/// <remarks>Inspection reads declaration metadata without constructing attributes and refuses duplicate declarations.</remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class EfCandidateEnvironmentInputsAttribute(int version, string policy) : Attribute
{
    public int Version { get; } = version;
    public string Policy { get; } = policy;
}
