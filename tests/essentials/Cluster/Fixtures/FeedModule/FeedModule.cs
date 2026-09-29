namespace Elsa.Cluster.Fixtures.FeedModule;

/// <summary>The names the fixture's module, family and features are declared and composed by.</summary>
public static class FeedModule
{
    /// <summary>The EF module, as its <c>[EfModule]</c> names it.</summary>
    public const string Name = "FeedModuleFixture";

    /// <summary>The module's migrations-history name, which also names its finalization tables.</summary>
    public const string HistoryModuleName = "ElsaFeedModuleFixture";

    /// <summary>The module's one schema family.</summary>
    public const string Family = "FeedModuleFixtureOrders";

    /// <summary>The version the database's record was created at, by a release before this one.</summary>
    public const string PreviousVersion = "1";

    /// <summary>The version this build writes, and the one the orders feature needs.</summary>
    public const string CurrentVersion = "2";

    /// <summary>The feature that binds the module's context and admits it through its finalization gate.</summary>
    public const string EntityFrameworkCoreFeature = "FeedModuleFixtureEntityFrameworkCore";

    /// <summary>The feature whose data only <see cref="CurrentVersion"/> holds, so it is dormant until that is finalized.</summary>
    public const string OrdersFeature = "FeedModuleFixtureOrders";

    /// <summary>Where the orders feature answers: 200 while it serves, 409 while it is dormant.</summary>
    public const string OrdersPath = "/feed-module-fixture/orders";
}
