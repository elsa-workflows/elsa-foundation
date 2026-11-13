using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// Reads an activity execution written before outcome selections (spec 1208 FR-025, Q11). Value-flow document
/// version <see cref="ActivityExecutionValueFlowDocumentVersions.SingleOutcomeKey"/> recorded one completion outcome
/// as <c>completion.outcomeKey</c>; it is read as a named selection of exactly that key. No unspecified or empty history
/// is invented. Only the state table carries a completion; inspection and hierarchy rows pass through unchanged.
/// </summary>
[EfSchemaUpcaster("1.0.0", "2.0.0")]
internal sealed class RuntimeActivityExecutionV1Upcaster : IEfSchemaUpcaster
{
    private const string DocumentVersionProperty = "documentVersion";
    private const string CompletionProperty = "completion";
    private const string LegacyOutcomeKeyProperty = "outcomeKey";
    private const string SelectionProperty = "selection";

    public RuntimeActivityExecutionV1Upcaster() { }

    public EfSchemaRowContent Upcast(EfSchemaRowContent row)
    {
        if (row.Entity != typeof(ActivityExecutionStateEntity))
            return row;

        var content = row[nameof(ActivityExecutionStateEntity.ContentJson)]
            ?? throw new InvalidDataException("An activity execution row has no content.");
        var document = JsonNode.Parse(content)?.AsObject()
            ?? throw new InvalidDataException("An activity execution row's content is not a JSON object.");

        // Only the document version this upcaster knows changes; any other version keeps failing the runtime's own
        // value-flow compatibility check, exactly as it did before.
        if (document[DocumentVersionProperty]?.GetValue<int>() != ActivityExecutionValueFlowDocumentVersions.SingleOutcomeKey)
            return row;

        if (document[CompletionProperty] is JsonObject completion)
        {
            if (completion.ContainsKey(SelectionProperty))
                throw new InvalidDataException("A legacy activity completion cannot already carry an outcome selection.");
            var outcomeKey = completion[LegacyOutcomeKeyProperty]
                ?? throw new InvalidDataException("A legacy activity completion has no outcome key.");
            completion.Remove(LegacyOutcomeKeyProperty);
            // The key node is copied as stored, so its string encoding is kept exactly.
            completion[SelectionProperty] = new JsonObject
            {
                ["state"] = nameof(OutcomeSelectionState.Named),
                ["names"] = new JsonArray(outcomeKey.DeepClone())
            };
        }

        document[DocumentVersionProperty] = ActivityExecutionValueFlowDocumentVersions.Current;
        return row.With(nameof(ActivityExecutionStateEntity.ContentJson), document.ToJsonString());
    }
}
