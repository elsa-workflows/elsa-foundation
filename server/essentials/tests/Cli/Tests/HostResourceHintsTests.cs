using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class HostResourceHintsTests : IDisposable
{
    private readonly TempDirectory host = new("elsa-resource-hints-");

    public void Dispose() => host.Dispose();

    [Theory]
    [InlineData("appsettings.json", "{ \"Elsa\": { \"Persistence\": { \"Resources\": null } } }")]
    [InlineData("appsettings.Production.json", "{ \"Elsa:Persistence:DefaultResource\": null }")]
    [InlineData("shells.json", "{ \"CShells\": { \"Shells\": { \"default\": { \"Configuration\": { \"Elsa\": { \"Persistence\": { \"Bindings\": null } } } } } } }")]
    [InlineData("shells.Production.json", "{ \"CShells:Shells\": [{ \"Configuration:Elsa:Persistence:DefaultResource\": null }] }")]
    public void Resource_key_presence_blocks_an_old_host_even_when_its_value_is_null(string file, string json)
    {
        File.WriteAllText(host.File(file), json);

        Assert.True(HostResourceHints.Exist(host.Path, "Production"));
    }

    [Fact]
    public void Ordinary_legacy_settings_do_not_claim_resource_intent()
    {
        File.WriteAllText(host.File("appsettings.json"),
            "{ \"Elsa\": { \"Persistence\": { \"Provider\": \"Sqlite\" } } }");
        File.WriteAllText(host.File("shells.json"),
            "{ \"CShells\": { \"Shells\": { \"default\": { \"Configuration\": { \"Elsa\": { \"Persistence\": { \"Provider\": \"Sqlite\" } } } } } } }");

        Assert.False(HostResourceHints.Exist(host.Path, "Production"));
    }

    [Fact]
    public void Malformed_json_refuses_without_echoing_its_content_or_path()
    {
        File.WriteAllText(host.File("appsettings.json"), "{ secret-canary: invalid }");

        var refusal = Assert.Throws<WorkerRefusal>(() => HostResourceHints.Exist(host.Path, "Production"));

        Assert.Equal("configuration-context-invalid", refusal.Code);
        Assert.DoesNotContain(host.Path, refusal.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-canary", refusal.ToString(), StringComparison.Ordinal);
    }
}
