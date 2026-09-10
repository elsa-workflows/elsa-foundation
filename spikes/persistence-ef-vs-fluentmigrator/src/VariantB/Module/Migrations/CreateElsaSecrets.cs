using FluentMigrator;

namespace Elsa.Persistence.Spike.VariantB.Migrations;

[Migration(20260101000001)]
public sealed class CreateElsaSecrets : Migration
{
    public override void Up()
    {
        IfDatabase(ProcessorIdConstants.SQLite)
            .Create.Table(SchemaNames.Table)
            .WithColumn("Id").AsCustom("TEXT").PrimaryKey()
            .WithColumn("TenantId").AsString(256).NotNullable()
            .WithColumn("Name").AsString(256).NotNullable()
            .WithColumn("Payload").AsCustom("TEXT").NotNullable()
            .WithColumn("RowVersion").AsCustom("BLOB").NotNullable();

        IfDatabase(ProcessorIdConstants.PostgreSQL)
            .Create.Table(SchemaNames.Table)
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("TenantId").AsString(256).NotNullable()
            .WithColumn("Name").AsString(256).NotNullable()
            .WithColumn("Payload").AsCustom("jsonb").NotNullable()
            .WithColumn("RowVersion").AsCustom("bytea").NotNullable();

        Create.UniqueConstraint(SchemaNames.UniqueIndex)
            .OnTable(SchemaNames.Table)
            .Columns("TenantId", "Name");
    }

    public override void Down()
    {
        Delete.UniqueConstraint(SchemaNames.UniqueIndex).FromTable(SchemaNames.Table);
        Delete.Table(SchemaNames.Table);
    }
}
