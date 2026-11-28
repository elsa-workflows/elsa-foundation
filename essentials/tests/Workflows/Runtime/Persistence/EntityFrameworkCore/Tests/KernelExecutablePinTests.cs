using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class KernelExecutablePinTests
{
    [Theory]
    [InlineData(PersistenceScope.DefaultValue)]
    [InlineData("kernel-pin-tenant")]
    public async Task Sqlite_keeps_selected_material_until_every_exact_pin_is_released(string scope)
    {
        var directory = Directory.CreateTempSubdirectory("kernel-executable-pins-");
        try
        {
            var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory.FullName, "runtime.db"), Pooling = false };
            await KernelExecutablePinContract.RunAsync("Sqlite", connection.ToString(), scope);
        }
        finally { directory.Delete(recursive: true); }
    }
}
