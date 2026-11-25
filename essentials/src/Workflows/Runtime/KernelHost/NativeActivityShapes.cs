using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>How one compiled input of an activity becomes a Native input.</summary>
internal abstract record InputRule
{
    /// <summary>Bound to the Native input of this name, whatever the binding's source.</summary>
    public sealed record Pass(string Native) : InputRule;

    /// <summary>
    /// Bound to the Native input of this name from a literal only, whose value is converted to the Native shape: the old
    /// core and Native type it differently. Convert returns null when the value has no Native form.
    /// </summary>
    public sealed record Literal(string Native, Func<JsonNode?, JsonNode?> Convert, string Expected) : InputRule;

    /// <summary>
    /// An input the Native activity does not model. The binding is accepted, and dropped, when it is absent or a literal that
    /// Native behaves as: <see cref="Accepts"/> says so; anything else is refused.
    /// </summary>
    public sealed record Unmodelled(Func<JsonNode?, bool> Accepts, string Reason) : InputRule;
}

/// <summary>Where a compiled result projection is in a Native result.</summary>
/// <param name="Path">The Native path, "" for the whole result.</param>
/// <param name="Members">For a projection whose members Native names otherwise: the old member name to the Native one.</param>
internal sealed record ResultProjection(string Path, IReadOnlyDictionary<string, string>? Members = null);

/// <summary>
/// What the mapper knows about each release 1 activity: how its compiled inputs, keyed as the old core's activity declares
/// them, become Native inputs, the Native inputs an absent one implies, and where its result projections are. An input
/// whose key names a Native input (ignoring case) passes through unless a rule says otherwise; an input with neither is
/// refused, so a behaviour the kernel does not model is never dropped without a word.
/// </summary>
internal sealed class NativeActivityShape
{
    private static readonly Dictionary<Type, NativeActivityShape> Shapes = Build();

    private readonly Dictionary<string, string> _nativeInputs;

    private NativeActivityShape(Type type)
    {
        _nativeInputs = type.GetProperties()
            .Where(property => property.IsDefined(typeof(Native.InputAttribute), inherit: true))
            .ToDictionary(property => property.Name, property => property.Name, StringComparer.OrdinalIgnoreCase);
    }

