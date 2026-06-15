using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Modularity.Api.Constants;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Modularity.Api.Endpoints;

internal sealed class Apply(IFeatureManagementService featureManagementService, ILogger<Apply> logger)
    : ElsaEndpoint<FeatureApplyRequest, FeatureApplyResult>
{
    public override void Configure()
    {
        Post(RouteConstants.GetRoute("apply"));
        ConfigurePermissions();
    }

    public override async Task HandleAsync(FeatureApplyRequest req, CancellationToken ct)
    {
        try
        {
            var result = await featureManagementService.ApplyAsync(req, ct);
            await Send.OkAsync(result, ct);
        }
        catch (FeatureCatalogRevisionConflictException e)
        {
            ThrowError(e, 409);
        }
        catch (ArgumentException e)
        {
            ThrowError(e, 400);
        }
        catch (InvalidOperationException e)
        {
            ThrowError(e, 400);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unexpected error occurred when applying feature management changes.");
            ThrowError("Unexpected error occurred", 500);
        }
    }
}
