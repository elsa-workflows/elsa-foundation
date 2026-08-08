using System.Text.Json;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Spec 150 Part E. The workbench is the everything-image: any feature should be activatable through
/// shells.json alone. A feature whose assembly the host does not reference cannot be enabled at all —
/// the shell only logs "requested N feature(s) that are not available in the runtime feature catalog"
/// and the capability silently never appears.
/// </summary>
/// <remarks>
/// This pins the decision <em>rule</em>, not a snapshot list: every fragment that carries consumer-visible
/// authoring surface (activities, structure kinds, expression types, intrinsics) must ship in the
/// everything-image. Alternative providers of a role the host already fills — MongoDb/SqlServer
/// persistence, EF Core diagnostics persistence — carry no authoring surface, so they stay omissible
/// without this test objecting, and a newly added provider will not fail the build spuriously. What
/// <em>will</em> fail is adding a new activity or expression package and forgetting to ship it, which is
/// exactly the omission that cost consumers RunJavaScript and Liquid.
/// </remarks>
public sealed class HostAuthoringSurfaceTests
{
    private const string EverythingImage = "Elsa.Workbench";

    private static string ContractsRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent!)
        {
            if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
                return Path.Combine(current.FullName, "docs", "contracts");
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void The_everything_image_ships_every_fragment_that_carries_authoring_surface()
    {
        var contractsRoot = ContractsRoot();
        using var hosts = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(contractsRoot, "hosts.json")));

        var shipped = hosts.RootElement.GetProperty("hosts").EnumerateArray()
            .Single(host => host.GetProperty("host").GetString() == EverythingImage)
            .GetProperty("fragments").EnumerateArray()
            .Select(fragment => fragment.GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var fragmentPath in Directory.EnumerateFiles(Path.Combine(contractsRoot, "fragments"), "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(fragmentPath);
            if (shipped.Contains(name))
                continue;

            using var fragment = JsonDocument.Parse(File.ReadAllBytes(fragmentPath));
            if (CarriesAuthoringSurface(fragment.RootElement))
                missing.Add(name);
        }

        Assert.True(missing.Count == 0,
            $"{EverythingImage} does not reference these assemblies, so their features cannot be enabled through shells.json, " +
            $"yet they publish consumer-visible authoring surface: {string.Join(", ", missing.Order(StringComparer.Ordinal))}. " +
            "Add the ProjectReference, or record the omission as deliberate in the csproj comment and relax this rule.");
    }

    private static bool CarriesAuthoringSurface(JsonElement fragment) =>
        HasItems(fragment, "activities") ||
        HasItems(fragment, "structures") ||
        HasItems(fragment, "intrinsics") ||
        (fragment.TryGetProperty("expressions", out var expressions) &&
         expressions.ValueKind == JsonValueKind.Object &&
         HasItems(expressions, "descriptors"));

    private static bool HasItems(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Array &&
        value.GetArrayLength() > 0;
}
