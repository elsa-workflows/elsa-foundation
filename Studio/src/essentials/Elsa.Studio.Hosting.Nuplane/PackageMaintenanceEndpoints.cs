using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Studio.Api.Authorization;
using global::Nuplane.Admin;

namespace Elsa.Studio.Hosting.Nuplane;

internal static class PackageMaintenanceEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/_elsa/module-management");
        group.MapPost("/prune", PruneAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
        group.MapPut("/retention-policy", SaveRetentionAsync).RequireAuthorization(StudioBridgeAuthorization.ModuleManagementManagePolicyName);
    }

    private static async Task<IResult> PruneAsync(PruneRequest request, ManagementConfigurationStore configuration,
        INuplanePackageStoreMaintenanceOperations maintenance, CancellationToken cancellationToken)
    {
        var policy = configuration.ReadRetention();
        if (policy.Status != "Supported")
            return Results.Conflict(new { Error = "The legacy count/age cleanup policy is incompatible with native package-store maintenance. Select an inactive-version retention policy first.", Code = "legacy-retention-unsupported" });
        if (string.IsNullOrWhiteSpace(request.RootLabel))
            return Results.BadRequest(new { Error = "A configured package-store root label is required." });
        var retention = policy.KeepNewestInactiveVersionsPerPackage is { } count ? new PackageStoreRetentionPolicy(count) : null;
        // Only the caller supplies confirmation. Nuplane compares it to fresh root/epoch/policy/protection facts.
        try
        {
            var result = await maintenance.PruneAsync(new(request.RootLabel, retention,
                request.DryRun ? PackageStorePruneMode.Preview : PackageStorePruneMode.Execute, request.Confirmation), cancellationToken);
            return Results.Json(result, JsonOptions);
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new { Error = error.Message });
        }
    }

    private static async Task<IResult> SaveRetentionAsync(RetentionRequest request, ManagementConfigurationStore configuration, CancellationToken cancellationToken)
    {
        if (request.KeepNewestInactiveVersionsPerPackage < 0)
            return Results.BadRequest(new { Error = "The inactive-version count must be nonnegative or null to retain all." });
        try
        {
            await configuration.SaveRetentionAsync(request.KeepNewestInactiveVersionsPerPackage, cancellationToken);
            return Results.Ok(new { RequiresRestart = false, Message = "Retention policy saved for the next manual prune operation." });
        }
        catch (ConfigurationReadOnlyException error)
        {
            return Results.Conflict(new { Error = error.Message, Code = "configuration-read-only" });
        }
    }

    private sealed record RetentionRequest(int? KeepNewestInactiveVersionsPerPackage);
    private sealed record PruneRequest(bool DryRun = true, string RootLabel = "default", PackageStorePruneConfirmation? Confirmation = null);
}
