using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeRecoveryBindingHashTests
{
    private const string Freeze = "JSON hashes freeze at Elsa 4's first stable release; before then regenerate goldens and fixtures together, never add an old-hash verifier.";
    private const string Purpose = "ef-runtime-recovery-liveness-v1";

    public static IEnumerable<object[]> Vectors()
    {
        using var stream = typeof(RuntimeRecoveryBindingHashTests).Assembly.GetManifestResourceStream("S9NestedBindingVectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("recoveryBindings").EnumerateArray().Select(vector => new object[] { vector.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Real_recovery_scan_binding_and_cursor_signature_pin_every_scan_field_without_JSON_text_nesting(JsonElement vector)
    {
        long Ticks(string field) => long.Parse(vector.GetProperty(field).GetString()!, CultureInfo.InvariantCulture);
        var now = new DateTimeOffset(Ticks("utcTicks"), TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(vector.GetProperty("offsetMinutes").GetInt32()));
        var request = new RuntimeRecoveryScanRequest(now, TimeSpan.FromTicks(Ticks("leaseTicks")), TimeSpan.FromTicks(Ticks("heartbeatTicks")),
            vector.GetProperty("limit").GetInt32(), vector.GetProperty("owner").GetString());
        var bindingMethod = typeof(EfExecutionLivenessStateStore).GetMethod("RecoveryBinding", BindingFlags.Static | BindingFlags.NonPublic)!;
        var binding = (string)bindingMethod.Invoke(null, [vector.GetProperty("scope").GetString(), request])!;
        var name = vector.GetProperty("name").GetString();
        Assert.True(binding == vector.GetProperty("sha256").GetString(), $"{name}: recovery binding {binding} differs. {Freeze}");
        var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));
        using var db = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=:memory:").Options);
        var store = new EfExecutionLivenessStateStore(db, new Accessor(), codec);
        var encode = typeof(EfExecutionLivenessStateStore).GetMethod("EncodeRecoveryCursor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var eligibleAt = new DateTimeOffset(Ticks("eligibleUtcTicks"), TimeSpan.Zero);
        var token = (string)encode.Invoke(store, [binding, eligibleAt, new ExecutionLivenessState("liveness", "workflow", null, null, null, null)])!;
        Assert.True(token.Split('.')[^1] == vector.GetProperty("cursorSignature").GetString(), $"{name}: recovery cursor signature differs. {Freeze}");
        using var payload = JsonDocument.Parse(codec.DecodeJson(Purpose, token));
        var canonical = Encoding.UTF8.GetString(JsonHashInput.CanonicalBytes(payload.RootElement));
        Assert.True(canonical == vector.GetProperty("cursorCanonical").GetString(), $"{name}: recovery cursor preimage differs. {Freeze}");
        Assert.Equal(binding, payload.RootElement.GetProperty("Binding").GetString());
        Assert.Throws<ArgumentException>(() => codec.Decode(Purpose, token));
        Assert.Throws<ArgumentException>(() => codec.DecodeJson(Purpose + "-other", token));
    }

    private sealed class Accessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
    }
}
