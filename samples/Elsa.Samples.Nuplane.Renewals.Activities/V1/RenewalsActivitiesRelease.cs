using Elsa.Activities.Design.Core.Models;

namespace Elsa.Samples.Nuplane.Renewals.Activities;

/// <summary>Release 1.0.0: Register renewal requires a policy reference.</summary>
internal static class RenewalsActivitiesRelease
{
    public const string ActivityVersion = "1.0.0";

    public const string RequiredFeature = "Renewals";

    public const string Description = "Registers a renewal using its policy reference.";

    public static readonly InputDefinition[] AddedInputs = [];
}
