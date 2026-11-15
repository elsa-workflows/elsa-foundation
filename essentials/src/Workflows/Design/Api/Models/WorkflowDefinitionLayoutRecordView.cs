using System.Text.Json;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDefinitionLayoutRecordView(
    string NodeId,
    double X,
    double Y,
    double? Width,
    double? Height,
    JsonElement? AdditionalProperties
)
{
    public static WorkflowDefinitionLayoutRecordView From(IDesignMetadataRecord record) =>
        new(record.NodeId, record.X, record.Y, record.Width, record.Height, record.AdditionalProperties);
}
