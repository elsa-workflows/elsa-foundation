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
internal sealed class InputMemberSelectionCapability(IServiceScopeFactory scopeFactory) : IRuntimeActivityConsumerCapability
{
    private readonly Lazy<IReadOnlyCollection<string>> _supported = new(() =>
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            return scope.ServiceProvider.GetService<IRuntimeActivityInputMaterializer>() is RuntimeActivityInputMaterializer
                ? [RuntimeReadSelection.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)]
                : [];
        }
        catch (InvalidOperationException)
        {
            // A materializer this composition cannot construct selects nothing; say so rather than fail every check.
            return [];
        }
    });

    /// <inheritdoc />
    public string ConsumerKey => RuntimeReadSelection.RequirementKey;

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedSchemaVersions => _supported.Value;
}
