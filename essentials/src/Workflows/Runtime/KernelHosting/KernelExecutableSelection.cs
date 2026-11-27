using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Native;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal sealed record KernelExecutableSelection(string Workflow, string Definition, WorkflowExecutableIdentity? Executable, WorkflowPublishedStart? PublishedStart)
{
    internal static KernelExecutableSelection? Read(string type, string typeData)
    {
        try
        {
            if (WorkflowActor.DescribeStart(type, typeData) is not { } start ||
                start.Model != NativeExecutionModel.ModelKey || string.IsNullOrWhiteSpace(start.Workflow) ||
                string.IsNullOrWhiteSpace(start.Definition)) return null;
            if (start.ExecutableSelection is null)
                return start.PublishedStart is null ? new(start.Workflow, start.Definition, null, null) : null;
            // The material keeps its original source fields when another logical definition publishes it.
            return NativeExecutableMapper.ReadMaterialSelection(start.ExecutableSelection, start.Workflow) is { } identity
                ? new(start.Workflow, start.Definition, identity, start.PublishedStart) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
