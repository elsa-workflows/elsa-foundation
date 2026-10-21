using Elsa.Etl.Core;

namespace Elsa.Etl.Tests;

internal static class EtlTestPipelines
{
    public static EtlPipeline Create(string outputName = "name") => new(
        new EtlFileBindings("source", "lookup", "output", "errors"),
        [new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("name", EtlColumnType.String, false)],
        [new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("category", EtlColumnType.String, false)],
        "id",
        "id",
        [new EtlOutputField(outputName, EtlFieldOrigin.Source, "name")],
        [],
        new EtlLimits());
}
