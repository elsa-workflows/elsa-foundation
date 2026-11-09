using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.UserTasks;

[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("User Tasks")]
[StudioModule("user-tasks", "User Tasks", "navigation", "routes", "http")]
[ShellFeature(name: "UserTasksStudio", DisplayName = "User Tasks", Description = "Human work inbox, reusable forms and task authoring.")]
public class UserTasksStudioFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) { }
}
