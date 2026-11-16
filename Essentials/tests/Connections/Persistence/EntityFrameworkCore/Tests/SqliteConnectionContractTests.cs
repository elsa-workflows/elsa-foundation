using Elsa.Connections.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Tests;

public sealed class SqliteConnectionContractTests : Elsa.Connections.Tests.ConnectionRepositoryContractTests, IDisposable
{
    private readonly ConnectionsSqliteDbContext context;
    public SqliteConnectionContractTests() : this(NewContext()) { }
    private SqliteConnectionContractTests(ConnectionsSqliteDbContext context) : base(new EfConnectionRepository(context)) => this.context = context;
    private static ConnectionsSqliteDbContext NewContext()
    {
        var path = Path.Combine(Path.GetTempPath(), "connections-contract-" + Guid.NewGuid().ToString("N") + ".db");
        var context = new ConnectionsSqliteDbContext(new DbContextOptionsBuilder<ConnectionsSqliteDbContext>().UseSqlite("Data Source=" + path + ";Pooling=False").Options);
        context.Database.EnsureCreated();
        return context;
    }
    public void Dispose()
    {
        var path = context.Database.GetDbConnection().DataSource;
        context.Dispose();
        File.Delete(path);
    }
}
