using CShells.Features;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Test-only qualification seam (spec 1193, stories 2 and 3; T022). It is not product code and never ships.
// The driver selects this feature only for the workflow-batch inventory, through the ordinary native feature apply,
// and deselects it afterwards (and the workflow-explain inventory likewise). The consumer's CLR-reconciled catalog has no
// activity with a credential input, none with a password-hinted input and none the availability policy excludes, so this
// seam adds exactly three entries to the designer's own catalog read (IActivityAuthoringCatalogReader): one with a
// credential input, one with a password-hinted input the designer masks, and one listed but not addable. The designer's
// GET /design/activities/catalog and Weaver's IActivityAuthoringCatalog both go through that read, so a batch that
// writes a literal to the credential input, or adds the excluded activity, meets the owner's own catalog answer.
[ShellFeature(name: "QualificationBatchCatalog", DependsOn = new object[] { "ActivitiesDesignApi" })]
public sealed class QualificationBatchCatalogFeature : IShellFeature
{
    internal const string CredentialTypeKey = "Qualification.CredentialActivity";
    internal const string CredentialVersionId = "qualification-credential-activity-v1";
    internal const string PasswordTypeKey = "Qualification.PasswordActivity";
    internal const string PasswordVersionId = "qualification-password-activity-v1";
    internal const string ExcludedTypeKey = "Qualification.ExcludedActivity";
    internal const string ExcludedVersionId = "qualification-excluded-activity-v1";
    internal const string ExcludedReason = "Excluded by the qualification availability policy.";

    public void ConfigureServices(IServiceCollection services)
    {
        // Exactly one reader stays registered; it delegates to the host's actual designer read.
        services.RemoveAll<IActivityAuthoringCatalogReader>();
        services.AddScoped<IActivityAuthoringCatalogReader>(provider => new BatchCatalogReader(provider.GetRequiredService<ActivityAuthoringCatalogReader>()));
    }
}

sealed class BatchCatalogReader(IActivityAuthoringCatalogReader actual) : IActivityAuthoringCatalogReader
{
    public async Task<ActivityAuthoringCatalogView> ListAsync(ListActivityAuthoringCatalog request, CancellationToken cancellationToken)
    {
        var view = await actual.ListAsync(request, cancellationToken);
        var credential = Entry(QualificationBatchCatalogFeature.CredentialVersionId, QualificationBatchCatalogFeature.CredentialTypeKey,
            "Qualification credential activity", true, null,
            [new("apiKey", "ApiKey", "String", CollectionKind.Single, "API key", "Accepts only a Secret Reference.", 0, null, true, false, true,
                null, null, null, null, true, true)]);
        // Neither sensitive nor a credential: only its password hint makes the designer mask it (spec 1193 slice D).
        var password = Entry(QualificationBatchCatalogFeature.PasswordVersionId, QualificationBatchCatalogFeature.PasswordTypeKey,
            "Qualification password activity", true, null,
            // Named so that no probe redactor (password, apiKey, ...) rewrites it: a leak of its value must stay findable.
            [new("passphrase", "Passphrase", "String", CollectionKind.Single, "Qualification passphrase", "A password-hinted input the designer masks.", 0, null,
                true, false, true, "password", null, null, null, false, false)]);
        // A not-addable entry is listed only when the caller asks for every entry, as the designer's own filter does.
        var excluded = Entry(QualificationBatchCatalogFeature.ExcludedVersionId, QualificationBatchCatalogFeature.ExcludedTypeKey,
            "Qualification excluded activity", false, QualificationBatchCatalogFeature.ExcludedReason, []);
        // First, so a bounded projection that truncates still shows them.
        return new ActivityAuthoringCatalogView(request.Availability == ActivityCatalogAvailability.All
            ? [credential, password, excluded, .. view.Activities] : [credential, password, .. view.Activities]);
    }

    private static ActivityAuthoringDescriptorView Entry(string versionId, string typeKey, string displayName, bool available, string? reason,
        IReadOnlyCollection<ActivityInputDescriptorView> inputs) =>
        new(versionId, typeKey, "1.0.0", displayName, "Qualification", "A test-only qualification activity.", "Activity", available, reason,
            inputs, [], [], null, new("activity", versionId, new Dictionary<string, ActivityArgumentValue>(), new Dictionary<string, ActivityArgumentValue>(), null));
}
