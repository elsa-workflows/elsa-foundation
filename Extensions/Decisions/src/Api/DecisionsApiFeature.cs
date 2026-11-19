using System.Security.Claims;
using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Decisions.Design;
using Elsa.Decisions.Core;
using Elsa.Decisions.Persistence.Core;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Elsa.Decisions.Api;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Decisions")]
[ManifestFeatureCategory("API")]
[ShellFeature(name: "DecisionsApi", DisplayName = "Decisions API", Description = "Authorized decision authoring, testing and evaluation API.", DependsOn = new object[] { "DecisionsDesign", typeof(ApiCapabilitiesFeature) })]
public class DecisionsApiFeature : IWebShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaEndpoints(); services.AddDecisionsDesign(); services.AddHttpContextAccessor();
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.decisions", 1,
            [new("decisions", "decisions")], "DecisionsApi"));
        services.AddPermissionContributor<DecisionsPermissionContributor>();
        services.UseDecisionsAccessPolicy<HttpDecisionAccessPolicy>();
    }
    public virtual void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) => endpoints.MapDecisionsApi();
}

internal sealed class HttpDecisionAccessPolicy(IHttpContextAccessor context, IPermissionAuthorizationService authorization, NormalizedPrincipalValidator validator) : IDecisionAccessPolicy
{
    public async ValueTask<bool> IsAllowedAsync(DecisionCaller caller, string permission, object? resource = null, CancellationToken cancellationToken = default)
    {
        var principal = context.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || DecisionsApi.Caller(principal, validator) != caller) return false;
        var decision = await authorization.AuthorizeAsync(new PermissionEvaluationContext(principal, permission, caller.TenantId, resource), cancellationToken);
        return decision.Succeeded;
    }
}

internal sealed class DecisionsPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Decisions.Api";
    public IEnumerable<Permission> Contribute() => new[] { DecisionPermissions.Read, DecisionPermissions.Manage, DecisionPermissions.Publish, DecisionPermissions.Evaluate, DecisionPermissions.Test, DecisionPermissions.History, DecisionPermissions.Source, DecisionPermissions.SensitiveValues }
        .Select(name => new Permission(name, "Decisions " + name, "Decisions", "Allows the explicit decision " + name + " operation.") { OwnerId = OwnerId });
}
