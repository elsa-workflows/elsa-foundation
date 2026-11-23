using System.Text;
using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfWorkflowDesignPersistenceTests
{
    private const string DesignHashFreeze = "JSON hashes freeze at Elsa 4's first stable release; before then regenerate goldens and fixtures together, never add an old-hash verifier.";

    public static IEnumerable<object?[]> DesignOperationVectors()
    {
        using var stream = typeof(EfWorkflowDesignPersistenceTests).Assembly.GetManifestResourceStream("S9DesignOperationVectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("vectors").EnumerateArray().Select(vector => new object?[]
        {
            vector.GetProperty("name").GetString(), vector.GetProperty("operationKind").GetString(),
            vector.GetProperty("materialType").GetString(), vector.GetProperty("material").GetString(),
            vector.GetProperty("canonical").GetString(), vector.GetProperty("sha256").GetString()
        }).ToArray();
    }

    [Theory]
    [MemberData(nameof(DesignOperationVectors))]
    public async Task Real_operation_materials_and_atomic_markers_match_independent_golden_vectors(
        string name, string kind, string? materialType, string material, string canonical, string expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        using var document = JsonDocument.Parse(material);
        object request = document.RootElement;
        if (materialType is not null)
        {
            var type = typeof(EfDesignAtomicWriter).Assembly.GetType(
                "Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands." + materialType, throwOnError: true)!;
            if (document.RootElement.TryGetProperty("requestMaterial", out var nested))
                request = new
                {
                    requestMaterial = JsonSerializer.Deserialize(nested, type, JsonBaseline.Options)!,
                    folderId = document.RootElement.GetProperty("folderId").GetString(),
                    actorId = document.RootElement.GetProperty("actorId").GetString()
                };
            else
                request = JsonSerializer.Deserialize(document.RootElement, type, JsonBaseline.Options)!;
        }

        var actualCanonical = Encoding.UTF8.GetString(JsonHashInput.CanonicalBytes(JsonHashInput.SerializeToElement(request)));
        Assert.True(canonical == actualCanonical, $"{name}: operation material differs from its pinned canonical preimage. {DesignHashFreeze}");
        var isResult = kind.EndsWith(".result", StringComparison.Ordinal);
        await writer.ExecuteAsync(new(name), isResult ? kind[..^7] : kind,
            isResult ? new { DraftId = "draft" } : request, [DesignPersistenceUnitNames.Drafts],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<bool>.Accepted(true)));
        var marker = await db.Operations.AsNoTracking().SingleAsync();
        var actual = isResult ? marker.ResultFingerprint : marker.RequestFingerprint;
        Assert.True(expected == actual, $"{name}: expected {expected}, actual {actual}. {DesignHashFreeze}");
    }
}
