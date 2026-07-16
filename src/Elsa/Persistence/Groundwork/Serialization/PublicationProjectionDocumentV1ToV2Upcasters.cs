using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>Adds the neutral authored-input source collection to existing source references.</summary>
public sealed class WorkflowExecutableSourceReferenceDocumentV2ToV3Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind;
    public int FromVersion => 2;

    public JsonObject Upcast(JsonObject content)
    {
        var reference = content["reference"] as JsonObject
            ?? throw new InvalidOperationException("Legacy source-reference document has no 'reference' object.");
        reference["authoredInputs"] = new JsonArray();
        return content;
    }
}

/// <summary>Adds neutral publication provenance and provider-compatible cardinality to legacy bindings.</summary>
public sealed class WorkflowTriggerBindingDocumentV1ToV2Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    private const string LegacyHttpStimulusType = "HttpEndpoint";

    public string DocumentKind => ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind;
    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        var stimulusType = content["stimulusType"]?.GetValue<string>();
        content["publicationId"] = null;
        content["slotId"] = null;
        content["isActive"] = false;
        content["cardinality"] = (int)(string.Equals(stimulusType, LegacyHttpStimulusType, StringComparison.Ordinal)
            ? TriggerCardinality.Exclusive
            : TriggerCardinality.FanOut);
        return content;
    }
}

/// <summary>Adds neutral publication provenance to legacy recurring schedules.</summary>
public sealed class RecurringTriggerScheduleDocumentV1ToV2Upcaster : IGroundworkRuntimeDocumentUpcaster
{
    public string DocumentKind => ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind;
    public int FromVersion => 1;

    public JsonObject Upcast(JsonObject content)
    {
        var schedule = content["schedule"] as JsonObject
            ?? throw new InvalidOperationException("Legacy recurring-schedule document has no 'schedule' object.");
        schedule["publicationId"] = null;
        schedule["slotId"] = null;
        schedule["isActive"] = false;
        return content;
    }
}
