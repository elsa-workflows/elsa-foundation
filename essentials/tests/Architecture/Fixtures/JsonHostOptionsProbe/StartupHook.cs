using System.Diagnostics;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

// A test-only .NET startup hook observes the actual application's container without modifying its registrations.
public static class StartupHook
{
    private static readonly List<IDisposable> Subscriptions = [];

    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("ELSA_JSON_HOST_OPTIONS_RECEIPT") is not { Length: > 0 } path)
            throw new InvalidOperationException("A host JSON options probe requires its isolated receipt path.");
        Subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(listener =>
        {
            if (listener.Name != "Microsoft.Extensions.Hosting") return;
            Subscriptions.Add(listener.Subscribe(new Observer<KeyValuePair<string, object?>>(item =>
            {
                if (item.Key != "HostBuilt" || item.Value is not IHost host) return;
                Inspect(host, path);
                // The probe is complete at HostBuilt. Stop cooperatively once normal host startup finishes.
                var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                lifetime.ApplicationStarted.Register(lifetime.StopApplication);
            })));
        })));
    }

    private static void Inspect(IHost host, string path)
    {
        var errors = new List<string>();
        var options = host.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        void Check(bool passes, string message) { if (!passes) errors.Add(message); }
        Check(ReferenceEquals(options.PropertyNamingPolicy, JsonNamingPolicy.CamelCase), "PropertyNamingPolicy");
        Check(options.DictionaryKeyPolicy is null, "DictionaryKeyPolicy");
        Check(!options.PropertyNameCaseInsensitive, "PropertyNameCaseInsensitive");
        Check(options.DefaultIgnoreCondition == JsonIgnoreCondition.Never, "DefaultIgnoreCondition");
        Check(options.NumberHandling == JsonNumberHandling.Strict, "NumberHandling");
        Check(ReferenceEquals(options.Encoder, JavaScriptEncoder.UnsafeRelaxedJsonEscaping), "Encoder");
        Check(!options.AllowTrailingCommas && options.ReadCommentHandling == JsonCommentHandling.Disallow, "JSON syntax");
        Check(options.Converters.Any(converter => converter.GetType().FullName == "Elsa.Serialization.Core.RawJsonElementConverter"), "RawJsonElementConverter");
        foreach (var type in new[] { typeof(DateTime), typeof(DateTimeOffset) })
        {
            try
            {
                JsonSerializer.Deserialize("\"2026-01-15T09:30:00\"", type, options);
                errors.Add(type.Name + " admitted an offset-less date");
            }
            catch (JsonException error)
            {
                Check(error.GetType().GetProperty("Code")?.GetValue(error) as string == "json-datetime-offset-missing", type.Name + " refusal code");
            }
        }
        const string raw = """{ "OrderID":9007199254740993, "nil":null, "text":"Ren\u00e9 <+>", "amount":1.00 }""";
        using var source = JsonDocument.Parse(raw);
        var body = new ProbeBody { Content = source.RootElement, Values = new() { ["OrderTotal"] = source.RootElement } };
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(body, options));
        Check(wire.RootElement.TryGetProperty("nullable", out var nil) && nil.ValueKind == JsonValueKind.Null, "Explicit null member");
        Check(wire.RootElement.GetProperty("content").GetRawText() == raw, "Raw JSON passthrough");
        Check(wire.RootElement.GetProperty("values").TryGetProperty("OrderTotal", out var carried) && carried.GetRawText() == raw, "Dictionary key and carried JSON");
        Check(JsonSerializer.Serialize("René <+>", options) == "\"René <+>\"", "Changed data relaxed encoder");
        Check(JsonSerializer.Serialize(new DateTime(2026, 1, 15, 9, 30, 0, DateTimeKind.Unspecified), options) == "\"2026-01-15T09:30:00Z\"", "API unspecified write mode");
        var entry = Assembly.GetEntryAssembly()!;
        var receipt = JsonSerializer.Serialize(new
        {
            Host = entry.GetName().Name,
            Source = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Errors = errors,
            ResolverTypes = options.TypeInfoResolverChain.Select(resolver => resolver.GetType().FullName).ToArray(),
            Wire = wire.RootElement.Clone()
        });
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(output);
        writer.Write(receipt);
    }

    private sealed class ProbeBody
    {
        public JsonElement Content { get; init; }
        public string? Nullable { get; init; }
        public Dictionary<string, JsonElement> Values { get; init; } = [];
    }

    private sealed class Observer<T>(Action<T> next) : IObserver<T>
    {
        public void OnNext(T value) => next(value);
        public void OnError(Exception error) => throw error;
        public void OnCompleted() { }
    }
}
