using System.Data.Common;
using Groundwork.Relational.Documents;
using Microsoft.Data.Sqlite;

namespace Groundwork.Sqlite.Documents;

internal sealed class SqliteDocumentStoreDialect : RelationalDocumentStoreDialect
{
    public override bool IsDuplicateDocumentKeyException(DbException exception) =>
        exception is SqliteException { SqliteErrorCode: 19 } sqliteException &&
        sqliteException.Message.Contains("UNIQUE constraint failed: groundwork_documents.document_kind, groundwork_documents.id", StringComparison.OrdinalIgnoreCase);

    public override bool IsUniqueIndexException(DbException exception) =>
        exception is SqliteException { SqliteErrorCode: 19 } sqliteException &&
        sqliteException.Message.Contains("UNIQUE constraint failed:", StringComparison.OrdinalIgnoreCase);

    public override bool IsWriteDependencyException(DbException exception) =>
        exception is SqliteException { SqliteErrorCode: 19 } sqliteException &&
        sqliteException.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase);
}
