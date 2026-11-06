using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Messaging;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Opt-in material facts only. Neither construction nor decoding proves publication, owner or delivery authority.</summary>
internal sealed class BpmnMessageModelDefinition
{
    internal const int CurrentVersion = 7;
    internal const string Profile = "foundation-message-v1";
    internal const string EnclosingOwner = "enclosing-declared-definition";
    internal BpmnMessageModelDefinition(JsonElement process, string correlationInputName,
        IReadOnlyCollection<BpmnMessageBinding> bindings)
    {
        if (process.ValueKind != JsonValueKind.Object) throw new ArgumentException("The process must be an object.", nameof(process));
        if (string.IsNullOrWhiteSpace(correlationInputName) || correlationInputName.Length > 128)
            throw new ArgumentException("The correlation input name is unavailable.", nameof(correlationInputName));
        ArgumentNullException.ThrowIfNull(bindings);
        var snapshot = bindings.Select(binding => binding ?? throw new ArgumentException("A Message binding is null.", nameof(bindings)))
            .OrderBy(binding => binding.ElementId, StringComparer.Ordinal).ToArray();
        if (snapshot.Length == 0 || snapshot.Select(binding => binding.ElementId).Distinct(StringComparer.Ordinal).Count() != snapshot.Length ||
            snapshot.Where(binding => binding.Role == BpmnMessageBinding.Catch).Select(binding => binding.BindingId).Distinct(StringComparer.Ordinal).Count() !=
            snapshot.Count(binding => binding.Role == BpmnMessageBinding.Catch))
            throw new ArgumentException("Message element/work binding identities must be complete and distinct.", nameof(bindings));
        _ = MessageMaterialInterestResult.Recognized(snapshot.Select(binding => binding.Declaration()).ToArray());
        Process = process.Clone(); CorrelationInputName = correlationInputName; Bindings = Array.AsReadOnly(snapshot);
    }
    internal JsonElement Process { get; }
    internal string CorrelationInputName { get; }
    internal IReadOnlyList<BpmnMessageBinding> Bindings { get; }
    public override string ToString() => nameof(BpmnMessageModelDefinition);
}

/// <summary>One fixed-name physical Message element and pinned portable payload contract.</summary>
internal sealed class BpmnMessageBinding
{
    internal const string Start = "start";
    internal const string Catch = "catch";
    internal const string AcceptedKey = "accepted-message-key";
    internal const string CapturedKey = "captured-execution-key";
    internal BpmnMessageBinding(string elementId, string? bindingId, string role, string messageName,
        string businessKeySource, ValueTypeDescriptor payloadType)
    {
        if (string.IsNullOrWhiteSpace(elementId) || elementId.Length > 256) throw new ArgumentException("The Message element address is unavailable.", nameof(elementId));
        if (role is not (Start or Catch) || role == Start && (bindingId is not null || businessKeySource != AcceptedKey) ||
            role == Catch && (string.IsNullOrWhiteSpace(bindingId) || bindingId.Length > 256 || businessKeySource != CapturedKey))
            throw new ArgumentException("The Message binding role, work address or key source is unsupported.");
        ArgumentNullException.ThrowIfNull(payloadType);
        if (!Enum.IsDefined(payloadType.CollectionKind) || payloadType.Schema is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("The payload type contract is unsupported.", nameof(payloadType));
        ElementId = elementId; BindingId = bindingId; Role = role; MessageName = messageName; BusinessKeySource = businessKeySource;
        PayloadType = new(payloadType.Alias, payloadType.CollectionKind, payloadType.Schema, payloadType.SchemaVersion);
        _ = Declaration(); // Central exact UTF-8 name/bounds validation; no normalization or mutable catalog lookup.
    }
    internal string ElementId { get; }
    internal string? BindingId { get; }
    internal string Role { get; }
    internal string MessageName { get; }
    internal string BusinessKeySource { get; }
    internal ValueTypeDescriptor PayloadType { get; }
    internal string ReceiverAddress => $"bpmn.message/v1:{ElementId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)}:{ElementId}:{(BindingId is null ? "-" : BindingId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))}:{BindingId}";
    internal MessageMaterialInterestDeclaration Declaration() => new(ReceiverAddress, [MessageName], Role == Start);
    public override string ToString() => nameof(BpmnMessageBinding);
}
