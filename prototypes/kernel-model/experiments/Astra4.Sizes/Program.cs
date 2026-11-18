// Gate 2, fourth Astra High review (#4229): size and identity probes for R4-M1, R4-M3 and R4-M6.
// Measures existing prototype behaviour with the real code, and the proposed compact forms beside it.
// The proposed digest encoding below lives in this probe only; no prototype default changes.
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.Native;
using Kernel.Runtime;
using Kernel.Workflows;

var failures = 0;
void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) failures++; }
static int Bytes(string s) => Encoding.UTF8.GetByteCount(s);
static string Rep(char c, int n) => new(c, n);

// ---- R4-M1: today's EventMatch.Encode and event wire id, against the proposed fixed digests ----------------------
Console.WriteLine("R4-M1 event match (existing EventMatch.Encode = Json.Write with DurableKeyJson, Store.cs 230-239)");
var witness = new EventMatch("t_k", "e1", "1/t_i", Rep('\u0001', 800), 1);
Console.WriteLine($"  reviewer-shaped witness (wait = 800 x U+0001, short key/event/instance): Match {Bytes(witness.Encode())} bytes");
const int N = 1024;
var tenant = Rep('a', 64);
string Q(string name) => tenant + "_" + name;
// Worst admitted names at N: control characters are one WTF-8 byte and six bytes once JSON escapes them.
var ctl = Rep('\u0001', N);
var lt = Rep('<', N);                               // ASCII, also escaped by the default encoder (<)
var cjk = Rep('界', N / 3);                         // three WTF-8 bytes, six escaped
var astral = string.Concat(Enumerable.Repeat("🌐", N / 4)); // four WTF-8 bytes, twelve escaped
foreach (var (label, name) in new[] { ("U+0001", ctl), ("'<'", lt), ("U+754C", cjk), ("U+1F310", astral) })
{
    var m = new EventMatch(Q(name), name, "1023/" + Q(name), name, long.MaxValue);
    var wire = FormattableString.Invariant($"evq:{name.Length}:{name}:{CreationProtocol.Origin("1023/" + Q(name), long.MaxValue)}:{name}");
    Console.WriteLine($"  four names of {Bytes(name)} WTF-8 bytes of {label}: Match {Bytes(m.Encode())} bytes; evq wire id {Bytes(wire)} bytes");
}
var worst = new EventMatch(Q(ctl), ctl, "1023/" + Q(ctl), ctl, long.MaxValue).Encode();
Check(Bytes(worst) > 4 * N + 192, $"today's worst Match ({Bytes(worst)}) exceeds the page's 4N + 192 = {4 * N + 192}");
Check(Bytes(worst) > 24 * 1024, $"today's worst Match alone exceeds M = 24,576 ({Bytes(worst)})");

// Proposed: a versioned canonical encoding (domain tag, version, then each part as a 4-byte big-endian count of its
// UTF-16 code units followed by the units big-endian, or a long as 8 bytes), hashed with SHA-256, written as hex.
static byte[] Canonical(byte domain, params object[] parts)
{
    var buffer = new List<byte> { domain, 1 };
    foreach (var part in parts)
    {
        if (part is long l) { var b = new byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(b, l); buffer.AddRange(b); continue; }
        var units = DurableKeyJson.CodeUnits((string)part);
        var n = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(n, units.Length / 2);
        buffer.AddRange(n); buffer.AddRange(units);
    }
    return buffer.ToArray();
}
static string Hex(byte[] canonical) => Convert.ToHexStringLower(SHA256.HashData(canonical));
static string MatchDigest(string qualifiedKey, string eventId) => Hex(Canonical((byte)'M', qualifiedKey, eventId));
static string DeliveryId(string eventId, string instance, long creation, string wait) => "ev:" + Hex(Canonical((byte)'D', eventId, instance, creation, wait));

