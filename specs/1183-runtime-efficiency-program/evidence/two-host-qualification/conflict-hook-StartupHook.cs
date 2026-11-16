using System.Collections;
using System.Text.Json;

// Diagnostics-only: records which tracked entities a first-chance DbUpdateConcurrencyException names.
public static class StartupHook
{
    public static void Initialize()
    {
        var path = Environment.GetEnvironmentVariable("ELSA_EFFICIENCY_CONFLICT_LOG");
        if (path is null) return;
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (e.Exception.GetType().Name != "DbUpdateConcurrencyException") return;
            try
            {
                var entries = new List<object>();
                if (e.Exception.GetType().GetProperty("Entries")?.GetValue(e.Exception) is IEnumerable list)
                    foreach (var entry in list)
                    {
                        var type = entry.GetType();
                        var entity = type.GetProperty("Entity")!.GetValue(entry)!;
                        object? original = null, current = null;
                        try
                        {
                            var property = type.GetMethod("Property", [typeof(string)])!.Invoke(entry, ["Revision"])!;
                            original = property.GetType().GetProperty("OriginalValue")!.GetValue(property);
                            current = property.GetType().GetProperty("CurrentValue")!.GetValue(property);
                        }
                        catch { }
                        entries.Add(new { entity = entity.GetType().Name, state = type.GetProperty("State")!.GetValue(entry)!.ToString(), revisionOriginal = original, revisionCurrent = current });
                    }
                var line = JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, entries, stack = Environment.StackTrace.Split('\n').Where(l => l.Contains("Elsa.")).Take(12) });
                lock (typeof(StartupHook)) File.AppendAllText(path, line + "\n");
            }
            catch { }
        };
    }
}
