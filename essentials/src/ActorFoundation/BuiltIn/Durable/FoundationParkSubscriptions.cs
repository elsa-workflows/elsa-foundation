namespace Kernel.Durable;

internal sealed record ParkSubscriptionRecipe(string Key, string Wait, bool Keep);

public sealed partial class InMemoryStore
{
    private const int SubscriptionRestoreAllowance = 7168;
    private const int KeepingRestoreAllowance = 9728;
    private const int CatchUpTicketAllowance = 90_112;

    internal IReadOnlyList<SubscriptionRow>? PlanFoundationSubscriptions(InstanceRow row, string tenant,
        IReadOnlyList<ParkSubscriptionRecipe> recipes, long bytes)
    {
        var subscriptions = new List<SubscriptionRow>(recipes.Count);
        var distinct = new Dictionary<string, HashSet<string>>(_foundationEventEquality);
        if (recipes.Any(recipe => recipe.Keep)) bytes += CatchUpTicketAllowance;
        foreach (var recipe in recipes)
        {
            if (recipe.Key is null || recipe.Wait is null ||
                !FoundationMetadataLimits.FitsName(recipe.Key) || !FoundationMetadataLimits.FitsName(recipe.Wait))
                return null;
            bytes += recipe.Keep ? KeepingRestoreAllowance : SubscriptionRestoreAllowance;
            if (bytes > MaxRestoreBytes) return null;
            if (!distinct.TryGetValue(recipe.Key, out var waits))
                distinct.Add(recipe.Key, waits = new(_foundationEventEquality));
            if (!waits.Add(recipe.Wait)) return null;
            subscriptions.Add(new(tenant + "_" + recipe.Key, row.Id, recipe.Wait, Creation: row.Creation)
                { FoundationTenant = tenant, FoundationKeepsEvents = recipe.Keep });
        }
        return subscriptions;
    }
}
