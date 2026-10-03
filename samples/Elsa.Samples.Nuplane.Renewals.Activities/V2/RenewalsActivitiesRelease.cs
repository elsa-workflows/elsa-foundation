using Elsa.Activities.Design.Core.Models;

namespace Elsa.Samples.Nuplane.Renewals.Activities;

/// <summary>Release 1.1.0: an optional decimal proposed premium is stored with the renewal.</summary>
internal static class RenewalsActivitiesRelease
{
    public const string ActivityVersion = "1.1.0";

    /// <summary>The premium registration feature is dormant until its schema version is finalized.</summary>
    public const string RequiredFeature = RenewalsModule.ProposedPremiumFeature;

    public const string Description = "Registers a renewal with a policy reference and optional proposed premium.";

    public static readonly InputDefinition[] AddedInputs = [RenewalsActivityReconciliationSource.DecimalInput(nameof(RegisterRenewal.ProposedPremium), "Proposed premium")];
}
