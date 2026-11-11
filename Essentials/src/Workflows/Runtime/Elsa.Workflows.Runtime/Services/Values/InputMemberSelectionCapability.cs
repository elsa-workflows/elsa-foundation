using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Advertises <c>elsa.runtime.input-member-selection@1</c> (#4078) exactly when this runtime selects members: when the
/// composed <see cref="IRuntimeActivityInputMaterializer"/> is the built-in <see cref="RuntimeActivityInputMaterializer"/>,
/// which, with <see cref="WorkflowIntrinsicExecutor"/>, implements the selection. A host that replaces the materializer
/// advertises nothing, so its runtime refuses an executable with a member read before any activity runs instead of
/// handing the read to a materializer that may treat it as a whole-value read. This replaces the note's earlier
/// "fail composition" rule: a replaced materializer stays a valid host for every executable without member reads.
/// </summary>
/// <remarks>
/// The decision is made from the composition's service registrations, not from a resolved service: the last
/// non-keyed registration of <see cref="IRuntimeActivityInputMaterializer"/> must name
/// <see cref="RuntimeActivityInputMaterializer"/> as its implementation type. It is made once, when the capability is
/// constructed, and resolves nothing, so reading it creates no scope and constructs no materializer. A factory, an
/// instance or a decorator registration counts as a replacement, even one that ends up producing the built-in
/// materializer, because what it produces cannot be known without running it; such a host advertises nothing.
/// </remarks>
internal sealed class InputMemberSelectionCapability : IRuntimeActivityConsumerCapability
{
    public InputMemberSelectionCapability(IEnumerable<ServiceDescriptor> registrations)
    {
        var materializer = registrations.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IRuntimeActivityInputMaterializer) && !descriptor.IsKeyedService);
        SupportedSchemaVersions = materializer is { ImplementationType: var type } && type == typeof(RuntimeActivityInputMaterializer)
            ? [RuntimeReadSelection.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)]
            : [];
    }

    /// <inheritdoc />
    public string ConsumerKey => RuntimeReadSelection.RequirementKey;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; }
}
