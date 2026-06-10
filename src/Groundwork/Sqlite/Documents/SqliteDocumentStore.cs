using Groundwork.Core.Manifests;
using Groundwork.Relational.Documents;
using Microsoft.Data.Sqlite;

namespace Groundwork.Sqlite.Documents;

public sealed class SqliteDocumentStore(SqliteConnection connection, StorageManifest manifest)
    : RelationalDocumentStore(connection, manifest, new SqliteDocumentStoreDialect());
