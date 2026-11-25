using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

/// <summary>
/// Installs the shipped format (24) in an empty dedicated database, as a Workbench with
/// <c>Elsa:CorePreview:Publications</c> on expects it: the setup step of an owned Core Preview stack
/// (tools/ux-acceptance/core_preview.py). It refuses a database that holds anything.
/// </summary>
internal static class CorePreviewStoreInstall
{
    public static async Task<int> RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Set KERNEL_PREVIEW_POSTGRES to the dedicated, empty Core Preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => PostgresPreviewHosting.CreateShippedContext(options);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using (var db = Open())
            if (!await PostgresFixtureDatabase.IsEmptyAsync(db, stop.Token))
                throw new InvalidOperationException("The Core Preview store installs only in an empty database.");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync();
        await store.InstallPreviewRootSchemaAsync(stop.Token);
        Console.WriteLine("ok installed the shipped Core Preview format in the empty database");
        return 0;
    }
}
