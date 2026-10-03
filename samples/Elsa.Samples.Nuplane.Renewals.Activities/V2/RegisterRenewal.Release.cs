using Elsa.Activities.Runtime.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Renewals.Activities;

public sealed partial class RegisterRenewal
{
    /// <summary>Optional, so a workflow pinned to version 1.0.0, which has no such input, runs on this release unchanged.</summary>
    [ActivityInput(Key = nameof(ProposedPremium))]
    public decimal? ProposedPremium { get; set; }

    /// <summary>
    /// The premium service checks dormancy before any write and stores the policy and optional premium in one save. Without
    /// a premium value, the activity uses the baseline store and remains compatible with 1.0.0.
    /// </summary>
    private async Task WriteAsync(IServiceProvider services, string policyReference, CancellationToken cancellationToken)
    {
        if (ProposedPremium is not null)
            await services.GetRequiredService<RenewalPremium>().RegisterAsync(policyReference, ProposedPremium, cancellationToken);
        else
            await services.GetRequiredService<RenewalStore>().AddAsync(policyReference, cancellationToken);
    }
}
