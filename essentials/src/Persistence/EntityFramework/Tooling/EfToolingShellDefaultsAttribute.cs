namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>Declares the one host-owned shell-default composer used by runtime and EF tooling.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class EfToolingShellDefaultsAttribute(Type composerType) : Attribute
{
    public Type ComposerType { get; } = composerType;
}
