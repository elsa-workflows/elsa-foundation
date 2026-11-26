using System.Text;
using Kernel.EntityFramework;
using Kernel.Store.Conformance;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

/// <summary>Measures the persisted text values of a fenced instance update using the database's own byte counts.</summary>
internal static class CheckpointByteChecks
{
    internal static async Task Run(string engine, Func<KernelDb> create, IKernelStore store, IKernelStore reader)
    {
        var lease = TimeSpan.FromMinutes(2);
        var fence = await store.TakeAsync(0, "checkpoint-bytes", lease)
            ?? throw new InvalidOperationException("The checkpoint fixture could not acquire its fence.");
        var position = fence.Position;
        var utf16 = engine == "sqlserver";
        var encoding = utf16 ? Encoding.Unicode : Encoding.UTF8;

        // Leave room for other fields in the 16 MiB logical budget and exceed it in UTF-16.
        await Measure("ascii", new string('x', 9 * 1024 * 1024), "handle");
        await Measure("unicode", "é界🌐", "handle/é界🌐");
        await Measure("escaped", StoredState.Family + "opaque/🌐", "handle");

        async Task Measure(string name, string result, string handle)
        {
            var id = "0/checkpoint-" + name;
            var live = Make.Row(id, 1, "active");
            var ended = live with
            {
                State = result, Status = InstanceStatus.Finished, Revision = 2,
                Model = "m🌐", Definition = "d界", CustomHandles = $"[\"{handle}\"]",
            };
            await Write(live, null);
            await Write(ended, 1);
            var loaded = (await reader.LoadAsync([id])).Single();
            if (loaded != ended || (await reader.ReadJournalOfAsync(0, id, 0)).Count != 2)
                throw new InvalidOperationException($"{name}: fresh read did not see the exact checkpoint and journal.");

            var storedState = StoredState.Encode(ended);
            string[] values = [storedState, ended.CustomHandles!, ended.Model, ended.Definition, ended.Id];
            var expected = values.Select(value => (long)encoding.GetByteCount(value)).ToArray();
            var utf8Reference = values.Sum(value => (long)Encoding.UTF8.GetByteCount(value));
            var actual = await ReadPhysicalBytes(id);
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException($"{name}: persisted byte counts [{string.Join(", ", actual)}], expected [{string.Join(", ", expected)}].");
            Console.WriteLine($"{name}: UTF-8 reference {utf8Reference}, stored text {actual.Sum()}, stored state {actual[0]}, framing {encoding.GetByteCount(storedState) - encoding.GetByteCount(result)}");
        }

        async Task Write(InstanceRow row, long? expected)
        {
            var commit = await store.CommitAsync(new(0, fence.Epoch, position, lease, false,
                [Make.Turn(row.Id, row, expected)], [], []));
            if (!commit.Landed)
                throw new InvalidOperationException($"{row.Id}: checkpoint revision {row.Revision} did not land.");
            position++;
        }

        async Task<long[]> ReadPhysicalBytes(string id)
        {
            await using var db = create();
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = engine switch
            {
                "sqlserver" => """
                    SELECT DATALENGTH([State]), DATALENGTH([CustomHandles]), DATALENGTH([Model]), DATALENGTH([Definition]), DATALENGTH([Id])
                    FROM [Instances] WHERE [Id] = @id
                    """,
                "mysql" => """
                    SELECT OCTET_LENGTH(`State`), OCTET_LENGTH(`CustomHandles`), OCTET_LENGTH(`Model`), OCTET_LENGTH(`Definition`), OCTET_LENGTH(`Id`)
                    FROM `Instances` WHERE `Id` = @id
                    """,
                "postgres" => """
                    SELECT octet_length("State"), octet_length("CustomHandles"), octet_length("Model"), octet_length("Definition"), octet_length("Id")
                    FROM "Instances" WHERE "Id" = @id
                    """,
                "sqlite" => """
                    SELECT length(CAST("State" AS BLOB)), length(CAST("CustomHandles" AS BLOB)), length(CAST("Model" AS BLOB)),
                           length(CAST("Definition" AS BLOB)), length(CAST("Id" AS BLOB))
                    FROM "Instances" WHERE "Id" = @id
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(engine)),
            };
            var parameter = command.CreateParameter();
            (parameter.ParameterName, parameter.Value) = ("@id", id);
            command.Parameters.Add(parameter);
            await using var rows = await command.ExecuteReaderAsync();
            if (!await rows.ReadAsync())
                throw new InvalidOperationException($"No persisted checkpoint for {id}.");
            return Enumerable.Range(0, rows.FieldCount).Select(index => Convert.ToInt64(rows.GetValue(index))).ToArray();
        }
    }
}
