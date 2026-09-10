using FluentMigrator.Runner.VersionTableInfo;

namespace Elsa.Persistence.Spike.VariantB.Host;

[VersionTableMetaData]
public sealed class SecretsVersionTable : IVersionTableMetaData
{
    public object ApplicationContext { get; set; } = null!;
    public bool OwnsSchema => false;
    public string? SchemaName => null;
    public string TableName => SchemaNames.FluentMigratorVersionTable;
    public string ColumnName => "Version";
    public string DescriptionColumnName => "Description";
    public string UniqueIndexName => "UC_Version";
    public string AppliedOnColumnName => "AppliedOn";
    public bool CreateWithPrimaryKey => true;
}
