using System.Reflection;

namespace Elsa.Persistence.Groundwork.Testing;

internal static class GroundworkProviderDriverSupport
{
    public static string PackageVersion(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var packageVersion = informationalVersion?.Split('+', 2)[0];
        return !string.IsNullOrWhiteSpace(packageVersion)
            ? packageVersion
            : throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' does not declare an informational package version.");
    }
}
