using System.Text.Json;
using Elsa.Integrations.Sdk.Design;

namespace Elsa.Integrations.Tests;

public sealed class TemplateAndDesignTests
{
    [Fact]
    public void Dotnet_new_template_is_elsa_connector()
    {
        var path = Path.GetFullPath(Path.Combine(RepoRoot(), "Extensions", "Integrations", "templates", "elsa-connector", ".template.config", "template.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("elsa-connector", document.RootElement.GetProperty("shortName").GetString());
        Assert.Equal("Elsa.Integrations.Connector", document.RootElement.GetProperty("identity").GetString());
    }

    [Fact]
    public void Connection_type_descriptor_is_design_only()
    {
        var descriptor = new ConnectionTypeDescriptor
        {
            Name = "http",
            DisplayName = "HTTP",
            EgressAllowlist = ["*.example.com"],
            Schemes =
            [
                new AuthSchemeDescriptor
                {
                    Name = "bearer",
                    DisplayName = "Bearer",
                    Fields = [new ConnectionFieldDescriptor { Name = "accessToken", DisplayName = "Access token", IsSensitive = true }]
                }
            ]
        };

        Assert.Equal("http", descriptor.Name);
        Assert.Contains("Design", typeof(ConnectionTypeDescriptor).Namespace);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
