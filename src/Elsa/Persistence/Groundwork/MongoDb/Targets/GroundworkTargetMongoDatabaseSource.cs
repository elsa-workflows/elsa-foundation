using Elsa.Persistence.Groundwork.Targets;
using MongoDB.Driver;

namespace Elsa.Persistence.Groundwork.MongoDb.Targets;

/// <summary>
/// The MongoDB leaf's database source for one target, closing over the connection string and database name.
/// <para>
/// The client is created once and reused. The MongoDB driver's client owns the connection pool and is
/// designed to be shared, so opening a fresh one per read would defeat pooling; this is the opposite of the
/// relational source, which hands out a new unopened <c>DbConnection</c> each time because ADO.NET pools
/// underneath the connection object instead.
/// </para>
/// </summary>
public sealed class GroundworkTargetMongoDatabaseSource : IGroundworkTargetMongoDatabaseSource
{
    private readonly Lazy<IMongoDatabase> _database;

    public GroundworkTargetMongoDatabaseSource(string targetName, string connectionString, string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        TargetName = GroundworkTargetNames.Normalize(targetName);
        DatabaseName = databaseName;
        _database = new Lazy<IMongoDatabase>(
            () => new MongoClient(connectionString).GetDatabase(databaseName),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string TargetName { get; }

    public string DatabaseName { get; }

    public IMongoDatabase GetDatabase() => _database.Value;
}