var dm = MatchDigest(Q(ctl), ctl);
var did = DeliveryId(ctl, "1023/" + Q(ctl), long.MaxValue, ctl);
Console.WriteLine($"  proposed Match digest: {Bytes(dm)} bytes; proposed delivery id: {Bytes(did)} bytes, whatever the inputs");
Check(Bytes(dm) == 64 && Bytes(MatchDigest("t_k", "e1")) == 64, "Match is 64 ASCII bytes for the shortest and the widest inputs");
Check(Bytes(did) == 67 && Bytes(DeliveryId("e1", "1/t_i", 1, "w")) == 67, "delivery id is 67 ASCII bytes for the shortest and the widest inputs");
Check(!Canonical((byte)'M', "a:b", "c").SequenceEqual(Canonical((byte)'M', "a", "b:c")), "length prefixes keep ('a:b','c') and ('a','b:c') apart");
Check(!Canonical((byte)'M', "x", "y").SequenceEqual(Canonical((byte)'D', "x", "y")), "the domain tag keeps a Match and a delivery id apart");
Check(MatchDigest("t_high\uD800", "e") != MatchDigest("t_high�", "e"), "code units keep an unpaired unit apart from U+FFFD (a defence; admission refuses the unit)");
// Two waits of one instance on one key: one delivery id each; two keys on one wait: one Match each (B2-3's distinct handles).
Check(DeliveryId("e", "1/i", 1, "w1") != DeliveryId("e", "1/i", 1, "w2") && MatchDigest("t_k1", "e") != MatchDigest("t_k2", "e"),
    "(Id, Match) separates two waits on one key and one wait on two keys");
// The components ride in the delivery's payload ahead of the event's payload: a length-prefixed header, no escaping.
// Proposed layout (this probe only): "1|", then for the key, the event id and the wait: a decimal count of UTF-16
// units, ':', and the units themselves; then a presence flag, '0' for no payload (nothing follows) or '1' followed by
// the event's payload unchanged, so a null payload and an empty one stay apart.
static string Header(string? payload, params string[] parts) =>
    "1|" + string.Concat(parts.Select(p => FormattableString.Invariant($"{p.Length}:{p}"))) + (payload is null ? "0" : "1" + payload);
static (string[] Parts, string? Payload) Strip(string stored)
{
    if (!stored.StartsWith("1|", StringComparison.Ordinal)) throw new FormatException("no header");
    var at = 2; var parts = new string[3];
    for (var i = 0; i < 3; i++)
    {
        var colon = stored.IndexOf(':', at);
        var n = int.Parse(stored.AsSpan(at, colon - at), System.Globalization.CultureInfo.InvariantCulture);
        parts[i] = stored.Substring(colon + 1, n); at = colon + 1 + n;
    }
    return stored[at] switch
    {
        '0' when stored.Length == at + 1 => (parts, null),
        '1' => (parts, stored[(at + 1)..]),
        _ => throw new FormatException("bad presence flag"),
    };
}
foreach (var (label, name) in new[] { ("ASCII", Rep('k', N)), ("U+0001", ctl), ("'<'", lt) })
{
    var h = Header("", Q(name), name, name);
    Console.WriteLine($"  delivery header for key, event id and wait of {N} bytes of {label}: {Bytes(h)} bytes ({h.Length * 2} in UTF-16)");
}
var widest = Header("", Q(ctl), ctl, ctl);
Check(Bytes(widest) <= 40 * 1024 && widest.Length * 2 <= 40 * 1024, "the header fits the proposed reserve C = 40 KiB, in WTF-8 and in UTF-16");
var published = "{\"v\":\"<\u0001>\\\\ \\\"q\\\" " + Rep('\u0001', 64) + "\"}";
var (back, unchanged) = Strip(Header(published, Q("k"), "e1", "w1"));
Check(unchanged == published && back.SequenceEqual(new[] { Q("k"), "e1", "w1" }), "stripping the header returns the published payload unchanged, escapable characters included");
var (_, none) = Strip(Header(null, Q("k"), "e1", "w1"));
var (_, empty) = Strip(Header("", Q("k"), "e1", "w1"));
Console.WriteLine($"  null payload: stripped {(none is null ? "null" : "a string")}, Values.Parse -> {(Values.Parse(none) is null ? "null" : "a value")}; " +
    $"empty payload: stripped \"{empty}\", Values.Parse -> {Values.Parse(empty)?.ToJsonString() ?? "null"}");
