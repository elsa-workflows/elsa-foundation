using CShells.Features;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Notes.Activities;

/// <summary>Contributes the <see cref="AddNote"/> activity to the workflow designer's catalog and runtime.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ManifestFeatureCategory("Activities")]
[ShellFeature(
    name: "NotesActivities",
    DisplayName = "Notes activities",
    Description = "The \"Add note\" workflow activity, which writes a note through the Notes module.",
    DependsOn = new object[] { NotesActivitiesRelease.RequiredFeature })]
public sealed class NotesActivitiesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IActivityReconciliationSource, NotesActivityReconciliationSource>();
}
