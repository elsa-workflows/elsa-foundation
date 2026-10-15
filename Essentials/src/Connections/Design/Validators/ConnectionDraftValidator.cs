using Elsa.Activities.Design.Core.Contracts;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Connections.Design.Validators;

/// <summary>
/// Publish gate (D7): a missing or wrong-type connection blocks; <c>NeedsAuthorization</c> does not.
/// </summary>
public sealed class ConnectionDraftValidator(
    IActivityStructureService activityStructureService,
    IActivityDefinitionLookup activityDefinitionLookup,
    IConnectionManager connectionManager,
    IConnectionPublishTenantAccessor tenantAccessor) : IDraftValidator
{
    public const string MissingType = "Connections/Missing";
    public const string TypeMismatchType = "Connections/TypeMismatch";
    public const string NeedsAuthorizationType = "Connections/NeedsAuthorization";

    public async ValueTask<IEnumerable<ValidationError>> Validate(IWorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        var tenantId = tenantAccessor.TenantId;
        var errors = new List<ValidationError>();
        foreach (var node in WalkTree(draft.State.RootActivity))
        {
            if (string.IsNullOrWhiteSpace(node.ActivityVersionId))
                continue;

            var version = await activityDefinitionLookup.FindVersion(node.ActivityVersionId, cancellationToken);
            if (version is null)
                continue;

            foreach (var input in version.Inputs.Where(definition => !string.IsNullOrWhiteSpace(definition.ConnectionType)))
            {
                var state = node.Inputs.FirstOrDefault(argument =>
                    StringComparer.Ordinal.Equals(argument.ReferenceKey, input.ReferenceKey));
                if (state is null || !state.IsBound())
                    continue;

                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    errors.Add(new ValidationError(
                        Path: $"{node.NodeId}/inputs/{input.ReferenceKey}",
                        Type: MissingType,
                        Message: "A tenant is required to publish a workflow that uses a connection."));
                    continue;
                }

                var reading = ConnectionReferencePayload.Read(state.Value?.Value);
                if (reading.Reference is not { } reference)
                {
                    errors.Add(new ValidationError(
                        Path: $"{node.NodeId}/inputs/{input.ReferenceKey}",
                        Type: MissingType,
                        Message: $"Activity '{node.NodeId}' input '{input.Name}' is not a well-formed connection reference."));
                    continue;
                }

                var metadata = await connectionManager.FindAsync(tenantId, reference.Name, cancellationToken);
                if (metadata is null)
                {
                    errors.Add(new ValidationError(
                        Path: $"{node.NodeId}/inputs/{input.ReferenceKey}",
                        Type: MissingType,
                        Message: $"Connection '{reference.Name}' was not found."));
                    continue;
                }

                var requiredType = input.ConnectionType!;
                if (!string.Equals(metadata.TypeName, requiredType, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ValidationError(
                        Path: $"{node.NodeId}/inputs/{input.ReferenceKey}",
                        Type: TypeMismatchType,
                        Message: $"Connection '{reference.Name}' is type '{metadata.TypeName}', not '{requiredType}'."));
                    continue;
                }

                // NeedsAuthorization warns only: it does not block publish (D7). The connection's
                // status is visible on the picker; a run still faults with ConnectionUnavailable.
                _ = string.Equals(metadata.Status, ConnectionStatuses.NeedsAuthorization, StringComparison.Ordinal);
            }
        }

        return errors;
    }

    private IEnumerable<ActivityNode> WalkTree(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<ActivityNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
