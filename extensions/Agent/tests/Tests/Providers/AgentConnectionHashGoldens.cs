using System.Globalization;
using System.Text.Json;
using Elsa.Connections.Core.Models;
using Xunit;

namespace Elsa.Agent.Tests.Providers;

public sealed partial class AgentConnectionProviderTests
{
    [Fact]
    public async Task Actual_provider_capabilities_pin_selected_fields_and_order_independent_hosts()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Elsa.Server.slnx"))) repository = repository.Parent;
        Assert.NotNull(repository);
        using var vectors = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(repository.FullName,
            "specs", "1207-json-data-integrity-baseline", "evidence", "agent-provider-hash-vectors.json")));
        foreach (var vector in vectors.RootElement.EnumerateArray())
        {
            var vendor = vector.GetProperty("vendor").GetString()!;
            var connection = new Resolved(vendor);
            connection.Values["maximumOutputTokens"] = vector.GetProperty("maximumOutputTokens").GetInt32().ToString(CultureInfo.InvariantCulture);
            connection.Values["temperatureSupported"] = vector.GetProperty("temperatureSupported").GetBoolean().ToString();
            connection.Hosts = AllowedHostsPolicy.Fixed(vector.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()!).ToArray());
            var provider = Provider(vendor, new Resolver(connection));
            var expected = vector.GetProperty("expected").GetString();
            Assert.True((await provider.GetCapabilitiesAsync(Model(vendor))).Fingerprint == expected,
                $"{vendor}/{vector.GetProperty("name").GetString()}: First-stable hash freeze requires an explicit identity/migration decision for capability changes.");
            connection.Version += 100;
            connection.IncarnationId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            Assert.True((await provider.GetCapabilitiesAsync(Model(vendor))).Fingerprint == expected,
                $"{vendor}/{vector.GetProperty("name").GetString()}: First-stable hash freeze: operational version/incarnation changes preserve the capability pin; changes require an explicit identity/migration decision.");
            Assert.Equal(0, connection.CredentialReads);
        }
    }
}