Check(none is null && empty == "" && Values.Parse(none) is null && Values.Parse(empty)?.ToJsonString() == "\"\"",
    "the presence flag keeps a null payload (the model gets null) apart from an empty one (the model gets the JSON string \"\")");
Check(Header(null, "k", "e", "w") != Header("", "k", "e", "w"), "null and empty payloads encode differently");

// ---- R4-M2: a subscription's MongoDB write at the name limits, with the real BSON encoder -----------------------
Console.WriteLine("R4-M2 subscription write on MongoDB (MongoDB.Bson 3.9.0; shape of MongoStore.ToDocument, MongoStore.cs 648-654)");
var sKey = Q(Rep('k', N)); var sInst = "1023/" + Q(Rep('i', N)); var sWait = Rep('w', N);
MongoDB.Bson.BsonDocument SubId() => new() { { "k", sKey }, { "i", sInst }, { "w", sWait } };
var subDoc = new MongoDB.Bson.BsonDocument { { "_id", SubId() }, { "key", sKey }, { "instance", sInst }, { "wait", sWait },
    { "partition", 1023 }, { "start", MongoDB.Bson.BsonNull.Value }, { "tenant", tenant }, { "creation", long.MaxValue }, { "keep", true } };
var docBytes = MongoDB.Bson.BsonExtensionMethods.ToBson(subDoc).Length;
var insertOp = new MongoDB.Bson.BsonDocument { { "insert", 0 }, { "document", subDoc } };     // one bulkWrite insert entry
var insertBytes = MongoDB.Bson.BsonExtensionMethods.ToBson(insertOp).Length;
var upsertOp = new MongoDB.Bson.BsonDocument { { "update", 0 }, { "filter", new MongoDB.Bson.BsonDocument("_id", SubId()) }, { "updateMods", subDoc }, { "upsert", true } };
var upsertBytes = MongoDB.Bson.BsonExtensionMethods.ToBson(upsertOp).Length;
Console.WriteLine($"  stored document {docBytes} bytes; as a bulkWrite insert entry {insertBytes} bytes; as today's replace-upsert entry {upsertBytes} bytes");
Check(insertBytes <= 7 * 1024, "the insert entry fits the proposed allowance of 7,168 bytes per subscription");
Check(upsertBytes > 7 * 1024, "today's replace-upsert, which repeats _id in its filter, does not");
Console.WriteLine($"  subscriptions in B_plan at 7,168 bytes each: {25165824 / 7168}");

// ---- R4-M6: the placement hash over two spellings one collation calls equal -------------------------------------
Console.WriteLine("R4-M6 placement (existing InstanceRef.Spread, Addresses.cs 22-27, over UTF-16 code units)");
foreach (var (x, y) in new[] { ("a_Invoice", "a_invoice"), ("a_café", "a_cafe"), ("a_INVOICE-9", "a_invoice-9") })
    Console.WriteLine($"  Spread('{x}', 1024) = {InstanceRef.Spread(x, 1024)}, Spread('{y}', 1024) = {InstanceRef.Spread(y, 1024)}");
Check(InstanceRef.Spread("a_Invoice", 1024) != InstanceRef.Spread("a_invoice", 1024), "case aliases place differently, so {vp}/ differs");

// ---- R4-M3: what Native keeps at a background arm, and what a restart needs ------------------------------------
Console.WriteLine("R4-M3 Native background job (existing NativeModel.cs 129, 185-195, 347-355)");
var wf = Wf.Workflow("job", Wf.Sequence("root",
    Wf.Set("s1", "svc", "alpha"),
    Wf.Parallel("p", null, Wf.Node<EffectEcho>("c", ("Service", Binding.Var("svc"))), Wf.Set("s2", "svc", "beta"))));
