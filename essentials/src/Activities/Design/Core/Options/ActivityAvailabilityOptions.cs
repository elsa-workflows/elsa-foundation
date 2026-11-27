namespace Elsa.Activities.Design.Core.Options;

public sealed class ActivityAvailabilityOptions
{
    public const string SectionName = "Elsa:Activities:Availability";

    public IDictionary<string, string[]> Sets { get; set; } = new Dictionary<string, string[]>(StringComparer.Ordinal);

    public ActivityAvailabilityRuleSet Include { get; set; } = new();

    public ActivityAvailabilityRuleSet Exclude { get; set; } = new();
}
