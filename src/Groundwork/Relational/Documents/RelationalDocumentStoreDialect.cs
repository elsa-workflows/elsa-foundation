namespace Groundwork.Relational.Documents;

public class RelationalDocumentStoreDialect
{
    public virtual string ParameterPrefix => "@";

    public string Parameter(string name) => $"{ParameterPrefix}{name}";

    public virtual object Boolean(bool value) => value ? 1 : 0;

    public virtual string InsertDocumentSql => $$"""
        INSERT INTO groundwork_documents
        (document_kind, id, schema_version, version, content_json, created_utc, updated_utc)
        VALUES ({{Parameter("kind")}}, {{Parameter("id")}}, {{Parameter("schemaVersion")}}, {{Parameter("version")}}, {{Parameter("content")}}, {{Parameter("createdUtc")}}, {{Parameter("updatedUtc")}});
        """;

    public virtual string UpdateDocumentSql => $$"""
        UPDATE groundwork_documents
        SET schema_version = {{Parameter("schemaVersion")}},
            version = {{Parameter("version")}},
            content_json = {{Parameter("content")}},
            updated_utc = {{Parameter("updatedUtc")}}
        WHERE document_kind = {{Parameter("kind")}} AND id = {{Parameter("id")}};
        """;

    public virtual string LoadDocumentSql => $$"""
        SELECT document_kind, id, schema_version, version, content_json, created_utc, updated_utc
        FROM groundwork_documents
        WHERE document_kind = {{Parameter("kind")}} AND id = {{Parameter("id")}};
        """;

    public virtual string DeleteDocumentSql => $$"""
        DELETE FROM groundwork_documents
        WHERE document_kind = {{Parameter("kind")}} AND id = {{Parameter("id")}};
        """;

    public virtual string DeleteIndexesSql => $$"""
        DELETE FROM groundwork_document_indexes
        WHERE document_kind = {{Parameter("kind")}} AND document_id = {{Parameter("id")}};
        """;

    public virtual string InsertIndexSql => $$"""
        INSERT INTO groundwork_document_indexes
        (document_kind, index_name, index_value, document_id, is_unique)
        VALUES ({{Parameter("kind")}}, {{Parameter("index")}}, {{Parameter("value")}}, {{Parameter("documentId")}}, {{Parameter("isUnique")}});
        """;

    public virtual string QueryByIndexSql => $$"""
        SELECT d.document_kind, d.id, d.schema_version, d.version, d.content_json, d.created_utc, d.updated_utc
        FROM groundwork_documents d
        INNER JOIN groundwork_document_indexes i
            ON i.document_kind = d.document_kind AND i.document_id = d.id
        WHERE i.document_kind = {{Parameter("kind")}} AND i.index_name = {{Parameter("index")}} AND i.index_value = {{Parameter("value")}}
        ORDER BY d.id
        LIMIT {{Parameter("take")}} OFFSET {{Parameter("skip")}};
        """;
}