var model = new NativeModel(new WorkflowRegistry().Add(wf));
var input = NativeModel.Spec("job");
var turn = DispatchProxy.Create<ITurn, Host>();
var host = (Host)(object)turn;
host.State = (NativeState)model.CreateState(input);
host.Inbox = [new Message("start", host.Actor.Ref, Kinds.Start, Start: input)];
await model.TurnAsync(turn, default);
var (jobKey, jobArgs) = host.Jobs.Single();
var waitEntry = host.State.Waits[jobKey];
var waitJson = Json.Write(waitEntry);
var argsJson = Json.Write(jobArgs);
var stateJson = Json.Write(host.State);
Console.WriteLine($"  WaitEntry for the job: {waitJson}");
Console.WriteLine($"  ticket arguments (BackgroundArgs): {argsJson}");
var armed = Json.Read<BackgroundArgs>(argsJson);
Check(!waitJson.Contains("alpha") && !waitJson.Contains(armed.EffectKey!), "WaitEntry holds frame, kind and key only: no input, path or effect key");
Check(armed.Inputs["Service"]?.GetValue<string>() == "alpha", "the ticket holds the input evaluated at arm (Service = alpha)");
Check(stateJson.Contains("\"beta\"") && !stateJson.Contains("\"alpha\""), "the binding source has since changed: the state holds svc = beta and no alpha");
IJob Ticket(string? arguments) => new TestJob(host.Actor.Ref, jobKey, arguments, host.Actor.Creation);
var fromSpec = (BackgroundResult)(await model.ExecuteJobAsync(Ticket(argsJson), default))!;
Console.WriteLine($"  the real background activity, run from the arm-time arguments: {fromSpec.Result?.ToJsonString()}");
Check(fromSpec.Result?.GetValue<string>() == $"alpha|{armed.EffectKey}", "it receives the arm-time input and the original effect key");
var fresh = armed with { Inputs = new JsonObject { ["Service"] = "beta" } };
var fromFresh = (BackgroundResult)(await model.ExecuteJobAsync(Ticket(Json.Write(fresh)), default))!;
Check(fromFresh.Result?.GetValue<string>() == $"beta|{armed.EffectKey}", "a fresh evaluation against today's state would send beta: a different invocation");
async Task<string> Fails(string? arguments)
{
    try { await model.ExecuteJobAsync(Ticket(arguments), default); return "no exception"; }
    catch (Exception e) { return e.GetType().Name; }
}
var marker = await Fails(null);
var noKey = await Fails(Json.Write(armed with { EffectKey = null }));
Console.WriteLine($"  a payload-free marker (no arguments): {marker}; arguments without an effect key: {noKey}");
Check(marker != "no exception" && noKey == "InvalidOperationException", "a ticket without the arm-time arguments cannot run");
Console.WriteLine($"  encoded sizes: WaitEntry {Bytes(waitJson)} bytes, BackgroundArgs {Bytes(argsJson)} bytes");

Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

/// <summary>A background leaf that reports what it received: the activity code of a Background node, run by ExecuteJobAsync.</summary>
[Background]
public sealed class EffectEcho : Activity<string>
{
    [Input] public string Service { get; set; } = "";
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => new(Complete($"{Service}|{context.EffectKey}"));
}

public sealed record TestJob(InstanceRef Instance, string Key, string? Arguments, long Creation) : IJob;

/// <summary>A minimal ITurn host, after the reviewer's DispatchProxy probe: records sends and job starts.</summary>
public class Host : DispatchProxy
{
    public NativeState State = new();
    public Message[] Inbox = [];
    public InstanceInfo Actor = new(new InstanceRef("0/subject"), "native", "job", null, 1);
    public List<(string Key, object? Arguments)> Jobs = [];
    private int _sent;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "get_State": return State;
            case "set_State": State = (NativeState)args![0]!; return null;
            case "get_Instance": return Actor;
            case "get_Inbox": return Inbox;
            case "get_Now": return 1000L;
            case "get_Ambient": case "get_Clock": return null;
            case "Redelivered": return false;
            case "StartJob": Jobs.Add(((string)args![0]!, args[1])); return null;
            case "Send": return FormattableString.Invariant($"c{Actor.Ref.Id.Length}_{Actor.Ref.Id}_{Actor.Creation}#1.{_sent++:D4}");
            case "SetStatus": case "Record": case "Recall": case "Emit": case "Hold": case "CancelJob": return null;
            default: throw new InvalidOperationException("unexpected turn call " + method.Name);
        }
    }
}
