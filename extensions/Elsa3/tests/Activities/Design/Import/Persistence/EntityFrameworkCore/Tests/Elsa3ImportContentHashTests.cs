using System.Reflection;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.Core;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Entities;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests;

public sealed class Elsa3ImportContentHashTests
{
    private const string Freeze = "JSON hashes freeze at Elsa 4's first stable release; before then regenerate goldens and fixtures together, never add an old-hash verifier.";
    private static readonly Type Codec = typeof(EfReusableActivityImportOperationStore).Assembly.GetType(
        "Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores.Elsa3ImportRecordCodec", throwOnError: true)!;

    public static IEnumerable<object[]> Vectors()
    {
        using var stream = typeof(Elsa3ImportContentHashTests).Assembly.GetManifestResourceStream("S9Elsa3ImportContentVectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("vectors").EnumerateArray().Select(vector => new object[]
        {
            vector.GetProperty("name").GetString()!, vector.GetProperty("kind").GetString()!, vector.GetProperty("writer").GetBoolean(),
            vector.GetProperty("material").GetString()!, vector.GetProperty("canonical").GetString()!, vector.GetProperty("sha256").GetString()!
        }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Real_import_record_writers_and_readers_pin_canonical_content_without_rewriting_received_text(
        string name, string kind, bool writer, string material, string canonical, string expected)
    {
        var record = Write(kind, material);
        if (writer)
        {
            var (json, hash) = Content(record);
            using var written = JsonDocument.Parse(json);
            var actualCanonical = Encoding.UTF8.GetString(JsonHashInput.CanonicalBytes(written.RootElement));
            Assert.True(canonical == actualCanonical, $"{name}: writer canonical preimage differs. {Freeze}");
            Assert.True(expected == hash, $"{name}: expected {expected}, actual {hash}. {Freeze}");
        }
        else
            SetContent(record, material, expected);
        var before = Content(record);
        object? decoded = null;
        var failure = Record.Exception(() => decoded = Read(kind, record, material));
        Assert.True(failure is null, $"{name}: retained-content reader golden failed: {failure}. {Freeze}");
        Assert.NotNull(decoded);
        Assert.Equal(before, Content(record));
    }

    [Theory]
    [InlineData("collection", "{")]
    [InlineData("collection", "{\"extra\":\"\\ud800\"}")]
    [InlineData("collection", "{\"\\ud800\":1}")]
    [InlineData("receipt", "{")]
    [InlineData("receipt", "{\"extra\":\"\\ud800\"}")]
    [InlineData("receipt", "{\"\\ud800\":1}")]
    public void Invalid_JSON_and_unpaired_surrogates_are_normalized_without_rewriting_received_content(string kind, string invalid)
    {
        var vector = Vectors().Single(vector => (string)vector[0] == kind + "-writer");
        var material = (string)vector[3];
        var record = Write(kind, material);
        SetContent(record, invalid, Content(record).Hash);
        var before = Content(record);
        var failure = Assert.Throws<TargetInvocationException>(() => Read(kind, record, material));
        var normalized = Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.True(normalized.InnerException is JsonException or InvalidOperationException, normalized.ToString());
        Assert.Equal(before, Content(record));
    }

    [Theory]
    [InlineData("collection")]
    [InlineData("receipt")]
    public void The_old_physical_text_hash_is_refused_without_rewriting_received_content(string kind)
    {
        var vector = Vectors().Single(vector => (string)vector[0] == kind + "-writer");
        var material = (string)vector[3];
        var record = Write(kind, material);
        var (json, _) = Content(record);
        SetContent(record, json, EfRelationalIdentity.Hash(json));
        var before = Content(record);
        var failure = Assert.Throws<TargetInvocationException>(() => Read(kind, record, material));
        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Equal(before, Content(record));
    }

    private static object Write(string kind, string material)
    {
        using var document = JsonDocument.Parse(material);
        if (kind == "collection")
        {
            var value = document.RootElement.GetProperty(kind).Deserialize<ReusableActivityImportCollectionHandle>(JsonBaseline.Options)!;
            return Codec.GetMethod("ToRecord", [typeof(ReusableActivityImportCollectionHandle)])!.Invoke(null, [value])!;
        }
        var receipt = document.RootElement.GetProperty(kind).Deserialize<ReusableActivityImportReceipt>(JsonBaseline.Options)!;
        return Codec.GetMethod("ToRecord", [typeof(ReusableActivityImportReceipt), typeof(string)])!.Invoke(null, [receipt, "commit-attempt"])!;
    }

    private static object? Read(string kind, object record, string material)
    {
        using var document = JsonDocument.Parse(material);
        var payload = document.RootElement.GetProperty(kind);
        var scope = payload.GetProperty("accessScope").Deserialize<ReusableActivityImportAccessScope>(JsonBaseline.Options)!;
        var identity = payload.GetProperty(kind == "collection" ? "handle" : "receiptId").GetString()!;
        return Codec.GetMethod(kind == "collection" ? "ReadCollection" : "ReadReceipt")!.Invoke(null, [record, identity, scope]);
    }

    private static (string Json, string Hash) Content(object record) => record switch
    {
        Elsa3ImportCollectionRecord row => (row.ContentJson, row.ContentHash),
        Elsa3ImportReceiptRecord row => (row.ContentJson, row.ContentHash),
        _ => throw new InvalidOperationException("Unexpected import record.")
    };

    private static void SetContent(object record, string json, string hash)
    {
        if (record is Elsa3ImportCollectionRecord collection) { collection.ContentJson = json; collection.ContentHash = hash; }
        else if (record is Elsa3ImportReceiptRecord receipt) { receipt.ContentJson = json; receipt.ContentHash = hash; }
        else throw new InvalidOperationException("Unexpected import record.");
    }
}
