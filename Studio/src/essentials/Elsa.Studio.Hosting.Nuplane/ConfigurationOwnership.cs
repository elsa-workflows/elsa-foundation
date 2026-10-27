using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace Elsa.Studio.Hosting.Nuplane;

internal static class ConfigurationOwnership
{
    public static bool IsExclusive(IConfigurationRoot root, string file, string prefix)
    {
        var providers = root.Providers.ToArray();
        var managed = providers.OfType<JsonConfigurationProvider>().Where(provider => SameFile(provider, file)).ToArray();
        if (managed.Length != 1) return false;
        return providers.Where(provider => !ReferenceEquals(provider, managed[0]))
            .All(provider => !provider.TryGet(prefix, out _) && !provider.GetChildKeys([], prefix).Any());
    }

    private static bool SameFile(JsonConfigurationProvider provider, string file)
    {
        // A subclass can advertise this path while loading different data. Only the standard
        // file-backed provider/source combination proves which file supplies the configuration.
        if (provider.GetType() != typeof(JsonConfigurationProvider)
            || provider.Source.GetType() != typeof(JsonConfigurationSource))
            return false;
        // PhysicalFileProvider still identifies an optional source when the file has not been created yet.
        if (provider.Source.FileProvider is not PhysicalFileProvider physical || provider.Source.Path is not { } path)
            return false;
        if (physical.GetType() != typeof(PhysicalFileProvider))
            return false;
        return string.Equals(Path.GetFullPath(path, physical.Root), file,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

internal sealed class ConfigurationReadOnlyException(string message) : Exception(message);
