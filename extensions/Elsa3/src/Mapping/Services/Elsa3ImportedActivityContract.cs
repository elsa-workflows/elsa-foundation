using Elsa.Serialization.Core;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
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

    public static ActivityContract Build(Elsa3ConvertedDefinition converted, IWellKnownTypeRegistry? types = null, IVariableTypeDescriptorCatalog? descriptors = null) => new(
        "1",
        converted.State.Inputs
            .Select(input => new ActivityInputContract(input.ReferenceKey, input.Name, input.Type, false, NullPolicyDefaults.AcceptsNull(input.Type, types, descriptors), null, input.StorageDriverType ?? DefaultStorageDriver))
            .ToArray(),
        converted.State.Outputs
            .Select(output => new ActivityOutputContract(output.ReferenceKey, output.Name, output.Type, false, NullPolicyDefaults.AcceptsNull(output.Type, types, descriptors), output.StorageDriverType ?? DefaultStorageDriver))
            .ToArray(),
        converted.BoundaryOutcomes.ToArray());
}
