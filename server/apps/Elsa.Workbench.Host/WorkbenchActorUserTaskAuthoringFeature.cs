using System.Text.Json;
using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CreateUserTask = Elsa.Activities.Primitives.Activities.CreateUserTask;

namespace Elsa.Workbench;

[ShellFeature]
public sealed class WorkbenchActorUserTaskAuthoringFeature : IShellFeature, IPostConfigureShellServices
{
    public void ConfigureServices(IServiceCollection services) { }

    public void PostConfigureServices(IServiceCollection services)
    {
        if (services.Any(service => !service.IsKeyedService && service.ServiceType == typeof(IActorUserTaskRuntime)))
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityInputSourceConstraintProvider, TaskInputConstraint>());
    }

    private sealed class TaskInputConstraint : IActivityInputSourceConstraintProvider
    {
        private const string Refusal = "actor-user-task.literal-required";
        private const string Guidance = "Enter a plain value for this Actor user task field.";

        public ValueTask<ActivityDataInputConstraint?> GetConstraintAsync(IActivityDefinitionVersion activityVersion,
            string inputKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTask(activityVersion)) return ValueTask.FromResult<ActivityDataInputConstraint?>(null);

            ActivityDataInputConstraint? constraint = inputKey switch
            {
                nameof(CreateUserTask.Actions) or nameof(CreateUserTask.OptionalFormActions) or nameof(CreateUserTask.Prefill) =>
                    new("Actor User Tasks", ["Literal", "Object"], Refusal, Guidance),
                nameof(CreateUserTask.Title) or nameof(CreateUserTask.Description) or nameof(CreateUserTask.AssigneeId) or
                    nameof(CreateUserTask.GroupId) or nameof(CreateUserTask.FormId) or nameof(CreateUserTask.FormVersion) =>
                    new("Actor User Tasks", ["Literal"], Refusal, Guidance),
                _ => null
            };
            return ValueTask.FromResult(constraint);
        }

        private static bool IsTask(IActivityDefinitionVersion version)
        {
            static bool Matches(string? type) => type == CreateUserTask.ActivityType || type == typeof(CreateUserTask).FullName;

            try
            {
                if (Matches(version.Definition.ActivityTypeKey)) return true;
            }
            catch (ArgumentNullException)
            {
                // Projected catalog versions can omit the definition navigation.
            }

            return version.DescriptorPayload is { ValueKind: JsonValueKind.Object } payload &&
                payload.EnumerateObject().Any(property => property.Name.Equals("typeAlias", StringComparison.OrdinalIgnoreCase) &&
                    property.Value is { ValueKind: JsonValueKind.String } alias && Matches(alias.GetString()));
        }
    }
}
