namespace Groundwork.Relational.Documents;

public sealed class SqlServerDocumentStoreDialect : RelationalDocumentStoreDialect
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
}
