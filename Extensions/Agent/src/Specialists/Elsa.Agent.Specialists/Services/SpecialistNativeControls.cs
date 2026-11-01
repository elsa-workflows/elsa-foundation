using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Agent.Specialists.Services;

internal static class SpecialistNativeControls
{
    internal static bool Enabled(SpecialistRuntimeDescriptor descriptor)
    {
        if (descriptor.InvocationControlsProfile is null) return false;
        if (descriptor.InvocationControlsProfile != SpecialistInvocationControls.Profile || !descriptor.AllowContinuity) throw Invalid();
        return true;
    }

    internal static void ValidateContract(SpecialistRuntimeDescriptor descriptor, ActivityContract contract)
    {
        if (!Enabled(descriptor)) return;
        if (!contract.Inputs.TryGetValue(SpecialistInvocationControls.InputKey, out var input) ||
            input.Name != SpecialistInvocationControls.InputName || !JsonType(input.Type) || input.IsRequired || !input.IsNullable ||
            input.HasDefault || input.IsCredential || input.ConnectionType is not null || input.IsStructureInput ||
            !contract.Result.Projections.TryGetValue(SpecialistInvocationControls.OutputKey, out var output) ||
            output.Path != SpecialistInvocationControls.OutputName || !JsonType(output.Type) || !output.IsRequired || output.SourceRepresentation != ValueRepresentation.StructuredValue ||
            contract.Inputs.Values.Any(x => x.Key != input.Key && (Reserved(x.Key) || StringComparer.OrdinalIgnoreCase.Equals(x.Name, input.Name))) ||
            contract.Result.Projections.Values.Any(x => x.Key != output.Key && (Reserved(x.Key) || OutputCollision(x.Path)))) throw Invalid();
        if (descriptor.InputSchema.ValueKind != JsonValueKind.Object || !descriptor.InputSchema.TryGetProperty("properties", out var inputProperties) || inputProperties.ValueKind != JsonValueKind.Object ||
            descriptor.ResultSchema.ValueKind != JsonValueKind.Object || !descriptor.ResultSchema.TryGetProperty("properties", out var outputProperties) || outputProperties.ValueKind != JsonValueKind.Object ||
            inputProperties.EnumerateObject().Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Name, input.Name)) || outputProperties.EnumerateObject().Any(x => OutputCollision(x.Name))) throw Invalid();
    }

    internal static SpecialistConversationAdmission? Read(SpecialistRuntimeDescriptor descriptor, ActivityContract contract, IReadOnlyDictionary<string, object?> inputs)
    {
        ValidateContract(descriptor, contract);
        if (!Enabled(descriptor) || !inputs.TryGetValue(SpecialistInvocationControls.InputKey, out var value)) return null;
        if (value is not JsonElement json) throw Invalid();
        if (json.ValueKind != JsonValueKind.Object || json.GetRawText().Length > 4096) throw Invalid();
        var properties = json.EnumerateObject().ToArray();
        if (properties.Length == 0) return null;
        if (properties.Length != 2 || properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
            !json.TryGetProperty("conversationId", out var id) || id.ValueKind != JsonValueKind.String ||
            !SpecialistTurnDocumentRules.IsToken(id.GetString()) || !json.TryGetProperty("revision", out var revision) ||
            revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out var expected) || expected is < 1 or long.MaxValue) throw Invalid();
        return new(id.GetString()!, expected);
    }

    internal static JsonElement Complete(SpecialistRuntimeDescriptor descriptor, SpecialistTurnResult accepted)
    {
        if (!Enabled(descriptor)) return accepted.Result;
        var session = accepted.Session ?? throw Invalid();
        var result = JsonNode.Parse(accepted.Result.GetRawText())!.AsObject();
        if (result.ContainsKey(SpecialistInvocationControls.OutputName)) throw Invalid();
        result.Add(SpecialistInvocationControls.OutputName, JsonSerializer.SerializeToNode(new { conversationId = session.ConversationId, revision = session.Revision }));
        return JsonSerializer.SerializeToElement(result);
    }

    private static bool JsonType(ValueTypeDescriptor type) => type.Alias == SpecialistInvocationControls.JsonType && type.CollectionKind == CollectionKind.Single && type.Schema is null && type.SchemaVersion is null;
    private static bool OutputCollision(string name) => StringComparer.OrdinalIgnoreCase.Equals(name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(), SpecialistInvocationControls.OutputName);
    private static bool Reserved(string key) => key.StartsWith("$specialist.", StringComparison.OrdinalIgnoreCase);
    private static SpecialistTurnException Invalid() => SpecialistModelRunner.Failure("specialist.controls.invalid", "Conversation controls require the exact published contract, conversation ID and positive expected revision.");
}
