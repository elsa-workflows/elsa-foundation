using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Tests.Support;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1197: authoring rules, artifact compatibility and host bindings of the SQL destination.</summary>
public sealed class EtlSqlDestinationTests
{
    private static readonly EtlScope Scope = new("tenant", "sql");

    [Fact]
    public void A_keyed_sql_destination_on_the_main_route_compiles_and_round_trips()
    {
        var result = Compile(Pipeline());

        var artifact = Assert.IsType<EtlArtifact>(result.Artifact);
        var read = EtlArtifactV2Json.Read(artifact.Pipeline);
        Assert.Equal(new EtlSqlDestination("warehouse", "dbo.people", ["id"]), read.Routes.Main);
        Assert.Contains("\"kind\":\"sql\"", artifact.Pipeline.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_pipelines_keep_their_bytes_and_the_connector_kinds_keep_their_values()
    {
        var baseline = Compile(EtlV2TestPipelines.Create());
        var again = Compile(EtlV2TestPipelines.Create());

        Assert.Equal(baseline.Artifact!.ContentHash, again.Artifact!.ContentHash);
        Assert.DoesNotContain("sql", baseline.Artifact.Pipeline.GetRawText(), StringComparison.Ordinal);
        // Identities hash the integer kind, so the new kind is appended.
        Assert.Equal([0, 1, 2, 3], new[] { EtlConnectorKind.Csv, EtlConnectorKind.Http, EtlConnectorKind.Jsonl, EtlConnectorKind.Sql }.Select(kind => (int)kind));
    }

    [Theory]
    [InlineData("people;drop", "destination.sql-identifier-invalid")]
    [InlineData("a.b.c", "destination.sql-identifier-invalid")]
    [InlineData("1people", "destination.sql-identifier-invalid")]
    public void A_table_that_is_not_an_identifier_is_refused(string table, string code) =>
        AssertRejected(Pipeline() with { Routes = new(new EtlSqlDestination("warehouse", table, ["id"]), new EtlJsonlDestination("errors")) }, code);

    [Fact]
    public void Columns_must_be_identifiers_that_stay_distinct_ignoring_case()
    {
        AssertRejected(Pipeline() with { Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("order id", EtlFieldOrigin.Source, "name")] },
            "destination.sql-identifier-invalid");
        AssertRejected(Pipeline() with { Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("ID", EtlFieldOrigin.Source, "name")] },
            "destination.sql-identifier-invalid");
    }

    [Fact]
    public void The_key_must_be_one_to_eight_distinct_fields_mapped_from_required_source_fields()
    {
        AssertRejected(WithKey([]), "destination.sql-key-invalid");
        AssertRejected(WithKey(["id", "id"]), "destination.sql-key-invalid");
        AssertRejected(WithKey(["missing"]), "destination.sql-key-invalid");
        AssertRejected(WithKey(["nickname"]), "destination.sql-key-invalid");
        AssertRejected(WithKey(Enumerable.Range(0, 9).Select(index => $"k{index}").ToArray()), "destination.sql-key-invalid");
        Assert.NotNull(Compile(WithKey(["id", "name"])).Artifact);
    }

    [Fact]
    public void Only_the_main_route_can_deliver_to_sql() =>
        AssertRejected(Pipeline() with { Routes = new(new EtlJsonlDestination("output"), new EtlSqlDestination("warehouse", "people", ["id"])) },
            "destination.sql-route-unsupported");

    [Fact]
    public void A_binding_pins_a_secret_free_handle_and_refuses_a_table_it_does_not_allow()
    {
        var binding = Binding("Data Source=one.db;Password=secret");
        var bindings = new HostSqlBindings([binding], [new SqliteEtlSqlEngine()]);

        var resolved = bindings.ResolveCurrent(Scope, "warehouse", "MainOutput", "dbo.people");
        var otherSecret = new HostSqlBindings([Binding("Data Source=one.db;Password=other")], [new SqliteEtlSqlEngine()])
            .ResolveCurrent(Scope, "warehouse", "MainOutput", "dbo.people");
        var otherTarget = new HostSqlBindings([Binding("Data Source=two.db;Password=secret")], [new SqliteEtlSqlEngine()])
            .ResolveCurrent(Scope, "warehouse", "MainOutput", "dbo.people");

        Assert.Equal(resolved.Handle, otherSecret.Handle);
        Assert.NotEqual(resolved.Handle, otherTarget.Handle);
        Assert.DoesNotContain("secret", resolved.ToString(), StringComparison.Ordinal);
        Assert.Equal(resolved.Handle, bindings.ResolveExact(Scope, "warehouse", "MainOutput", "v1", resolved.Handle, "dbo.people").Handle);
        Assert.Equal("etl.sql-table-not-allowed",
            Assert.Throws<EtlBindingException>(() => bindings.ResolveCurrent(Scope, "warehouse", "MainOutput", "dbo.other")).Code);
        // PostgreSQL's quoted identifiers are case-sensitive, so the allow-list compares exactly.
        Assert.Equal("etl.sql-table-not-allowed",
            Assert.Throws<EtlBindingException>(() => bindings.ResolveCurrent(Scope, "warehouse", "MainOutput", "DBO.People")).Code);
        var badLedger = Binding("Data Source=one.db");
        badLedger.LedgerTable = "my-ledger";
        Assert.Equal("etl.binding-unavailable", Assert.Throws<EtlBindingException>(() =>
            new HostSqlBindings([badLedger], [new SqliteEtlSqlEngine()]).ResolveCurrent(Scope, "warehouse", "MainOutput", "dbo.people")).Code);
        Assert.Equal("etl.binding-revision-unavailable",
            Assert.Throws<EtlBindingException>(() => bindings.ResolveExact(Scope, "warehouse", "MainOutput", "v1", new string('0', 64), "dbo.people")).Code);
        Assert.Equal("etl.sql-unavailable",
            Assert.Throws<EtlBindingException>(() => new HostSqlBindings([binding], []).ResolveCurrent(Scope, "warehouse", "MainOutput", "dbo.people")).Code);
    }

    private static EtlPipelineV2 Pipeline()
    {
        var baseline = EtlV2TestPipelines.Create();
        return baseline with
        {
            Source = new EtlCsvSource("source", [new EtlColumn("id", EtlColumnType.Int64, true), new EtlColumn("name", EtlColumnType.String, true),
                new EtlColumn("nickname", EtlColumnType.String, false)]),
            Routes = new(new EtlSqlDestination("warehouse", "dbo.people", ["id"]), new EtlJsonlDestination("errors")),
            Mapping = [new("id", EtlFieldOrigin.Source, "id"), new("name", EtlFieldOrigin.Source, "name"), new("nickname", EtlFieldOrigin.Source, "nickname")]
        };
    }

    private static EtlPipelineV2 WithKey(string[] key) =>
        Pipeline() with { Routes = new(new EtlSqlDestination("warehouse", "dbo.people", key), new EtlJsonlDestination("errors")) };

    private static EtlHostSqlBindingOptions Binding(string connectionString) => new()
    {
        TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = "warehouse", Revision = "v1",
        Provider = "sqlite", ConnectionString = connectionString, AllowedTables = ["dbo.people"]
    };

    private static EtlCompilationResult Compile(EtlPipelineV2 pipeline) => new EtlArtifactCompiler().Compile("orders", 1, pipeline);

    private static void AssertRejected(EtlPipelineV2 pipeline, string code)
    {
        var result = Compile(pipeline);
        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, item => item.Code == code);
    }
}
