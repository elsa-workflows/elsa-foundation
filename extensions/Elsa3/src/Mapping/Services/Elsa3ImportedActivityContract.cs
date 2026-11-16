using Elsa.Activities.Design.Core.Models;
using Elsa3.Mapping.Conversion;

namespace Elsa3.Mapping.Services;

/// <summary>
/// The activity contract an imported reusable workflow is published with (spec 246 FR-002): the workflow's inputs and
/// outputs, all optional as Elsa 3 declared none required and nullable where their type admits null (a Boolean does not:
/// the publisher refuses a nullable member of a type without null), and the outcomes the conversion derived.
/// </summary>
internal static class Elsa3ImportedActivityContract
{
    private const string DefaultStorageDriver = "elsa.json";

    public static ActivityContract Build(Elsa3ConvertedDefinition converted) => new(
        "1",
        converted.State.Inputs
            .Select(input => new ActivityInputContract(input.ReferenceKey, input.Name, input.Type, false, input.IsNullable, null, input.StorageDriverType ?? DefaultStorageDriver))
            .ToArray(),
        converted.State.Outputs
            .Select(output => new ActivityOutputContract(output.ReferenceKey, output.Name, output.Type, false, output.IsNullable, output.StorageDriverType ?? DefaultStorageDriver))
            .ToArray(),
        converted.BoundaryOutcomes.ToArray());
}
