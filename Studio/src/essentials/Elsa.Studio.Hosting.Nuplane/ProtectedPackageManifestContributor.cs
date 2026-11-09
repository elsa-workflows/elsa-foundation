using System.Text.Json;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using global::Nuplane.Abstractions;
using global::Nuplane.Abstractions.PackageStoreProtection;

namespace Elsa.Studio.Hosting.Nuplane;

/// <summary>Adapts protected metadata bytes to the shared manifest parser and catalog mapper.</summary>
internal sealed class ProtectedPackageManifestContributor(PackageContentReader reader) : IFeatureCatalogContributor
{
    public async Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
    {
        var packages = await reader.GetPackagesAsync(cancellationToken);
        foreach (var package in packages.Packages)
        {
            var manifest = await ReadAsync(package, cancellationToken);
            if (manifest.Manifest is not null)
                PackageManifestCatalogMapper.Apply(context, manifest.Manifest, manifest.Path, manifest.Hash, package.PackageId, package.Version);
            else
            {
                var error = context.GetOrAdd($"{package.PackageId}:{package.Version}");
                error.SourceKind = FeatureSourceKinds.ManifestError;
                error.DisplayName = package.PackageId;
                error.PackageId = package.PackageId;
                error.PackageVersion = package.Version;
                error.ReadError = manifest.ReadError;
            }
        }
    }

    private async Task<PackageManifestReadResult> ReadAsync(ActivePackage package, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = await reader.OpenAsync(package, cancellationToken);
            foreach (var path in new[] { "elsa-package.json", "build/elsa-package.json" })
                if (scope.Read(path, PackageContentReader.MaximumMetadataBytes, cancellationToken) is { } content)
                    return PackageManifestCatalogMapper.ReadManifestBytes(content.Bytes, path);
            return PackageManifestReadResult.Missing("Package manifest not found.");
        }
        catch (PackageStoreAdmissionException error)
        {
            return PackageManifestReadResult.Missing($"Protected package metadata is unavailable ({error.Reason}).");
        }
        catch (PackageContentSizeException error) { return PackageManifestReadResult.Missing(error.Message); }
        catch (PackageContentProfileException error) { return PackageManifestReadResult.Missing(error.Message); }
        catch (JsonException) { return PackageManifestReadResult.Missing("Package manifest is not valid JSON."); }
        catch (IOException) { return PackageManifestReadResult.Missing("Protected package metadata could not be read."); }
        catch (UnauthorizedAccessException) { return PackageManifestReadResult.Missing("Protected package metadata access was refused."); }
    }
}
