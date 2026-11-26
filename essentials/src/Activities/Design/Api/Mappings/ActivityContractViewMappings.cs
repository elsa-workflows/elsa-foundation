using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using System.Text.Json;

namespace Elsa.Activities.Design.Api.Models;

public static class ActivityContractViewMappings
{
    public static ActivityContract ToDomain(this ActivityContractView view) => new(
        view.ContractSchemaVersion,
        view.Inputs.Select(x => new ActivityInputContract(
            x.ReferenceKey,
            x.Name,
            x.Type.ToDomain(),
            x.IsRequired,
            x.IsNullable,
            x.Default is null ? null : new(x.Default.Syntax, x.Default.Value.Clone()),
            x.StorageDriverKey,
            x.Durability,
            x.DisplayName,
            x.Description,
            x.Category,
            x.Order,
            x.UiHint,
            Clone(x.UiSpecifications))).ToArray(),
        view.Outputs.Select(x => new ActivityOutputContract(
            x.ReferenceKey,
            x.Name,
            x.Type.ToDomain(),
            x.IsRequired,
            x.IsNullable,
            x.StorageDriverKey,
            x.Durability,
            x.DisplayName,
            x.Description,
            x.Category,
            x.Order,
            x.UiHint,
            Clone(x.UiSpecifications),
            x.SourceRepresentation)).ToArray(),
        view.Outcomes.Select(x => new ActivityOutcomeContract(x.ReferenceKey, x.Name, x.IsEmitted, x.Description)).ToArray());

    public static ActivityContractView ToView(this ActivityContract contract) => new(
        contract.ContractSchemaVersion,
        contract.Inputs.Select(x => new ActivityInputContractView(
            x.ReferenceKey,
            x.Name,
            x.Type.ToView(),
            x.IsRequired,
            x.IsNullable,
            x.Default is null ? null : new(x.Default.Syntax, x.Default.Value.Clone()),
            x.StorageDriverKey,
            x.Durability,
            x.DisplayName,
            x.Description,
            x.Category,
            x.Order,
            x.UiHint,
            Clone(x.UiSpecifications))).ToArray(),
        contract.Outputs.Select(x => new ActivityOutputContractView(
            x.ReferenceKey,
            x.Name,
            x.Type.ToView(),
            x.IsRequired,
            x.IsNullable,
            x.StorageDriverKey,
            x.Durability,
            x.DisplayName,
            x.Description,
            x.Category,
            x.Order,
            x.UiHint,
            Clone(x.UiSpecifications),
            x.SourceRepresentation)).ToArray(),
        contract.Outcomes.Select(x => new ActivityOutcomeContractView(x.ReferenceKey, x.Name, x.IsEmitted, x.Description)).ToArray());

    public static TypeReference ToDomain(this ActivityTypeReferenceView view)
    {
        if (!Enum.TryParse<CollectionKind>(view.CollectionKind, false, out var collectionKind) ||
            !StringComparer.Ordinal.Equals(collectionKind.ToString(), view.CollectionKind))
            throw new ArgumentException($"Collection kind '{view.CollectionKind}' is not supported.", nameof(view));
        return new(view.Alias, collectionKind);
    }

    public static ActivityTypeReferenceView ToView(this TypeReference type) => new(
        type.Alias,
        type.CollectionKind.ToString());

    private static JsonElement? Clone(JsonElement? value) => value is { } element ? element.Clone() : null;
}