    public Dictionary<string, InputRule> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A Native input to bind to a literal when the compiled node does not bind the input it comes from: the old default differs.</summary>
    public Dictionary<string, (string OldInput, JsonNode? Value)> AbsentDefaults { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ResultProjection> Results { get; } = new(StringComparer.Ordinal);

    public static NativeActivityShape For(Type type) =>
        Shapes.TryGetValue(type, out var shape) ? shape : throw new InvalidOperationException($"No activity shape for {type.Name}.");

    /// <summary>The rule for a compiled input, or null when the Native activity has no such input.</summary>
    public InputRule? RuleFor(string input) =>
        Rules.TryGetValue(input, out var rule) ? rule : _nativeInputs.TryGetValue(input, out var native) ? new InputRule.Pass(native) : null;

    private static Dictionary<Type, NativeActivityShape> Build()
    {
        var shapes = new Dictionary<Type, NativeActivityShape>();
        NativeActivityShape Add(Type type)
        {
            var shape = new NativeActivityShape(type);
            shapes.Add(type, shape);
            return shape;
        }

        foreach (var type in new[]
                 {
                     typeof(Native.Sequence), typeof(Native.If), typeof(Native.Switch), typeof(Native.For), typeof(Native.While),
                     typeof(Native.Do), typeof(Native.Parallel), typeof(Native.Break), typeof(Native.Flowchart), typeof(Native.Complete),
                     typeof(Native.WriteLine), typeof(Native.WriteLines), typeof(Native.Fault), typeof(Native.WriteHttpResponse),
                 })
            Add(type);

        var forEach = Add(typeof(Native.ForEach));
        forEach.Rules["Collection"] = new InputRule.Pass("Items");
        // Native always declares CurrentIndex beside CurrentItem; a body that does not read it is unaffected.
        forEach.Rules["ExposeIndex"] = new InputRule.Unmodelled(_ => true, "");

        Add(typeof(Native.Inline)).Results["$result"] = new("");
        Add(typeof(Native.RunJavaScript)).Results["result"] = new("Value");

        var @event = Add(typeof(Native.Event));
        @event.Rules["CorrelationId"] = new InputRule.Unmodelled(IsNull, "the kernel matches events by name only, without a correlation id");
        @event.Rules["CanStartWorkflow"] = new InputRule.Unmodelled(_ => true, "");
        @event.Rules["ExpiresAt"] = new InputRule.Unmodelled(IsNull, "the kernel has no match deadline for an event wait");
        @event.Results["Result"] = new("EventName");
        @event.Results["Payload"] = new("Payload");

        var publish = Add(typeof(Native.PublishEvent));
        publish.Rules["CorrelationId"] = new InputRule.Unmodelled(IsNull, "the kernel publishes events by name only, without a correlation id");
        publish.Rules["IsLocalEvent"] = new InputRule.Unmodelled(value => IsNull(value) || IsFalse(value), "the kernel has no local events");

        Add(typeof(Native.Delay)).Rules["Duration"] = new InputRule.Literal("Milliseconds", Milliseconds, "a literal time span");

        Add(typeof(Native.Timer)).Results["Result"] = new("Interval");
        Add(typeof(Native.Cron)).Results["Result"] = new("Expression");

        var dispatch = Add(typeof(Native.DispatchWorkflow));
        // The definition id as given: the host resolves it to the artifact it serves when the child starts
        // (NativeExecutionOptions.ServedWorkflow).
        dispatch.Rules["WorkflowDefinitionId"] = new InputRule.Pass("Workflow");
        dispatch.Rules["Inputs"] = new InputRule.Pass("Input");
        dispatch.Rules["CancelChildOnParentCancellation"] = new InputRule.Unmodelled(value => IsNull(value) || IsTrue(value),
            "the kernel always cancels a waited child with its parent");
        dispatch.Rules["CorrelationId"] = new InputRule.Unmodelled(IsNull, "the kernel starts a child without a correlation id");
        dispatch.Results["Result"] = new("Output");

        var endpoint = Add(typeof(Native.HttpEndpoint));
        endpoint.Rules["SupportedMethods"] = new InputRule.Literal("Method", OneMethod, "a literal list of one HTTP method");
        endpoint.AbsentDefaults["Method"] = ("SupportedMethods", JsonValue.Create("GET"));
        // A kernel host starts a workflow from its start route whatever this says; the coordinator decides which routes serve.
        endpoint.Rules["CanStartWorkflow"] = new InputRule.Unmodelled(_ => true, "");
        endpoint.Rules["Authorize"] = new InputRule.Unmodelled(value => IsNull(value) || IsFalse(value), "the kernel's HTTP edge does not authorize routes");
        endpoint.Rules["Policy"] = new InputRule.Unmodelled(IsNull, "the kernel's HTTP edge does not authorize routes");
        endpoint.Rules["RequestTimeout"] = new InputRule.Unmodelled(IsNull, "the kernel's reply deadline is the route timeout");
        endpoint.Rules["RequestSizeLimit"] = new InputRule.Unmodelled(IsNull, "the kernel's HTTP edge sets the request size limit");
        endpoint.Rules["ResponseMode"] = new InputRule.Unmodelled(value => IsNull(value) || IsSync(value), "the kernel answers a request from the workflow (Sync) only");
        endpoint.Results["Request"] = new("", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["path"] = "Path", ["method"] = "Method", ["headers"] = "Headers", ["query"] = "Query", ["body"] = "Body", ["routeData"] = "RouteValues",
        });
        endpoint.Results["RouteData"] = new("RouteValues");
        endpoint.Results["ParsedContent"] = new("Body");

        var send = Add(typeof(Native.SendHttpRequest));
        send.Rules["Content"] = new InputRule.Pass("Body");
        send.Rules["RequestHeaders"] = new InputRule.Pass("Headers");
        send.Rules["Connection"] = new InputRule.Literal("Connection", ConnectionName, "a literal Connection reference");
        send.AbsentDefaults["Method"] = ("Method", JsonValue.Create("GET"));
        send.Results["StatusCode"] = new("StatusCode");
        send.Results["ResponseBody"] = new("Body");
        send.Results["ResponseHeaders"] = new("Headers");

        // A GraphActivity's inputs and results are its boundary's own (NativeExecutableMapper.MapGraph).
        Add(typeof(Native.GraphActivity)).Results["$result"] = new("");
        return shapes;
    }

    private static bool IsNull(JsonNode? value) => value is null;
    private static bool IsTrue(JsonNode? value) => value is JsonValue v && v.GetValueKind() == JsonValueKind.True;
    private static bool IsFalse(JsonNode? value) => value is JsonValue v && v.GetValueKind() == JsonValueKind.False;

    // ResponseMode serializes as its name or its number; Sync is 1.
    private static bool IsSync(JsonNode? value) => value is JsonValue v &&
        (v.TryGetValue<string>(out var name) && string.Equals(name, "Sync", StringComparison.OrdinalIgnoreCase) || v.TryGetValue<int>(out var number) && number == 1);

    private static JsonNode? Milliseconds(JsonNode? value)
    {
        if (value is not JsonValue v || !v.TryGetValue<string>(out var text)) return null;
        if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span))
        {
            try { span = XmlConvert.ToTimeSpan(text); }
            catch (FormatException) { return null; }
        }
        return span < TimeSpan.Zero ? null : JsonValue.Create((long)span.TotalMilliseconds);
    }

    private static JsonNode? OneMethod(JsonNode? value) =>
        value is JsonArray { Count: 1 } methods && methods[0] is JsonValue method && method.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name)
            ? JsonValue.Create(name.Trim().ToUpperInvariant())
            : null;

    // A Connection reference is its name, or the reference object the designer stores ({ "connection": name, "type": ... }).
    private static JsonNode? ConnectionName(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name) => JsonValue.Create(name),
        JsonObject o when o["connection"] is JsonValue v && v.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name) => JsonValue.Create(name),
        _ => null,
    };
}
