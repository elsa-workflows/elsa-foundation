using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// One-off non-timing attribution. No EF/package reference or change to the host's logger/exporter configuration.
public static class StartupHook
{
    public static void Initialize()
    {
        var output = Environment.GetEnvironmentVariable("ELSA_SQL_CAPTURE_OUTPUT");
        var control = Environment.GetEnvironmentVariable("ELSA_SQL_CAPTURE_CONTROL");
        if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(control) || !Path.IsPathFullyQualified(output) || !Path.IsPathFullyQualified(control))
            throw new InvalidOperationException("Owned absolute capture paths are required.");
        var observer = new Capture(output, control);
        DiagnosticListener.AllListeners.Subscribe(observer);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => observer.Finish();
    }
    private sealed class Capture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        private readonly string output;
        private readonly string control;
        private readonly StreamWriter writer;
        private readonly object gate = new();
        private long rows;
        private long bytes;
        private long dropped;
        private string? error;
        private bool finished;
        public Capture(string outputPath, string controlPath)
        {
            output = outputPath; control = controlPath;
            writer = new StreamWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore") listener.Subscribe(this);
        }
        private static object? Property(object? value, string key) => value?.GetType().GetProperty(key, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
        public void OnNext(KeyValuePair<string, object?> item)
        {
            var kind = item.Key[(item.Key.LastIndexOf('.') + 1)..];
            if (kind is not ("CommandExecuted" or "CommandError" or "TransactionStarted" or "TransactionCommitted" or "CreatedSavepoint" or "ReleasedSavepoint" or "RolledBackToSavepoint")) return;
            try
            {
                if (!File.Exists(control)) return;
                string phase;
                try { phase = File.ReadAllText(control); }
                catch (FileNotFoundException) { return; }
                if (!Regex.IsMatch(phase, "^[a-z0-9-]{1,32}$")) throw new InvalidDataException("Invalid phase.");
                var command = Property(item.Value, "Command") as DbCommand;
                var text = command?.CommandText;
                var truncated = text?.Length > 65_536;
                if (truncated) text = text![..65_536];
                var record = JsonSerializer.Serialize(new
                {
                    atUtc = DateTimeOffset.UtcNow, phase, kind, traceId = Activity.Current?.TraceId.ToString(),
                    contextType = Property(item.Value, "Context")?.GetType().FullName,
                    connectionType = command?.Connection?.GetType().FullName,
                    durationMilliseconds = Property(item.Value, "Duration") is TimeSpan duration ? duration.TotalMilliseconds : (double?)null,
                    sql = text, sqlTruncated = truncated, exceptionType = Property(item.Value, "Exception")?.GetType().FullName
                });
                lock (gate)
                {
                    if (finished) return;
                    var size = Encoding.UTF8.GetByteCount(record) + 1;
                    if (rows >= 100_000 || bytes + size > 32 * 1024 * 1024) { dropped++; return; }
                    writer.WriteLine(record); rows++; bytes += size;
                }
            }
            catch (Exception failure)
            {
                lock (gate) error ??= failure.GetType().FullName;
            }
        }
        public void OnCompleted() { }
        public void OnError(Exception failure) { lock (gate) error ??= failure.GetType().FullName; }
        public void Finish()
        {
            lock (gate)
            {
                if (finished) return; finished = true;
                try { writer.Dispose(); }
                catch (Exception failure) { error ??= failure.GetType().FullName; }
                File.WriteAllText(output + ".receipt.json", JsonSerializer.Serialize(new
                {
                    complete = error is null && dropped == 0, rows, bytes, dropped, errorType = error,
                    boundary = "EF diagnostic events only; no parameter values or connection strings are read. Instrumented runs are excluded from timing."
                }));
            }
        }
    }
}
