using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Tests;

/// <summary>Spec 1237 T008: request validation, before anything is read or composed.</summary>
public sealed class MigrationRequestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "elsa3-migration-request-" + Guid.NewGuid().ToString("N"));

    public MigrationRequestTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_complete_request_is_valid_and_names_its_command()
    {
        var request = Request();

        Assert.Null(request.Validate());
        Assert.Equal("inventory", request.CommandName);
        Assert.Equal("assess", (request with { Command = MigrationCommand.Assess }).CommandName);
        Assert.Equal("elsa3-migration-out", MigrationRequest.DefaultOutputPath);
    }

    [Theory]
    [InlineData("", "out", "1.0.0")]
    [InlineData("export", " ", "1.0.0")]
    [InlineData("export", "out", "")]
    public void Missing_arguments_refuse_with_the_arguments_reason(string export, string output, string version)
    {
        var refusal = Request() with { ExportPath = export, OutputPath = output, ToolVersion = version };

        Assert.Equal(MigrationRefusal.Arguments, refusal.Validate()?.Reason);
    }

    [Fact]
    public void An_unknown_command_refuses()
    {
        Assert.Equal(MigrationRefusal.Arguments, (Request() with { Command = (MigrationCommand)7 }).Validate()?.Reason);
    }

    [Fact]
    public void An_output_path_that_is_a_file_refuses()
    {
        var file = Path.Combine(_root, "taken.txt");
        File.WriteAllText(file, "x");

        Assert.Equal(MigrationRefusal.Arguments, (Request() with { OutputPath = file }).Validate()?.Reason);
    }

    [Fact]
    public void An_output_path_inside_a_file_refuses()
    {
        var file = Path.Combine(_root, "export.zip");
        File.WriteAllText(file, "x");

        Assert.Equal(MigrationRefusal.Arguments, (Request() with { OutputPath = Path.Combine(file, "out") }).Validate()?.Reason);
    }

    [Fact]
    public void Invalid_path_characters_refuse()
    {
        Assert.Equal(MigrationRefusal.Arguments, (Request() with { OutputPath = "out\0" }).Validate()?.Reason);
        Assert.Equal(MigrationRefusal.Arguments, (Request() with { ExportPath = "exp\0" }).Validate()?.Reason);
    }

    [Theory]
    [InlineData("migrated")]
    [InlineData("migrated/workflows")]
    [InlineData("report.json")]
    public void An_export_inside_an_entry_the_run_replaces_refuses(string relative)
    {
        var output = Path.Combine(_root, "out");

        var refusal = (Request() with { OutputPath = output, ExportPath = Path.Combine(output, relative) }).Validate();

        Assert.Equal(MigrationRefusal.Arguments, refusal?.Reason);
    }

    [Fact]
    public void An_export_beside_the_replaced_entries_is_valid()
    {
        var output = Path.Combine(_root, "out");

        Assert.Null((Request() with { OutputPath = output, ExportPath = Path.Combine(output, "migrated-export") }).Validate());
    }

    [Fact]
    public void Validation_creates_nothing()
    {
        var output = Path.Combine(_root, "never");

        Assert.Null((Request() with { OutputPath = output }).Validate());
        Assert.False(Directory.Exists(output));
    }

    private MigrationRequest Request() =>
        new(MigrationCommand.Inventory, Path.Combine(_root, "export"), Path.Combine(_root, "out"), AllVersions: false, "1.0.0");
}
