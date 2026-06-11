using System.Data.Common;
using Groundwork.Relational.Documents;
using Microsoft.Data.SqlClient;

namespace Groundwork.SqlServer.Documents;

internal sealed class SqlServerDocumentStoreDialect : RelationalDocumentStoreDialect
{
    public override string QueryByIndexSql => $$"""
        SELECT d.document_kind, d.id, d.schema_version, d.version, d.content_json, d.created_utc, d.updated_utc
        FROM groundwork_documents d
        INNER JOIN groundwork_document_indexes i
            ON i.document_kind = d.document_kind AND i.document_id = d.id
        WHERE i.document_kind = {{Parameter("kind")}} AND i.index_name = {{Parameter("index")}} AND i.index_value = {{Parameter("value")}}
        ORDER BY d.id
        OFFSET {{Parameter("skip")}} ROWS FETCH NEXT {{Parameter("take")}} ROWS ONLY;
        """;

    public override string QueryByPhysicalizedSql(string tableName, string columnName) => $$"""
        SELECT d.document_kind, d.id, d.schema_version, d.version, d.content_json, d.created_utc, d.updated_utc
        FROM groundwork_documents d
        INNER JOIN {{tableName}} p
            ON p.document_kind = d.document_kind AND p.document_id = d.id
        WHERE p.document_kind = {{Parameter("kind")}} AND p.{{columnName}} = {{Parameter("value")}}
        ORDER BY d.id
        OFFSET {{Parameter("skip")}} ROWS FETCH NEXT {{Parameter("take")}} ROWS ONLY;
        """;

    public override bool IsDuplicateDocumentKeyException(DbException exception) =>
        exception is SqlException { Number: 2627 or 2601 } sqlException &&
        sqlException.Message.Contains("pk_groundwork_documents", StringComparison.OrdinalIgnoreCase);

    public override bool IsUniqueIndexException(DbException exception) =>
        exception is SqlException { Number: 2627 or 2601 };

    public override bool IsWriteDependencyException(DbException exception) =>
        exception is SqlException { Number: 547 };
}
