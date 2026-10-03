using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Renewals.Activities;

public sealed partial class RegisterRenewal
{
    private async Task WriteAsync(IServiceProvider services, string policyReference, CancellationToken cancellationToken) =>
        await services.GetRequiredService<RenewalStore>().AddAsync(policyReference, cancellationToken);
}
