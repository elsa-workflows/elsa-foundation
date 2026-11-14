using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kernel.Native;

internal sealed record LexicalResource(string Owner, string? Parent, Dictionary<string, JsonNode?> Values);

public sealed partial class NativeState
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal Dictionary<string, LexicalResource>? LexicalScopes { get; set; }
}
public sealed partial class Frame
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? LexicalScope { get; set; }
}
public sealed partial class Work
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? LexicalScope { get; set; }
}

internal static class LexicalScopes
{
    internal static void Terminate(NativeState state)
    {
        // Forced workflow termination retains its other forensic state but retires all lexical handles/resources.
        state.LexicalScopes = null;
        foreach (var frame in state.Frames.Values) frame.LexicalScope = null;
        foreach (var work in state.Agenda) work.LexicalScope = null;
    }
    internal static bool Present(NativeState state) => state.LexicalScopes is not null || state.Frames.Values.Any(f => f.LexicalScope is not null) || state.Agenda.Any(w => w.LexicalScope is not null);
    internal static bool Within(NativeState state, string frame, string ancestor)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (string? id = frame; id is not null && state.Frames.TryGetValue(id, out var f); id = f.Parent)
        {
            if (!seen.Add(id)) throw new InvalidOperationException("Lexical scope frame ancestry is cyclic.");
            if (id == ancestor) return true;
        }
        return false;
    }
    internal static LexicalResource Accessible(NativeState state, string frame, string id)
    {
        if (state.LexicalScopes?.GetValueOrDefault(id) is not { } scope || !Within(state, frame, scope.Owner))
            throw new InvalidOperationException("Lexical scope is missing or inaccessible.");
        return scope;
    }
    internal static void Validate(NativeState state)
    {
        foreach (var (id, scope) in state.LexicalScopes ?? [])
        {
            if (string.IsNullOrEmpty(id) || !state.Frames.ContainsKey(scope.Owner) || scope.Values is null)
                throw new InvalidOperationException("Lexical scope owner or values are missing.");
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            for (var parent = scope.Parent; parent is not null; parent = Accessible(state, scope.Owner, parent).Parent)
                if (!seen.Add(parent)) throw new InvalidOperationException("Lexical scope ancestry is cyclic.");
        }
        foreach (var f in state.Frames.Values)
            if (f.LexicalScope is { } id) _ = Accessible(state, f.Id, id);
        foreach (var w in state.Agenda)
            if (w.LexicalScope is { } id)
            {
                if (w.Resume || w.Parent is null) throw new InvalidOperationException("Invalid lexical scope work reference.");
                _ = Accessible(state, w.Parent, id);
            }
    }
}

internal sealed partial class Burst
{
    internal string CreateLexicalScope(Frame owner, IReadOnlyDictionary<string, JsonNode?> values, string? parent)
    {
        if (parent is not null) _ = LexicalScopes.Accessible(_s, owner.Id, parent);
        // Clone before allocating/mutating the checkpoint, including values with nested mutable JSON.
        var declared = values.ToDictionary(p => p.Key, p => p.Value?.DeepClone(), StringComparer.Ordinal);
        var id = NewId("s");
        (_s.LexicalScopes ??= new(StringComparer.Ordinal)).Add(id, new(owner.Id, parent, declared));
        return id;
    }
    internal void ReleaseLexicalScope(Frame owner, string id)
    {
        var scope = LexicalScopes.Accessible(_s, owner.Id, id);
        if (scope.Owner != owner.Id) throw new InvalidOperationException("Only the lexical scope owner can release it.");
        if (_s.LexicalScopes!.Values.Any(s => s.Parent == id) || _s.Frames.Values.Any(f => f.LexicalScope == id) || _s.Agenda.Any(w => w.LexicalScope == id))
            throw new InvalidOperationException("Lexical scope still has children or work.");
        RemoveLexicalScope(id);
    }
    private void RemoveLexicalScope(string id)
    {
        _s.LexicalScopes!.Remove(id);
        if (_s.LexicalScopes.Count == 0) _s.LexicalScopes = null;
    }
    private void DisposeLexicalScopes(string owner)
    {
        if (_s.LexicalScopes is null) return;
        var owned = _s.LexicalScopes.Where(p => p.Value.Owner == owner).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        while (owned.Count != 0)
        {
            var leaves = owned.Where(id => !_s.LexicalScopes!.Values.Any(s => s.Parent == id)).ToArray();
            if (leaves.Length == 0 || leaves.Any(id => _s.Frames.Values.Any(f => f.LexicalScope == id) || _s.Agenda.Any(w => w.LexicalScope == id)))
                throw new InvalidOperationException("Removed owner retains lexical scope consumers.");
            foreach (var id in leaves) { RemoveLexicalScope(id); owned.Remove(id); }
        }
    }
    private IEnumerable<Dictionary<string, JsonNode?>> VisibleScopes(Frame? frame)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (; frame is not null; frame = ParentOf(frame))
        {
            if (frame.LiveScope is { } live) yield return live;
            if (frame.Scope is { } local) yield return local;
            for (var id = frame.LexicalScope; id is not null && seen.Add(id);)
            {
                // An executing intrinsic/leaf is not stored as a live frame; its scheduled parent owns access.
                var access = _s.Frames.ContainsKey(frame.Id) ? frame.Id : frame.Parent ?? frame.Id;
                var scope = LexicalScopes.Accessible(_s, access, id);
                yield return scope.Values;
                id = scope.Parent;
            }
        }
    }
}

public sealed partial class CompositeContext
{
    internal string CreateLexicalScope(IReadOnlyDictionary<string, JsonNode?> values, string? parent = null) => _burst.CreateLexicalScope(_frame, values, parent);
    internal string ScheduleInScope(Node child, string scope, string? tag = null) => _burst.Schedule(_frame, child, tag, null, scope);
    internal void ReleaseLexicalScope(string scope) => _burst.ReleaseLexicalScope(_frame, scope);
}
