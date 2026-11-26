using Microsoft.Data.Sqlite;

namespace Elsa.Workflows.Runtime.Native.Migration.SqliteFixture;

public static class SqlitePlanFixtureCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args is ["sqlite-plan-projection", "inspect-neighbor", var neighborFile, var neighborPlan, var neighborActor, .. var neighborToken] &&
            neighborToken.Length <= 1)
            return await SqlitePlanProjectionChecks.InspectNeighborAsync(neighborFile, neighborPlan, neighborActor, neighborToken.SingleOrDefault());
        if (args is ["sqlite-plan-projection", "build", var planFile]) return await SqlitePlanProjectionChecks.BuildAsync(planFile);
        if (args is ["sqlite-plan-projection", "inspect", var existingPlanFile, var issuedPlan, var expectedDigest])
            return await SqlitePlanProjectionChecks.InspectAsync(existingPlanFile, issuedPlan, expectedDigest);
        if (args is ["sqlite-plan-projection", "enable-paging", var viewFile])
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await SqliteMigrationPlanReads.EnableAsync(viewFile, timeout.Token); return 0; }
            catch (Exception error) when (error is InvalidOperationException or SqliteException)
            { Console.Error.WriteLine(error.Message); return 1; }
        }
        if (args is ["sqlite-plan-projection", "list", var listFile, var listPlan, var limit, .. var continuation] &&
            int.TryParse(limit, out var pageSize) && pageSize is >= 1 and <= 500 && continuation.Length <= 1)
        {
            try { return await SqlitePlanProjectionChecks.ListAsync(listFile, listPlan, pageSize, continuation.SingleOrDefault()); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or SqliteException)
            { Console.Error.WriteLine(error.Message); return 1; }
        }
        Console.Error.WriteLine("""
            Usage
              sqlite-plan-projection build <file>
              sqlite-plan-projection inspect <file> <plan-ref> <digest>
              sqlite-plan-projection enable-paging <file>
              sqlite-plan-projection list <file> <plan-ref> <page-size 1-500> [continuation]
            """);
        return 2;
    }
}
