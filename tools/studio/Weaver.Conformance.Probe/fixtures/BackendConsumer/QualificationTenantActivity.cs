using CShells.Features;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Test-only qualification seam (spec 1193, story 4). It is not product code and never ships.
// The driver selects this feature only for the workflow-truth-seams inventory, through the ordinary native
// feature apply, and deselects it afterwards. The consumer's catalog is CLR-reconciled and tenant-agnostic, so it
// has no activity that only one tenant sees. This seam adds exactly one to the designer's own catalog read
// (IActivityAuthoringCatalogReader), for requests whose principal is in the configured Tenant. The designer's
// GET /design/activities/catalog and Weaver's IActivityAuthoringCatalog both go through that same read, so the
// driver can compare them per actor and tenant; the seam supplies no other service and changes no product code.
[ShellFeature(name: "QualificationTenantActivity", DependsOn = new object[] { "ActivitiesDesignApi" })]
public sealed class QualificationTenantActivityFeature : IShellFeature
{
    internal const string ActivityTypeKey = "Qualification.TenantOnlyActivity";
    internal const string ActivityVersionId = "qualification-tenant-only-activity-v1";

    public string Tenant { get; set; } = string.Empty;

    public void ConfigureServices(IServiceCollection services)
    {
        if (string.IsNullOrWhiteSpace(Tenant) || Tenant != Tenant.Trim())
            throw new InvalidOperationException("Set QualificationTenantActivity:Tenant to the one tenant that sees the qualification activity.");
        var tenant = Tenant;
        // Exactly one reader stays registered; it delegates to the host's actual designer read.
        services.RemoveAll<IActivityAuthoringCatalogReader>();
        services.AddScoped<IActivityAuthoringCatalogReader>(provider => new TenantActivityReader(
            provider.GetRequiredService<ActivityAuthoringCatalogReader>(), provider.GetRequiredService<IHttpContextAccessor>(), tenant));
    }
}

sealed class TenantActivityReader(IActivityAuthoringCatalogReader actual, IHttpContextAccessor accessor, string tenant) : IActivityAuthoringCatalogReader
{
    public async Task<ActivityAuthoringCatalogView> ListAsync(ListActivityAuthoringCatalog request, CancellationToken cancellationToken)
    {
        var view = await actual.ListAsync(request, cancellationToken);
        var current = accessor.HttpContext?.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        if (!string.Equals(current, tenant, StringComparison.Ordinal)) return view;
        var entry = new ActivityAuthoringDescriptorView(QualificationTenantActivityFeature.ActivityVersionId,
            QualificationTenantActivityFeature.ActivityTypeKey, "1.0.0", "Qualification tenant-only activity", "Qualification",
            "Visible only in the qualification tenant.", "Activity", true, null, [], [], [], null,
            new("activity", QualificationTenantActivityFeature.ActivityVersionId, new Dictionary<string, ActivityArgumentValue>(),
                new Dictionary<string, ActivityArgumentValue>(), null));
        // First, so a bounded projection that truncates still shows it.
        return new ActivityAuthoringCatalogView([entry, .. view.Activities]);
    }
}
