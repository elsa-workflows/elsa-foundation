namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static class RootEventWaits
    {
        internal static TurnCommit Lower(WorkerRoot root, TurnCommit submitted, TurnCommit journal)
        {
            var prefix = root.Tenant + "_";
            if (submitted.Subscribed.Concat(submitted.Unsubscribed).Any(wait =>
                wait.Creation != 1 || wait.Instance != root.Actor || wait.FoundationTenant != root.Tenant ||
                wait.Start is not null || wait.FoundationKeepsEvents || !wait.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                !RootEventAdmission.FitsWaitKey(wait.Key[prefix.Length..]) || !RootEventAdmission.FitsWaitKey(wait.Wait)))
                throw RootShape();

            // The bound generated-root address embeds its partition and cannot be reused. Pass only the physical
            // columns to the owned writer, which writes waits and their checkpoint under the same fence.
            return submitted.Unsubscribe
                ? journal with { Unsubscribe = true }
                : journal with
                {
                    Subscribed = LowerRows(submitted.Subscribed),
                    Unsubscribed = LowerRows(submitted.Unsubscribed)
                };
        }

        private static SubscriptionRow[] LowerRows(IReadOnlyList<SubscriptionRow> rows) =>
            rows.Select(wait => new SubscriptionRow(wait.Key, wait.Instance, wait.Wait)).ToArray();
    }
}
