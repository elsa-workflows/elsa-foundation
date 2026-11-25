using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Services;

/// <summary>Review identity for presentation changes; never an executable or provider hash.</summary>
public static class ActivityNodePresentationHasher
{
    public static string Compute(IEnumerable<ActivityNodePresentationRecord>? records) =>
        ActivityLayoutHasher.Compute(ActivityNodePresentationRecord.NormalizeCollection(records).Select(record =>
            new ActivityLayoutRecord(record.NodeId, Elsa.Serialization.Core.JsonHashInput.SerializeToElement(new { record.DisplayName, record.Description }))));
}
