using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.UserTasks;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.UserTasks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MinimalEndpoints;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>Serves the retained task journal and reusable task forms on a kernel shell.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("API")]
[ShellFeature(
    name: "WorkflowsRuntimeKernelUserTasksApi",
    DisplayName = "Workflows Runtime User Tasks and Forms API",
    Description = "Views the task inbox, edits reusable task forms, and serves actions on actor-owned tasks when task execution is selected.",
    DependsOn = new object[] { WorkflowsRuntimeKernelRetainedApiFeature.FeatureName, "WorkflowsRuntimeUserTaskArchiveEntityFrameworkCore" })]
public sealed class WorkflowsRuntimeKernelUserTasksApiFeature : IWebShellFeature, IPostConfigureShellServices
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<UserTaskOptions>();
        services.TryAddSingleton<UserTaskCursorCodec>();
        services.TryAddScoped<IUserTaskAccess, TaskRequestAccess>();
        services.TryAddScoped<UserTaskPeople>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserTaskFormProvider, BasicUserTaskForms>());
        services.AddScoped<IUserTaskOperations>(provider => new UserTaskOperations(
            provider.GetRequiredService<IUserTaskStore>(),
            provider.GetRequiredService<IUserTaskAccess>(),
            provider.GetServices<IUserTaskFormProvider>(),
            provider.GetRequiredService<TimeProvider>(),
            null,
            provider.GetRequiredService<UserTaskCursorCodec>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            actorRuntime: provider.GetService<IActorUserTaskRuntime>()));
        services.AddPermissionContributor<UserTaskArchivePermissions>();
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Count(descriptor => descriptor.ServiceType == typeof(IUserTaskStore) && !descriptor.IsKeyedService) != 1)
            throw new InvalidOperationException("The user task archive API requires a single task persistence owner.");

        var participantOwners = services.Count(descriptor =>
            descriptor.ServiceType == typeof(IActorUserTaskRuntime) && !descriptor.IsKeyedService);
        if (participantOwners > 1)
            throw new InvalidOperationException("The user task API requires a single actor task execution owner.");
        var participantActions = participantOwners == 1;
        if (participantActions)
            services.AddPermissionContributor<UserTaskParticipantPermissions>();

        ApiCapabilityLink[] links = participantActions
            ? [.. UserTaskHttpApi.ArchiveLinks, .. UserTaskHttpApi.ParticipantLinks]
            : UserTaskHttpApi.ArchiveLinks;
        services.AddApiCapability(new ApiCapabilityDeclaration("elsa.api.user-tasks", 1, links, "WorkflowsRuntimeKernelUserTasksApi"));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        var api = endpoints.MapEndpointGroup(RuntimeHttpApi.OwnerId, UserTaskJsonContext.Default, jsonContentType: "application/json; charset=utf-8");
        var available = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>();
        var participantActions = available.IsService(typeof(IActorUserTaskRuntime));
        UserTaskHttpApi.MapEndpoints(api, includeAssignmentTargets: participantActions, includeParticipantActions: participantActions);
    }

    private sealed class UserTaskParticipantPermissions : IPermissionContributor
    {
        public IEnumerable<Permission> Contribute() =>
        [
            new(UserTaskPermissions.Act, "Act on my tasks", "User Tasks", "Claim, release and respond to eligible actor-owned user tasks.")
        ];
    }

    private sealed class UserTaskArchivePermissions : IPermissionContributor
    {
        public IEnumerable<Permission> Contribute() => UserTaskHttpApi.ReadAndFormPermissions();
    }
}
