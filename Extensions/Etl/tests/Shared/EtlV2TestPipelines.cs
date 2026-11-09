using Elsa.Etl.Core;

namespace Elsa.Etl.Tests;

/// <summary>Creates shared, representative ETL v2 pipelines for compiler and codec tests.</summary>
internal static class EtlV2TestPipelines
{
    /// <summary>Creates the baseline paginated HTTP JSON pipeline.</summary>
    public static EtlPipelineV2 Create() => new(
        new EtlHttpJsonSource(
            "source",
            "/records",
            [
                new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true),
                new EtlJsonField("name", "/name", EtlColumnType.String, Required: true)
            ],
            new EtlTokenPagination("/next", "cursor")),
        null,
        new EtlRoutes(
            new EtlHttpDestination("output"),
            new EtlHttpDestination("errors")),
        [new EtlOutputField("name", EtlFieldOrigin.Source, "name")],
        [],
        new EtlConnectorLimits(),
        new EtlHttpPolicy());
}
