using Elsa.Cluster.Readability.Tests;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

// Two families declared on this test assembly the way a module declares its own: one whose chain runs 1 -> 2 -> 3, and
// one whose chain has a gap between 1 and 2. A host that has loaded this assembly reports them, and the stores of a
// module declaring them would read through them, which is what lets a test hold the report to the reads.
[assembly: EfModule(ChainedSchemaFamilies.Module, typeof(ChainedSchemaFamilies.Context), HistoryModule = "ReadabilityChained")]
[assembly: EfSchemaFamily(
    ChainedSchemaFamilies.Orders,
    ChainedSchemaFamilies.Module,
    "3",
    Upcasters = new[] { typeof(ChainedSchemaFamilies.OrdersOneToTwo), typeof(ChainedSchemaFamilies.OrdersTwoToThree) })]
[assembly: EfSchemaFamily(
    ChainedSchemaFamilies.Gapped,
    ChainedSchemaFamilies.Module,
    "3",
    Upcasters = new[] { typeof(ChainedSchemaFamilies.GappedZeroToOne), typeof(ChainedSchemaFamilies.GappedTwoToThree) })]

namespace Elsa.Cluster.Readability.Tests;

internal static class ChainedSchemaFamilies
{
    public const string Module = "Readability.Chained";
    public const string Orders = "ReadabilityChainedOrders";
    public const string Gapped = "ReadabilityChainedGapped";

    public sealed class Context(DbContextOptions options) : DbContext(options);

    [EfSchemaUpcaster("1", "2")]
    public sealed class OrdersOneToTwo : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    [EfSchemaUpcaster("2", "3")]
    public sealed class OrdersTwoToThree : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    [EfSchemaUpcaster("0", "1")]
    public sealed class GappedZeroToOne : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }

    [EfSchemaUpcaster("2", "3")]
    public sealed class GappedTwoToThree : IEfSchemaUpcaster
    {
        public string Upcast(EfSchemaContent content) => content.Value;
    }
}
