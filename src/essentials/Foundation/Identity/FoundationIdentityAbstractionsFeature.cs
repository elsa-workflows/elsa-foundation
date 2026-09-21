using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Identity")]
[ManifestFeatureCategory("Security")]
[ShellFeature(
    name: "FoundationIdentityAbstractions",
    DisplayName = "Foundation Identity Abstractions",
    Description = "Registers provider-agnostic authentication, IAM, authorization, and ownership contracts."
)]
public class FoundationIdentityAbstractionsFeature : IShellFeature, IMiddlewareShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddFoundationIdentityAbstractions();

        // UseMiddleware below needs the ASP.NET Core marker services. AddFoundationIdentityAbstractions
        // calls AddAuthorizationCore, which registers the policy engine but not the marker UseAuthorization
        // asserts on — without these the shell fails to compose its pipeline and answers 503 to everything.
        // Both use TryAdd semantics, so a provider feature that also calls them is unaffected.
        services.AddAuthentication();
        services.AddAuthorization();
    }

    /// <summary>Only middleware contributor in the shell today, so the value is not ordered against anything.</summary>
    public int Order => 0;

    /// <summary>
    /// Installs authentication and authorization into the shell pipeline. This belongs to the feature that
    /// registers the authn/authz contracts rather than to the host: Elsa.Foundation.Host composes no feature,
    /// so a host-level UseAuthorization() would hardcode an assumption the host cannot know. It also belongs
    /// here rather than on the identity API feature — a shell can enable this substrate plus a provider and
    /// serve authorized endpoints without ever mapping the login endpoints. Running inside the shell pipeline
    /// is a better position than the host-level equivalent too: it does not depend on ShellMiddleware having
    /// already swapped HttpContext.RequestServices, the way an after-MapShells registration does.
    ///
    /// Without it, any endpoint carrying authorization metadata fails the request with "Endpoint ... contains
    /// authorization metadata, but a middleware was not found that supports authorization".
    /// </summary>
    public void UseMiddleware(IApplicationBuilder app, IHostEnvironment? environment)
    {
        app.UseAuthentication();
        app.UseAuthorization();
    }
}
