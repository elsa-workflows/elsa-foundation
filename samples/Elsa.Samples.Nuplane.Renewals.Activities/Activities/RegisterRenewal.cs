using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

// One namespace for the package, whatever the folder, as in the Renewals sample: the activity's full name is its type key and
// its alias, which every workflow that uses it stores.
namespace Elsa.Samples.Nuplane.Renewals.Activities;

/// <summary>
/// Registers a renewal through the Renewals module's store, as <c>POST /demo/renewals</c> does. What release 1.1.0 adds is in
/// <see cref="WriteAsync"/>, one implementation per release folder.
/// </summary>
/// <remarks>
/// <para>
/// It is constructed in a scope of its own for each execution, so <paramref name="services"/> resolves the scoped
/// <see cref="RenewalStore"/> of that scope.
/// </para>
/// <para>
/// A workflow node pinned to version 1.0.0 keeps running after release 1.1.0 is installed in place, but on release 1.1.0's
/// class: the alias is the CLR type name, and one type is registered per alias. Release 1.1.0 therefore stays
/// input-compatible with 1.0.0: <see cref="PolicyReference"/> keeps its name and meaning, and <c>ProposedPremium</c> is optional. A release
/// that renamed or removed an input would break every workflow pinned to an earlier version, and nothing warns of that yet.
/// </para>
/// </remarks>
public sealed partial class RegisterRenewal(IServiceProvider services) : Activity<ActivityUnit>
{
    [ActivityInput(Key = nameof(PolicyReference))]
    public string? PolicyReference { get; set; }

    protected override async ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(PolicyReference))
            throw new InvalidOperationException("Register renewal needs a policy reference.");

        await WriteAsync(services, PolicyReference.Trim(), context.CancellationToken);
        return ActivityTransition.Complete(ActivityUnit.Value);
    }
}
