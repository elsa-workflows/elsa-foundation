using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Workflows.Tests.Support;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Secrets.Workflows.Tests;

/// <summary>
/// The canary's encoded scanner finds a planted value in every form a surface can store it in, at every byte alignment,
/// in a SQLite database's write-ahead log as well as in its main file, and reports nothing for content that does not
/// hold the value (spec 188, T080).
/// </summary>
public sealed class CanaryScannerTests : IDisposable
{
    private readonly string _value = $"canaryscan{Guid.NewGuid():N}";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("elsa-canary-scanner-");

    public static TheoryData<int> Alignments => new() { 0, 1, 2, 3, 4, 5 };

    [Fact]
    public void The_raw_UTF8_and_UTF16LE_bytes_are_found()
    {
        AssertFoundAs("utf8", [.. "prefix:"u8, .. Encoding.UTF8.GetBytes(_value), .. ":suffix"u8]);
        AssertFoundAs("utf16le", [.. Encoding.Unicode.GetBytes("prefix:"), .. Encoding.Unicode.GetBytes(_value), .. Encoding.Unicode.GetBytes(":suffix")]);
    }

    [Fact]
    public void A_value_the_JSON_encoders_escape_is_found_as_each_of_them_writes_it()
    {
        // The canary's own values are plain words, which no JSON encoder escapes; this one shows the escaped form is searched.
        var escapable = $"canary<+>{Guid.NewGuid():N}";
        var defaultJson = JsonSerializer.Serialize(new { held = escapable });
        Assert.DoesNotContain(escapable, defaultJson, StringComparison.Ordinal);

        Assert.Contains(CanaryScanner.Find(defaultJson, escapable, "default-json"), hit => hit.Form == "json");
    }

    [Theory]
    [MemberData(nameof(Alignments))]
    public void A_UTF8_value_inside_a_longer_Base64_string_is_found_at_its_alignment(int prefixLength)
    {
        var encoded = Convert.ToBase64String([.. Filler(prefixLength), .. Encoding.UTF8.GetBytes(_value), .. Filler(7)]);

        Assert.DoesNotContain(_value, encoded, StringComparison.Ordinal);
        AssertFoundAs($"base64-utf8-{prefixLength % 3}", Encoding.ASCII.GetBytes(encoded));
    }

    [Theory]
    [MemberData(nameof(Alignments))]
    public void A_UTF16LE_value_inside_a_longer_Base64_string_is_found_at_its_alignment(int prefixLength)
    {
        // As the EF runtime stores write a CLR string: Base64 of its UTF-16LE code units, the value somewhere inside.
        var encoded = Convert.ToBase64String([.. Filler(prefixLength), .. Encoding.Unicode.GetBytes(_value), .. Filler(5)]);

        AssertFoundAs($"base64-utf16le-{prefixLength % 3}", Encoding.ASCII.GetBytes(encoded));
    }

    [Fact]
    public void A_value_in_a_fault_message_the_EF_stores_encode_is_found()
    {
        var stored = EfRelationalIdentity.Encode($"The activity failed while it held {_value}.");

        Assert.DoesNotContain(_value, stored, StringComparison.Ordinal);
        Assert.Contains(CanaryScanner.Find(stored, _value, "fault"), hit => hit.Form.StartsWith("base64-utf16le-", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_still_in_a_SQLite_write_ahead_log_is_found_in_the_wal_file()
    {
        var database = Path.Join(_directory.FullName, "planted.db");
        using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        connection.Open();
        Execute(connection, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE rows (content TEXT);");
        Execute(connection, $"INSERT INTO rows VALUES ('{EfRelationalIdentity.Encode(_value)}');");

        // The connection stays open, so nothing has checkpointed the row into the main file yet.
        var hits = CanaryScanner.ScanDirectory(_directory.FullName, _value);

        Assert.Contains(hits, hit => hit.Location.EndsWith("planted.db-wal", StringComparison.Ordinal) && hit.Form.StartsWith("base64-utf16le-", StringComparison.Ordinal));
    }

    [Fact]
    public void A_clean_file_holding_a_value_of_the_same_shape_reports_nothing()
    {
        var other = $"canaryscan{Guid.NewGuid():N}";
        var content = string.Join('\n', other, EfRelationalIdentity.Encode(other), Convert.ToBase64String(Encoding.UTF8.GetBytes(other)));
        File.WriteAllText(Path.Join(_directory.FullName, "clean.json"), content);

        Assert.Empty(CanaryScanner.ScanDirectory(_directory.FullName, _value));
    }

    [Fact]
    public void A_directory_with_nothing_to_scan_is_refused_rather_than_reported_clean() =>
        Assert.Throws<InvalidOperationException>(() => CanaryScanner.ScanDirectory(_directory.FullName, _value));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    /// <summary>Writes <paramref name="content"/> to a file and asserts the scan of its directory finds the value as <paramref name="form"/>.</summary>
    private void AssertFoundAs(string form, byte[] content)
    {
        File.WriteAllBytes(Path.Join(_directory.FullName, $"{Guid.NewGuid():N}.bin"), content);

        Assert.Contains(CanaryScanner.ScanDirectory(_directory.FullName, _value), hit => hit.Form == form);
    }

    private static byte[] Filler(int length) => Enumerable.Range(0, length).Select(index => (byte)(0x5A + index)).ToArray();

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
