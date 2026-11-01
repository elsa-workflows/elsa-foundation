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
        if (descriptor.InvocationControlsProfile is not (SpecialistInvocationControls.Profile or SpecialistInvocationControls.LegacyProfile) || !descriptor.AllowContinuity) throw Invalid();
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

    internal sealed record Invocation(SpecialistConversationAdmission? Continuation, SpecialistTurnLimits? Limits);

    internal static Invocation Read(SpecialistRuntimeDescriptor descriptor, ActivityContract contract, IReadOnlyDictionary<string, object?> inputs)
    {
        ValidateContract(descriptor, contract);
        if (!Enabled(descriptor) || !inputs.TryGetValue(SpecialistInvocationControls.InputKey, out var value)) return new(null, null);
        if (value is not JsonElement json || json.ValueKind != JsonValueKind.Object || json.GetRawText().Length > 4096) throw Invalid();
        var properties = json.EnumerateObject().ToArray();
        if (properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length ||
            properties.Any(x => x.Name is not ("conversationId" or "revision" or "limits") || x.Name == "limits" && descriptor.InvocationControlsProfile == SpecialistInvocationControls.LegacyProfile)) throw Invalid();
        var hasId = json.TryGetProperty("conversationId", out var id);
        var hasRevision = json.TryGetProperty("revision", out var revision);
        SpecialistConversationAdmission? continuation = null;
        if (hasId != hasRevision) throw Invalid();
        if (hasId)
        {
            if (id.ValueKind != JsonValueKind.String || !SpecialistTurnDocumentRules.IsToken(id.GetString()) ||
                revision.ValueKind != JsonValueKind.Number || !revision.TryGetInt64(out var expected) || expected is < 1 or long.MaxValue) throw Invalid();
            continuation = new(id.GetString()!, expected);
        }
        SpecialistTurnLimits? limits = null;
        if (json.TryGetProperty("limits", out var requested))
        {
            if (requested.ValueKind != JsonValueKind.Object) throw Invalid();
            var members = requested.EnumerateObject().ToArray();
            if (members.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != members.Length ||
                members.Any(x => x.Name is not ("maximumModelCalls" or "timeoutMilliseconds" or "maximumOutputTokens"))) throw Invalid();
            var published = descriptor.Limits;
            limits = new(ReadInteger("maximumModelCalls", published.MaximumModelCalls)!.Value,
                ReadInteger("timeoutMilliseconds", published.TimeoutMilliseconds)!.Value,
                ReadInteger("maximumOutputTokens", published.MaximumOutputTokens));
            if (limits == published) limits = null;
            int? ReadInteger(string name, int? fallback)
            {
                if (!requested.TryGetProperty(name, out var number)) return fallback;
                if (number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var result)) throw Invalid();
                return result;
            }
        }
        return new(continuation, limits);
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
    private static SpecialistTurnException Invalid() => SpecialistModelRunner.Failure("specialist.controls.invalid", "Invocation controls require the exact published contract, a valid conversation reference and integer limit values.");
}
