using System.Text.Json;

namespace Elsa.Integrations.Tests;

public sealed class TemplateTests
{
    [Fact]
    public void Dotnet_new_template_is_elsa_connector()
    {
        var path = Path.GetFullPath(Path.Combine(RepoRoot(), "Extensions", "Integrations", "templates", "elsa-connector", ".template.config", "template.json"));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("elsa-connector", document.RootElement.GetProperty("shortName").GetString());
        Assert.Equal("Elsa.Integrations.Connector", document.RootElement.GetProperty("identity").GetString());
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
