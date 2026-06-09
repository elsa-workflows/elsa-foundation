using System.Reflection;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Extensions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>
/// THE BRIDGE. Three moves, each touching exactly one seam:
/// <list type="number">
///   <item><b>Read</b> the persisted definition version — the Design seam hands over an opaque
///   <c>(DescriptorType, DescriptorPayload)</c> plus the argument definitions.</item>
///   <item><b>Invoke</b> <see cref="IActivityFactory"/> — the Runtime seam dispatches on the descriptor
///   type to the owning constructor and returns a whole <c>IActivity</c>.</item>
///   <item><b>Project</b> both sides into one view so the crossing is visible.</item>
/// </list>
/// Unit 006 is construct-only, so no author values are bound here — the inputs/outputs bags are left
/// null. Mapping author argument <i>values</i> (joining a node's <c>ArgumentState</c> by
/// <c>ReferenceKey</c> onto typed runtime arguments) is the place this bridge grows into the
/// compile-and-publish domain.
/// </summary>
public sealed class ConstructActivityRequestHandler(
    IQueries<ActivityDefinitionVersion> versions,
    IActivityFactory factory)
    : IRequestHandler<ConstructActivity, ConstructedActivityView>
{
    public async Task<ConstructedActivityView> Handle(ConstructActivity request, CancellationToken cancellationToken)
    {
        // 1. Read — the Design seam. The payload stays opaque; we never deserialize it ourselves.
        var version = await versions.GetVersionInlcudingDefinition(request.ActivityId, cancellationToken);

        // 2. Invoke — the Runtime seam. Construct-only: no author values to bind yet.
        var activity = await factory.Create(
            version.DescriptorType,
            version.DescriptorPayload,
            inputs: null,
            outputs: null,
            cancellationToken);

        // 3. Project — both sides in one payload.
        return new ConstructedActivityView(
            ActivityVersionId: version.Id,
            DescriptorType: version.DescriptorType,
            RuntimeType: activity.GetType().FullName ?? activity.GetType().Name,
            Properties: ReadConcreteProperties(activity),
            SyntheticProperties: activity.SyntheticProperties,
            CustomProperties: activity.CustomProperties,
            Inputs: version.Inputs.Select(i => new ArgumentView(i.ReferenceKey, i.Name, i.Type.GetTypeFullName())).ToArray(),
            Outputs: version.Outputs.Select(o => new ArgumentView(o.ReferenceKey, o.Name, o.Type.GetTypeFullName())).ToArray());
    }

    /// <summary>
    /// The activity's own (concrete-type-declared) properties — e.g. <c>WorkflowIdentity</c> on the
    /// workflow-backed activity, or a primitive's typed <c>InputArgument</c> properties. Base
    /// <c>IActivity</c> bookkeeping (Id, Name, the bags) is excluded via <c>DeclaredOnly</c>.
    /// </summary>
    private static IReadOnlyDictionary<string, string?> ReadConcreteProperties(IActivity activity)
    {
        var properties = activity.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.GetIndexParameters().Length == 0);

        var result = new Dictionary<string, string?>();
        foreach (var property in properties)
            result[property.Name] = property.GetValue(activity)?.ToString();

        return result;
    }
}
