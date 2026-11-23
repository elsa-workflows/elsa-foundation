using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record ActivityPresentationRecordView(
    string NodeId,
    string? DisplayName = null,
    string? Description = null)
{
    public ActivityPresentationRecord ToRecord() =>
        new(NodeId, DisplayName, Description);

    public static ActivityPresentationRecordView From(IActivityPresentationRecord record) =>
        new(record.NodeId, record.DisplayName, record.Description);
}
