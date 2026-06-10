using Groundwork.Core.Manifests;
using Groundwork.Relational.Documents;
using Microsoft.Data.SqlClient;

namespace Groundwork.SqlServer.Documents;

public sealed class SqlServerDocumentStore(SqlConnection connection, StorageManifest manifest)
    : RelationalDocumentStore(connection, manifest, new SqlServerDocumentStoreDialect());
