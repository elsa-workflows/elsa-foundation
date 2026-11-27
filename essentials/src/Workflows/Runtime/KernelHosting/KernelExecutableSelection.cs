using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Kernel.Native;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed record KernelExecutableSelection(string Workflow, string Definition, WorkflowExecutableIdentity? Executable)
{
    internal static KernelExecutableSelection? Read(string type, string typeData)
    {
        try
        {
            if (WorkflowActor.DescribeStart(type, typeData) is not { } start ||
                start.Model != NativeExecutionModel.ModelKey || string.IsNullOrWhiteSpace(start.Workflow) ||
                string.IsNullOrWhiteSpace(start.Definition)) return null;
            if (start.ExecutableSelection is null) return new(start.Workflow, start.Definition, null);
            var identity = JsonSerializer.Deserialize<WorkflowExecutableIdentity>(start.ExecutableSelection);
            return identity is not null && identity.ArtifactId == start.Workflow && identity.DefinitionId == start.Definition &&
                !string.IsNullOrWhiteSpace(identity.ArtifactHash) && !string.IsNullOrWhiteSpace(identity.ArtifactVersion) &&
                !string.IsNullOrWhiteSpace(identity.DefinitionVersionId) ? new(start.Workflow, start.Definition, identity) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
