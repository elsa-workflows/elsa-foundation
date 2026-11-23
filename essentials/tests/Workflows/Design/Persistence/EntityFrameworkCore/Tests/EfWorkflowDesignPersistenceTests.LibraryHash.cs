using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfWorkflowDesignPersistenceTests
{
    public static IEnumerable<object?[]> LibraryBindingVectors()
    {
        using var stream = typeof(EfWorkflowDesignPersistenceTests).Assembly.GetManifestResourceStream("S9WorkflowLibraryBindingVectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(vector => new object?[]
        {
            vector.GetProperty("name").GetString(), vector.GetProperty("kind").GetString(),
            vector.GetProperty("tenant").GetString(), vector.GetProperty("subject").GetString(),
            vector.GetProperty("material").GetString(), vector.GetProperty("sha256").GetString()
        }).ToArray();
    }

    [Theory]
    [MemberData(nameof(LibraryBindingVectors))]
    public async Task Library_query_bindings_pin_canonical_identity_and_scope(string name, string kind, string tenant, string? subject, string material, string expected)
    {
        await using var host = LibraryHost("Data Source=:memory:", tenant);
        await using var scope = host.CreateAsyncScope();
        var library = scope.ServiceProvider.GetRequiredService<IWorkflowLibraryStore>();
        var binding = library.GetType().GetMethod("Binding", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var document = JsonDocument.Parse(material);
        var actual = Assert.IsType<string>(binding.Invoke(library, [kind, document.RootElement.Clone(), subject]));
        Assert.True(expected == actual,
            $"{name}: expected {expected}, actual {actual}. JSON hashes freeze at Elsa 4's first stable release; before then regenerate the golden and fixtures together, never add an old-hash verifier.");
    }
}
