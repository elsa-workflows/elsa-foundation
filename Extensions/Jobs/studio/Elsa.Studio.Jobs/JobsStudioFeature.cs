using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Jobs;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Jobs")]
[StudioModule("jobs", "Jobs", "navigation", "routes", "http")]
[ShellFeature(name: "JobsStudio", DisplayName = "Jobs", Description = "Inspect durable jobs, results, schedules and operational holds.")]
public sealed class JobsStudioFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) { }
}
