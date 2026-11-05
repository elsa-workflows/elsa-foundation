using Elsa.Workflows.Runtime.Core.Contracts;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;

namespace Elsa.Activities.Switch.Internal;

/// <summary>Consumes the ordered case inputs before the invocation's immutable snapshot is committed.</summary>
internal sealed class SwitchStructureInputPolicy : IRuntimeStructureInputMaterializationPolicy
{
    public string Kind => SwitchActivity.StructureKind;
    public string SchemaVersion => SwitchActivity.StructureSchemaVersion;

    public async ValueTask MaterializeAsync(IRuntimeStructureInputMaterializationContext context, CancellationToken cancellationToken)
    {
        var navigator = SwitchNavigator.From(context.Node);
        foreach (var item in navigator.Cases)
        {
            bool matches;
            if (item.ConditionInputKey is { } key)
                matches = SwitchConditionValues.Boolean(await context.MaterializeAsync(key, cancellationToken), key);
            else
            {
                var value = await context.MaterializeAsync(nameof(SwitchActivity.Value), cancellationToken);
                matches = StringComparer.Ordinal.Equals(item.Match, SwitchConditionValues.String(value, nameof(SwitchActivity.Value)));
            }
            if (matches) return;
        }
    }
}
