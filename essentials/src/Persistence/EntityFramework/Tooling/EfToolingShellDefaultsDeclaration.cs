using System.Reflection;

namespace Elsa.Persistence.EntityFramework.Tooling;

/// <summary>One declaration rule shared by live host enrollment and tooling composition.</summary>
public static class EfToolingShellDefaultsDeclaration
{
    /// <summary>Finds the selected host's one public composer, allowing absence only for a legacy probe.</summary>
    public static Type? ResolveComposerType(Assembly hostAssembly, bool required)
    {
        ArgumentNullException.ThrowIfNull(hostAssembly);
        var declarations = hostAssembly.GetCustomAttributes<EfToolingShellDefaultsAttribute>().ToArray();
        if (declarations.Length == 0 && !required)
            return null;
        if (declarations.Length != 1)
            throw new InvalidOperationException(
                "EF persistence resources require exactly one shell-default composer declaration on the explicit host assembly.");

        var composerType = declarations[0].ComposerType;
        if (!(composerType.IsPublic || composerType.IsNestedPublic) ||
            !typeof(IEfToolingShellDefaults).IsAssignableFrom(composerType) ||
            composerType.IsAbstract ||
            composerType.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException(
                "The declared EF shell-default composer must be a concrete implementation with a public parameterless constructor.");
        return composerType;
    }

    /// <summary>Creates a validated composer without exposing its constructor exception.</summary>
    public static IEfToolingShellDefaults Construct(Type composerType)
    {
        ArgumentNullException.ThrowIfNull(composerType);
        try
        {
            return (IEfToolingShellDefaults)Activator.CreateInstance(composerType)!;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("The declared EF shell-default composer could not be constructed.");
        }
    }
}
