using System.Reflection;
using System.Runtime.Loader;
using Elsa.Studio.Api.Contracts;
using Elsa.Studio.Api.Services;
using global::Nuplane.Abstractions.PackageStoreProtection;
using global::Nuplane.PackageArchives;

namespace Elsa.Studio.Hosting.Nuplane;

internal sealed class PackageEntryStyles(PackageContentReader reader) : IStudioPackageEntryStyles
{
    public async ValueTask<string[]?> ReadAsync(Assembly assembly, string directory, CancellationToken cancellationToken)
    {
        if (AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default)
            return null;
        if (PackageContentReader.NormalizeRelativePath(directory) != directory)
            throw new InvalidDataException("Invalid package module directory.");

        await using var lease = await reader.TryAcquireResponseAsync(cancellationToken)
            ?? throw new StudioModuleDiscoveryBusyException();

        var prefix = $"staticwebassets/{directory}";
        try
        {
            var archive = await reader.OpenAssetAsync(assembly.GetName().Name!, cancellationToken, expectedAssembly: assembly);
            if (archive?.GetEntry($"{prefix}/entry-styles.json") is not { } contract)
                return null;

            var bytes = PackageContentReader.Read(contract, PackageContentReader.MaximumMetadataBytes, cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            return StudioModuleManifestProvider.ReadEntryStyles(stream, name => archive.GetEntry($"{prefix}/{name}") is not null);
        }
        catch (PackageStoreAdmissionException error)
        {
            throw new StudioModuleDiscoveryBusyException(error);
        }
        catch (Exception error) when (error is PackageArchiveException or PackageContentSizeException)
        {
            throw new InvalidDataException("The verified package stylesheet contract is unavailable.", error);
        }
    }
}
