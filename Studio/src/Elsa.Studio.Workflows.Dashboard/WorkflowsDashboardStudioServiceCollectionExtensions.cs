using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Elsa.Studio.Workflows.Dashboard.Handlers;
using Microsoft.Extensions.DependencyInjection;
namespace Elsa.Studio.Workflows.Dashboard;
public static class WorkflowsDashboardStudioServiceCollectionExtensions { public static IServiceCollection AddWorkflowsDashboardStudio(this IServiceCollection services) => services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeWorkflowsDashboardStudioModule>(); }
