using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

// Disposable lifecycle experiment. This is not an Elsa store implementation or a production API.
var directory = args[0];
Directory.CreateDirectory(directory);
var store = new ConversationPrototype(Path.Combine(directory, "conversation.db"));
await store.InitializeAsync();
var scope = new Scope("tenant-a", "copywriter-v1", "conversation-one");
await store.CreateAsync(scope);
var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }

// Independent connections contend for one exact conversation revision. No model work starts before admission.
var claims = await Task.WhenAll(Task.Run(() => store.BeginAsync(scope, "turn-a", 0)), Task.Run(() => store.BeginAsync(scope, "turn-b", 0)));
Check(claims.Count(x => x) == 1, "one winner for two turns at the same conversation revision");
var turn = claims[0] ? "turn-a" : "turn-b";
Check(await store.ReadAsync(scope with { Tenant = "tenant-b" }) is null, "foreign tenant cannot address session");
Check(await store.ReadAsync(scope with { AgentVersion = "copywriter-v2" }) is null, "different agent version cannot silently continue session");
var client = new Client();
var agent = new ChatClientAgent(client, instructions: "Write the supplied brief.", name: "copywriter-v1");
var session = await agent.CreateSessionAsync();
var response = await agent.RunAsync("moss gardens", session);
var serialized = await agent.SerializeSessionAsync(session);
Check(await store.CompleteAsync(scope, turn, 0, serialized.GetRawText(), response.Text), "session and accepted result commit at same prototype revision");
Check(!await store.CompleteAsync(scope, turn, 0, "{}", "duplicate"), "duplicate completion cannot replace accepted result");
var accepted = (await store.ReadAsync(scope))!;
Check(accepted.Revision == 1 && accepted.Result == response.Text && client.Calls == 1, "accepted result remains available without model replay");
Check(await store.BeginAsync(scope, "turn-two", 1), "explicit next turn admits after preceding commit");
using var saved = JsonDocument.Parse(accepted.Session!);
var restored = await agent.DeserializeSessionAsync(saved.RootElement);
var continued = await agent.RunAsync("continue with a headline", restored);
Check(client.LastHistory.Any(x => x.Role == ChatRole.User && x.Text == "moss gardens"), "real MAF session restores previous accepted conversation");
Check(await store.CompleteAsync(scope, "turn-two", 1, (await agent.SerializeSessionAsync(restored)).GetRawText(), continued.Text), "second turn advances exact revision");
Check(!await store.CompleteAsync(scope, turn, 0, serialized.GetRawText(), "late"), "late first-turn response cannot replace later accepted turn");

Check(await store.BeginAsync(scope, "cancelled-turn", 2), "cancel experiment admits exact turn");
Check(await store.StopAsync(scope, "cancelled-turn", "Cancelled"), "cancellation invalidates active completion authority");
Check(!await store.CompleteAsync(scope, "cancelled-turn", 2, "{}", "late cancelled"), "completion after cancellation is refused");
Check((await store.ReadAsync(scope))!.Session == (await agent.SerializeSessionAsync(restored)).GetRawText(), "cancelled turn preserves previous accepted session");

var uncertain = scope with { Conversation = "uncertain" };
await store.CreateAsync(uncertain);
Check(await store.BeginAsync(uncertain, "lost-response", 0), "ambiguous experiment admits a turn");
Check(await store.StopAsync(uncertain, "lost-response", "OutcomeUnknown"), "lost provider response records uncertainty separately from success");
Check(!await store.BeginAsync(uncertain, "automatic-retry", 0), "unknown outcome does not automatically reissue provider work");
Check(!await store.CompleteAsync(uncertain, "lost-response", 0, "{}", "late response"), "late unknown response cannot silently become accepted");
Check((await store.ReadAsync(uncertain)) is { Status: "OutcomeUnknown", Result: null }, "ambiguous result is visibly unresolved");

await File.WriteAllTextAsync(Path.Combine(directory, "lifecycle.json"), JsonSerializer.Serialize(new
{
    checks, passed = checks.Count, modelCalls = client.Calls, liveModel = false,
    limitations = new[] { "Research SQLite CAS store, not Elsa checkpoint integration", "No process-kill or external-effect exactly-once proof", "Cancellation authority is probed; SDK cancellation is covered by #3529", "Unknown outcomes require an explicit production recovery decision" }
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { checks, passed = checks.Count, modelCalls = client.Calls, liveModel = false }));

sealed record Scope(string Tenant, string AgentVersion, string Conversation);
sealed record Snapshot(long Revision, string Status, string? Session, string? Result);

sealed class ConversationPrototype(string path)
{
    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False;Default Timeout=30");
        await connection.OpenAsync(); return connection;
    }
    public async Task InitializeAsync()
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Conversations (tenant TEXT, agent TEXT, conversation TEXT, revision INTEGER NOT NULL, status TEXT NOT NULL, turn TEXT, session TEXT, result TEXT, PRIMARY KEY(tenant,agent,conversation))";
        await command.ExecuteNonQueryAsync();
    }
    public async Task CreateAsync(Scope scope) => await WriteAsync(scope,
        "INSERT INTO Conversations(tenant,agent,conversation,revision,status) VALUES($tenant,$agent,$conversation,0,'Ready')");
    public Task<bool> BeginAsync(Scope scope, string turn, long revision) => WriteAsync(scope,
        "UPDATE Conversations SET status='Running',turn=$turn WHERE tenant=$tenant AND agent=$agent AND conversation=$conversation AND revision=$revision AND status='Ready'", turn, revision);
    public Task<bool> CompleteAsync(Scope scope, string turn, long revision, string session, string result) => WriteAsync(scope,
        "UPDATE Conversations SET revision=revision+1,status='Ready',turn=NULL,session=$session,result=$result WHERE tenant=$tenant AND agent=$agent AND conversation=$conversation AND revision=$revision AND status='Running' AND turn=$turn", turn, revision, session, result);
    public Task<bool> StopAsync(Scope scope, string turn, string status) => WriteAsync(scope,
        "UPDATE Conversations SET status=$status,turn=NULL WHERE tenant=$tenant AND agent=$agent AND conversation=$conversation AND status='Running' AND turn=$turn", turn, status: status);
    private async Task<bool> WriteAsync(Scope scope, string sql, string? turn = null, long revision = 0, string? session = null, string? result = null, string? status = null)
    {
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = sql; Bind(command, scope);
        command.Parameters.AddWithValue("$turn", (object?)turn ?? DBNull.Value);
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$session", (object?)session ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", (object?)result ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (object?)status ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync() == 1;
    }
    public async Task<Snapshot?> ReadAsync(Scope scope)
    {
        await using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT revision,status,session,result FROM Conversations WHERE tenant=$tenant AND agent=$agent AND conversation=$conversation";
        Bind(command, scope); using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)) : null;
    }
    private static void Bind(SqliteCommand command, Scope scope)
    {
        command.Parameters.AddWithValue("$tenant", scope.Tenant); command.Parameters.AddWithValue("$agent", scope.AgentVersion); command.Parameters.AddWithValue("$conversation", scope.Conversation);
    }
}
sealed class Client : IChatClient
{
    public int Calls { get; private set; }
    public ChatMessage[] LastHistory { get; private set; } = [];
    public void Dispose() {}
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Calls++; LastHistory = messages.ToArray();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"article\":\"draft " + Calls + "\"}")));
    }
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
