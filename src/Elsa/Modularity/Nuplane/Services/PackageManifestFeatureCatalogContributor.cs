using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Nuplane;
using Nuplane.Admin;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class PackageManifestFeatureCatalogContributor(INuplaneAdminOperations nuplaneAdmin) : IFeatureCatalogContributor
{
    public async Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
    {
        var packages = await nuplaneAdmin.GetPackagesAsync(cancellationToken);
        foreach (var package in packages.Packages)
        {
            var manifest = ReadManifest(package.InstallPath);
            if (manifest.Manifest is null)
            {
                var error = context.GetOrAdd($"{package.PackageId}:{package.Version}");
                error.SourceKind = FeatureSourceKinds.ManifestError;
                error.DisplayName = package.PackageId;
                error.PackageId = package.PackageId;
                error.PackageVersion = package.Version;
                error.ReadError = manifest.ReadError ?? "Package manifest not found.";
                continue;
            }

            PackageManifestCatalogMapper.Apply(context, manifest.Manifest, manifest.Path, manifest.Hash, package.PackageId, package.Version);
        }
    }

    internal static PackageManifestReadResult ReadManifest(string installPath)
    {
        // Nuplane owns the on-disk package layout (extracted dir vs .nupkg, root vs build/); we only choose
        // which relative manifest paths to try.
        var path = "elsa-package.json";
        var bytes = PackageContent.TryReadFile(installPath, path);
        if (bytes is null)
        {
            path = "build/elsa-package.json";
            bytes = PackageContent.TryReadFile(installPath, path);
        }

        if (bytes is null)
            return PackageManifestReadResult.Missing("Package manifest not found.");

        try
        {
            return PackageManifestCatalogMapper.ReadManifestBytes(bytes, path);
        }
        catch (JsonException ex)
        {
            return PackageManifestReadResult.Missing(ex.Message);
        }
    }
}
