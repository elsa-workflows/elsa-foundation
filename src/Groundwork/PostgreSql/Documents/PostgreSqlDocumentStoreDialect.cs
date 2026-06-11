using System.Data.Common;
using Groundwork.Relational.Documents;
using Npgsql;

namespace Groundwork.PostgreSql.Documents;

internal sealed class PostgreSqlDocumentStoreDialect : RelationalDocumentStoreDialect
{
    public override bool IsDuplicateDocumentKeyException(DbException exception) =>
        exception is PostgresException { SqlState: "23505", ConstraintName: "groundwork_documents_pkey" };

    public override bool IsUniqueIndexException(DbException exception) =>
        exception is PostgresException { SqlState: "23505" };

    public override bool IsWriteDependencyException(DbException exception) =>
        exception is PostgresException { SqlState: "23503" };
}
